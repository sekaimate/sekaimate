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

public static class BasisPropBeeWebBuildRunner
{
    private const string SourcePrefabPath = "Packages/com.basis.examples/Seats/SimpleSeat.prefab";
    private const string SourceModelMetaPath = "Packages/com.basis.examples/Seats/seats.fbx.meta";
    private const string OutputArgument = "basisPropBeeOutput";
    private const string PasswordArgument = "basisPropBeePassword";
    private static Task<string> runningTask;

    public static void RunFromCommandLine()
    {
        if (runningTask != null)
        {
            throw new InvalidOperationException("A prop BEE verification build is already running.");
        }

        runningTask = RunAsync(RequireArgument(OutputArgument), RequireArgument(PasswordArgument));
        EditorApplication.update += PollCommandLineBuild;
    }

    public static async Task<string> RunAsync(string outputRoot, string password)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            throw new InvalidOperationException("The active build target must be WebGL before starting the prop BEE build.");
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        string[] dirtyScenes = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(SceneManager.GetSceneAt)
            .Where(scene => scene.isDirty)
            .Select(scene => string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path)
            .ToArray();
        if (dirtyScenes.Length != 0)
        {
            throw new InvalidOperationException($"Open scenes must be saved before the prop BEE build: {string.Join(", ", dirtyScenes)}");
        }

        outputRoot = Path.GetFullPath(outputRoot);
        if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
        {
            throw new InvalidOperationException($"Prop BEE output root must be empty: {outputRoot}");
        }

        Directory.CreateDirectory(outputRoot);
        SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        BasisAssetBundleObject settings = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        if (settings == null)
        {
            throw new InvalidOperationException("BEE build settings are missing.");
        }

        byte[] originalModelMeta = File.ReadAllBytes(SourceModelMetaPath);
        string verificationFolderName = $"BasisPropBeeVerification_{Guid.NewGuid():N}";
        string verificationRoot = $"Assets/{verificationFolderName}";
        GameObject buildRoot = null;
        string bundleFolder = null;

        try
        {
            string folderGuid = AssetDatabase.CreateFolder("Assets", verificationFolderName);
            if (string.IsNullOrWhiteSpace(folderGuid))
            {
                throw new InvalidOperationException($"Failed to create verification asset folder: {verificationRoot}");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                throw new InvalidOperationException($"Verification prop prefab is missing: {SourcePrefabPath}");
            }

            buildRoot = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (buildRoot == null || !buildRoot.TryGetComponent(out BasisProp content))
            {
                throw new InvalidOperationException("Verification prop prefab does not contain a root BasisProp.");
            }

            content.BasisBundleDescription.AssetBundleName = $"web-prop-verification-{Guid.NewGuid():N}";
            BasisBeeRuntimeCapabilityFixture.Attach(
                buildRoot,
                verificationRoot,
                BasisBeeRuntimeCapabilityFormat.Prop,
                new Vector3(0f, 1f, 0f));

            (bool success, string message) = await BasisBundleBuild.GameObjectBundleBuild(
                null,
                content,
                new List<BuildTarget> { BuildTarget.WebGL },
                true,
                password);
            if (!success)
            {
                throw new InvalidOperationException($"Prop BEE build failed: {message}");
            }

            bundleFolder = Path.Combine(
                BasisBundleBuild.PathConversion(settings.AssetBundleDirectory),
                BasisBundleBuild.MakeSafeFolderName(content.BasisBundleDescription.AssetBundleName));
            string[] beePaths = Directory.GetFiles(bundleFolder, "*.BEE", SearchOption.TopDirectoryOnly);
            if (beePaths.Length != 1)
            {
                throw new InvalidOperationException($"Expected one prop BEE, but found {beePaths.Length} in {bundleFolder}.");
            }

            string beePath = beePaths[0];
            BeeResult<BasisIOManagement.BeeReadResult> readResult = await BasisIOManagement.ReadRemoteBeeFromDiskEx(
                beePath,
                password,
                new BasisProgressReport(),
                includeSection: false);
            if (!readResult.IsSuccess || readResult.Value?.Connector == null)
            {
                throw new InvalidOperationException($"Prop BEE connector validation failed: {readResult.Error}");
            }

            long connectorLength = ReadConnectorLength(beePath);
            long fileLength = new FileInfo(beePath).Length;
            if (!BasisWebBeeArtifactValidator.TryValidateProp(readResult.Value.Connector, connectorLength, fileLength, out string validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            string outputPath = Path.Combine(outputRoot, Path.GetFileName(beePath));
            File.Copy(beePath, outputPath);
            File.Copy(
                Path.Combine(bundleFolder, $"{settings.ProtectedPasswordFileName}.txt"),
                Path.Combine(outputRoot, $"{settings.ProtectedPasswordFileName}.txt"));
            Debug.Log($"[BasisPropBeeWebBuildRunner] BEE_PATH={outputPath}");
            Debug.Log($"[BasisPropBeeWebBuildRunner] BEE_PASSWORD={password}");
            return outputPath;
        }
        finally
        {
            if (buildRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(buildRoot);
            }

            File.WriteAllBytes(SourceModelMetaPath, originalModelMeta);
            EditorUtility.ClearProgressBar();
            try
            {
                if (originalSceneSetup.Length == 0)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(verificationRoot);
                AssetDatabase.Refresh();
                if (!string.IsNullOrWhiteSpace(bundleFolder) && Directory.Exists(bundleFolder))
                {
                    Directory.Delete(bundleFolder, true);
                }
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
            throw new InvalidOperationException($"Prop BEE header must contain {header.Length} bytes, but contained {bytesRead}.");
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
            Debug.Log($"[BasisPropBeeWebBuildRunner] Completed: {beePath}");
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
