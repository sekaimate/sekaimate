using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class BasisHeadlessBuild
{
    private const string AddressablesBuildWithPlayerPreferenceKey = "Addressables.BuildAddressablesWithPlayerBuild";

    public static void BuildLinuxServer()
    {
        BuildServer(BuildTarget.StandaloneLinux64);
    }

    public static void BuildWindowsServer()
    {
        BuildServer(BuildTarget.StandaloneWindows64);
    }

    public static void BuildWeb()
    {
        BuildWeb(false);
    }

    public static void BuildWebE2E()
    {
        BuildWeb(true);
    }

    private static void BuildWeb(bool developmentBuild)
    {
        string buildPath = RequireArgument("customBuildPath");
        if (Directory.Exists(buildPath) && Directory.EnumerateFileSystemEntries(buildPath).Any())
        {
            throw new BuildFailedException($"Web build output directory must be empty: {buildPath}");
        }

        EnsureActiveBuildTarget(BuildTarget.WebGL);
        BuildPlayer(BuildTarget.WebGL, CreateWebBuildPlayerOptions(buildPath, developmentBuild));
    }

    public static BuildPlayerOptions CreateWebBuildPlayerOptions(string buildPath, bool developmentBuild = false)
    {
        return new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = buildPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = developmentBuild ? BuildOptions.Development : BuildOptions.None
        };
    }

    private static void BuildServer(BuildTarget target)
    {
        string buildPath = RequireArgument("customBuildPath");
        string standaloneSubtargetArg = GetArgument("standaloneBuildSubtarget") ?? "Server";
        string linuxArchitectureArg = GetArgument("linuxArchitecture");

        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
        EnsureActiveBuildTarget(target);

        StandaloneBuildSubtarget standaloneSubtarget = ParseStandaloneSubtarget(standaloneSubtargetArg);
        EditorUserBuildSettings.standaloneBuildSubtarget = standaloneSubtarget;
        if (target == BuildTarget.StandaloneLinux64)
        {
            int linuxArchitecture = ParseLinuxArchitecture(linuxArchitectureArg);
            PlayerSettings.SetArchitecture(NamedBuildTarget.FromBuildTargetGroup(targetGroup), linuxArchitecture);
            Debug.Log($"[BasisHeadlessBuild] Linux architecture(set)={linuxArchitecture}");
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = buildPath,
            target = target,
            targetGroup = targetGroup,
            subtarget = (int)standaloneSubtarget,
            options = BuildOptions.None
        };

        BuildPlayer(target, options);
    }

    private static void BuildPlayer(BuildTarget target, BuildPlayerOptions options)
    {
        string projectPath = GetArgument("projectPath") ?? Directory.GetCurrentDirectory();

        Debug.Log($"[BasisHeadlessBuild] Starting {target} build");
        Debug.Log($"[BasisHeadlessBuild] projectPath={projectPath}");
        Debug.Log($"[BasisHeadlessBuild] buildPath={options.locationPathName}");
        Debug.Log($"[BasisHeadlessBuild] activeBuildTarget={EditorUserBuildSettings.activeBuildTarget}");

        if (!BuildPipeline.IsBuildTargetSupported(options.targetGroup, target))
        {
            throw new BuildFailedException($"Build target {target} is not supported in this editor.");
        }

        EnsureBuildDirectory(options.locationPathName);
        LogEnabledScenes();

        AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
        bool restoreBuildAddressablesWithPlayerBuild = false;
        AddressableAssetSettings.PlayerBuildOption originalBuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.PreferencesValue;
        if (addressableSettings != null)
        {
            originalBuildAddressablesWithPlayerBuild = addressableSettings.BuildAddressablesWithPlayerBuild;
            restoreBuildAddressablesWithPlayerBuild = true;
            if (ShouldBuildAddressablesWithPlayerBuild(originalBuildAddressablesWithPlayerBuild))
            {
                BuildAddressables(target, addressableSettings);
            }
            addressableSettings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            Debug.Log($"[BasisHeadlessBuild] Overriding BuildAddressablesWithPlayerBuild: {originalBuildAddressablesWithPlayerBuild} -> {addressableSettings.BuildAddressablesWithPlayerBuild}");
        }
        else
        {
            Debug.LogWarning("[BasisHeadlessBuild] Addressables settings not found; continuing without Addressables override.");
        }

        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[BasisHeadlessBuild] Build result={report.summary.result}");
            Debug.Log($"[BasisHeadlessBuild] Build output path={report.summary.outputPath}");
            Debug.Log($"[BasisHeadlessBuild] Build totalErrors={report.summary.totalErrors}");
            Debug.Log($"[BasisHeadlessBuild] Build totalWarnings={report.summary.totalWarnings}");

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Player build failed: {report.summary.result}");
            }

            if (target == BuildTarget.WebGL)
            {
                ValidateWebBuildOutput(options.locationPathName);
            }
        }
        finally
        {
            if (restoreBuildAddressablesWithPlayerBuild)
            {
                addressableSettings.BuildAddressablesWithPlayerBuild = originalBuildAddressablesWithPlayerBuild;
                Debug.Log($"[BasisHeadlessBuild] Restored BuildAddressablesWithPlayerBuild={addressableSettings.BuildAddressablesWithPlayerBuild}");
            }
        }
    }

    public static string[] FindMissingWebBuildArtifacts(IEnumerable<string> relativeFilePaths)
    {
        string[] paths = relativeFilePaths
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
        var missing = new List<string>();

        RequireArtifact(paths, missing, "index.html", path => path == "index.html");
        RequireArtifact(paths, missing, "TemplateData", path => path.StartsWith("TemplateData/", StringComparison.Ordinal));
        RequireArtifact(paths, missing, "loader.js", path => path.StartsWith("Build/", StringComparison.Ordinal) && path.EndsWith(".loader.js", StringComparison.Ordinal));
        RequireArtifact(paths, missing, "framework.js", path => path.StartsWith("Build/", StringComparison.Ordinal) && (path.EndsWith(".framework.js", StringComparison.Ordinal) || path.EndsWith(".framework.js.gz", StringComparison.Ordinal) || path.EndsWith(".framework.js.br", StringComparison.Ordinal)));
        RequireArtifact(paths, missing, "wasm", path => path.StartsWith("Build/", StringComparison.Ordinal) && (path.EndsWith(".wasm", StringComparison.Ordinal) || path.EndsWith(".wasm.gz", StringComparison.Ordinal) || path.EndsWith(".wasm.br", StringComparison.Ordinal)));
        RequireArtifact(paths, missing, "data", path => path.StartsWith("Build/", StringComparison.Ordinal) && (path.EndsWith(".data", StringComparison.Ordinal) || path.EndsWith(".data.gz", StringComparison.Ordinal) || path.EndsWith(".data.br", StringComparison.Ordinal)));
        RequireArtifact(paths, missing, "Addressables settings", path => path == "StreamingAssets/aa/settings.json");
        RequireArtifact(paths, missing, "Addressables catalog", path => path == "StreamingAssets/aa/catalog.bin");
        RequireArtifact(paths, missing, "Addressables bundle", path => path.StartsWith("StreamingAssets/aa/", StringComparison.Ordinal) && path.EndsWith(".bundle", StringComparison.Ordinal));

        return missing.ToArray();
    }

    private static void ValidateWebBuildOutput(string buildPath)
    {
        string[] relativeFilePaths = Directory
            .EnumerateFiles(buildPath, "*", SearchOption.AllDirectories)
            .Where(path => new FileInfo(path).Length > 0)
            .Select(path => Path.GetRelativePath(buildPath, path))
            .ToArray();
        string[] missing = FindMissingWebBuildArtifacts(relativeFilePaths);

        if (missing.Length > 0)
        {
            throw new BuildFailedException($"Web build output is incomplete. Missing: {string.Join(", ", missing)}");
        }
    }

    private static void RequireArtifact(string[] paths, List<string> missing, string name, Func<string, bool> predicate)
    {
        if (!paths.Any(predicate))
        {
            missing.Add(name);
        }
    }

    private static void EnsureActiveBuildTarget(BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target)
        {
            return;
        }

        BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
        {
            throw new BuildFailedException($"Failed to switch active build target to {target}.");
        }
    }

    private static bool ShouldBuildAddressablesWithPlayerBuild(AddressableAssetSettings.PlayerBuildOption option)
    {
        switch (option)
        {
            case AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer:
                return true;
            case AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer:
                return false;
            case AddressableAssetSettings.PlayerBuildOption.PreferencesValue:
                return EditorPrefs.GetBool(AddressablesBuildWithPlayerPreferenceKey, true);
            default:
                return false;
        }
    }

    private static void BuildAddressables(BuildTarget target, AddressableAssetSettings settings)
    {
        Debug.Log($"[BasisHeadlessBuild] Building Addressables explicitly for {target}. Active profile={settings.activeProfileId}, builderIndex={settings.ActivePlayerDataBuilderIndex}");
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        Debug.Log($"[BasisHeadlessBuild] Addressables result.Error='{result.Error}'");
        Debug.Log($"[BasisHeadlessBuild] Addressables outputPath='{result.OutputPath}'");

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            throw new BuildFailedException($"Addressables build failed: {result.Error}");
        }
    }

    private static void LogEnabledScenes()
    {
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            Debug.Log($"[BasisHeadlessBuild] Scene enabled={scene.enabled} path={scene.path}");
        }
    }

    private static void EnsureBuildDirectory(string buildPath)
    {
        string directory = Path.GetDirectoryName(buildPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static StandaloneBuildSubtarget ParseStandaloneSubtarget(string value)
    {
        if (Enum.TryParse(value, true, out StandaloneBuildSubtarget parsed))
        {
            return parsed;
        }

        return StandaloneBuildSubtarget.Server;
    }

    private static int ParseLinuxArchitecture(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        switch (value.Trim().ToUpperInvariant())
        {
            case "ARM64":
                return 1;
            case "UNIVERSAL":
                return 2;
            case "X64":
            default:
                return 0;
        }
    }

    private static string RequireArgument(string name)
    {
        string value = GetArgument(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BuildFailedException($"Required command line argument '-{name}' was not provided.");
        }

        return value;
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == $"-{name}")
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
