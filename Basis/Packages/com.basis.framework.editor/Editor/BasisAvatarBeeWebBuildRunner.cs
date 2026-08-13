using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BasisAvatarBeeWebBuildRunner
{
    private const string SourceAvatarPath = "Packages/com.unity.3rdpersondemo/HumanoidMidAir.fbx";
    private const string SourceAvatarMetaPath = SourceAvatarPath + ".meta";
    private const string OutputArgument = "basisAvatarBeeOutput";
    private const string PasswordArgument = "basisAvatarBeePassword";
    private static Task<string> runningTask;

    public static void RunFromCommandLine()
    {
        if (runningTask != null)
        {
            throw new InvalidOperationException("An avatar BEE verification build is already running.");
        }

        runningTask = RunAsync(RequireArgument(OutputArgument), RequireArgument(PasswordArgument));
        EditorApplication.update += PollCommandLineBuild;
    }

    public static async Task<string> RunAsync(string outputRoot, string password)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            throw new InvalidOperationException("The active build target must be WebGL before starting the avatar BEE build.");
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        outputRoot = Path.GetFullPath(outputRoot);
        if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
        {
            throw new InvalidOperationException($"Avatar BEE output root must be empty: {outputRoot}");
        }

        Directory.CreateDirectory(outputRoot);
        BasisAssetBundleObject settings = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        if (settings == null)
        {
            throw new InvalidOperationException("BEE build settings are missing.");
        }

        GameObject sourceAvatar = AssetDatabase.LoadAssetAtPath<GameObject>(SourceAvatarPath);
        if (sourceAvatar == null)
        {
            throw new InvalidOperationException($"Verification avatar source is missing: {SourceAvatarPath}");
        }

        byte[] originalAvatarMeta = File.ReadAllBytes(SourceAvatarMetaPath);
        string verificationFolderName = $"BasisAvatarBeeVerification_{Guid.NewGuid():N}";
        string verificationRoot = $"Assets/{verificationFolderName}";
        GameObject buildRoot = null;
        string bundleFolder = null;
        Scene verificationScene = default;

        try
        {
            string folderGuid = AssetDatabase.CreateFolder("Assets", verificationFolderName);
            if (string.IsNullOrWhiteSpace(folderGuid))
            {
                throw new InvalidOperationException($"Failed to create verification asset folder: {verificationRoot}");
            }

            verificationScene = EditorSceneManager.NewPreviewScene();
            buildRoot = (GameObject)PrefabUtility.InstantiatePrefab(sourceAvatar, verificationScene);
            if (buildRoot == null)
            {
                throw new InvalidOperationException($"Failed to instantiate verification avatar: {SourceAvatarPath}");
            }

            buildRoot.name = "BasisAvatar-WebGL";
            Animator animator = buildRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.isHuman)
            {
                throw new InvalidOperationException("Verification avatar source must contain a humanoid Animator.");
            }

            BasisAvatar avatar = buildRoot.AddComponent<BasisAvatar>();
            avatar.Animator = animator;
            avatar.BasisBundleDescription = new BasisBundleDescription(
                $"Web-Avatar-Verification-{Guid.NewGuid():N}",
                "WebGL avatar BEE verification artifact");
            BasisBeeRuntimeCapabilityFixture.Attach(
                buildRoot,
                verificationRoot,
                BasisBeeRuntimeCapabilityFormat.Avatar,
                new Vector3(0f, 1.5f, 0f));

            (bool success, string message) = await BasisBundleBuild.GameObjectBundleBuild(
                null,
                avatar,
                new List<BuildTarget> { BuildTarget.WebGL },
                true,
                password);
            if (!success)
            {
                throw new InvalidOperationException($"Avatar BEE build failed: {message}");
            }

            bundleFolder = Path.Combine(
                BasisBundleBuild.PathConversion(settings.AssetBundleDirectory),
                BasisBundleBuild.MakeSafeFolderName(avatar.BasisBundleDescription.AssetBundleName));
            string[] beePaths = Directory.GetFiles(bundleFolder, "*.BEE", SearchOption.TopDirectoryOnly);
            if (beePaths.Length != 1)
            {
                throw new InvalidOperationException($"Expected one avatar BEE, but found {beePaths.Length} in {bundleFolder}.");
            }

            string beePath = beePaths[0];
            BeeResult<BasisIOManagement.BeeReadResult> readResult = await BasisIOManagement.ReadRemoteBeeFromDiskEx(
                beePath,
                password,
                new BasisProgressReport(),
                includeSection: false);
            if (!readResult.IsSuccess || readResult.Value?.Connector == null)
            {
                throw new InvalidOperationException($"Avatar BEE connector validation failed: {readResult.Error}");
            }

            long connectorLength = ReadConnectorLength(beePath);
            long fileLength = new FileInfo(beePath).Length;
            if (!BasisWebBeeArtifactValidator.TryValidateAvatar(
                    readResult.Value.Connector,
                    connectorLength,
                    fileLength,
                    out string validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            string outputPath = Path.Combine(outputRoot, Path.GetFileName(beePath));
            File.Copy(beePath, outputPath);
            File.Copy(
                Path.Combine(bundleFolder, $"{settings.ProtectedPasswordFileName}.txt"),
                Path.Combine(outputRoot, $"{settings.ProtectedPasswordFileName}.txt"));
            Debug.Log($"[BasisAvatarBeeWebBuildRunner] BEE_PATH={outputPath}");
            Debug.Log($"[BasisAvatarBeeWebBuildRunner] BEE_PASSWORD={password}");
            return outputPath;
        }
        finally
        {
            File.WriteAllBytes(SourceAvatarMetaPath, originalAvatarMeta);
            EditorUtility.ClearProgressBar();
            if (buildRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(buildRoot);
            }

            if (verificationScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(verificationScene);
            }

            AssetDatabase.DeleteAsset(verificationRoot);
            AssetDatabase.Refresh();
            if (!string.IsNullOrWhiteSpace(bundleFolder) && Directory.Exists(bundleFolder))
            {
                Directory.Delete(bundleFolder, true);
            }
        }
    }

    private static long ReadConnectorLength(string beePath)
    {
        byte[] header = new byte[BasisBeeConstants.RemoteHeaderSize];
        using FileStream stream = File.OpenRead(beePath);
        int bytesRead = stream.Read(header, 0, header.Length);
        if (bytesRead != header.Length)
        {
            throw new InvalidOperationException($"Avatar BEE header must contain {header.Length} bytes, but contained {bytesRead}.");
        }

        return BitConverter.ToInt64(header, 0);
    }

    private static string RequireArgument(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string flag = $"-{name}";
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], flag, StringComparison.Ordinal))
            {
                return arguments[index + 1];
            }
        }

        throw new ArgumentException($"Missing command-line argument {flag}.");
    }

    private static void PollCommandLineBuild()
    {
        if (runningTask == null || !runningTask.IsCompleted)
        {
            return;
        }

        EditorApplication.update -= PollCommandLineBuild;
        try
        {
            string beePath = runningTask.GetAwaiter().GetResult();
            Debug.Log($"[BasisAvatarBeeWebBuildRunner] Completed: {beePath}");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            runningTask = null;
        }
    }
}
