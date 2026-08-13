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

public static class BasisWorldBeeWebBuildRunner
{
    private const string SourceScenePath = "Packages/com.basis.examples/Seats/SeatScene.unity";
    private const string SourceModelMetaPath = "Packages/com.basis.examples/Seats/seats.fbx.meta";
    private const string OutputArgument = "basisWorldBeeOutput";
    private const string PasswordArgument = "basisWorldBeePassword";
    private static Task<string> runningTask;

    public static void RunFromCommandLine()
    {
        if (runningTask != null)
        {
            throw new InvalidOperationException("A world BEE verification build is already running.");
        }

        runningTask = RunAsync(RequireArgument(OutputArgument), RequireArgument(PasswordArgument));
        EditorApplication.update += PollCommandLineBuild;
    }

    public static async Task<string> RunAsync(string outputRoot, string password)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            throw new InvalidOperationException("The active build target must be WebGL before starting the world BEE build.");
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
            throw new InvalidOperationException($"Open scenes must be saved before the world BEE build: {string.Join(", ", dirtyScenes)}");
        }

        outputRoot = Path.GetFullPath(outputRoot);
        if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
        {
            throw new InvalidOperationException($"World BEE output root must be empty: {outputRoot}");
        }

        Directory.CreateDirectory(outputRoot);
        SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        BasisAssetBundleObject settings = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(BasisAssetBundleObject.AssetBundleObject);
        if (settings == null)
        {
            throw new InvalidOperationException("BEE build settings are missing.");
        }

        byte[] originalModelMeta = File.ReadAllBytes(SourceModelMetaPath);
        string verificationFolderName = $"BasisWorldBeeVerification_{Guid.NewGuid():N}";
        string verificationRoot = $"Assets/{verificationFolderName}";
        string bundleFolder = null;

        try
        {
            string folderGuid = AssetDatabase.CreateFolder("Assets", verificationFolderName);
            if (string.IsNullOrWhiteSpace(folderGuid))
            {
                throw new InvalidOperationException($"Failed to create verification asset folder: {verificationRoot}");
            }

            string sceneCopyPath = $"{verificationRoot}/SeatScene-WebGL.unity";
            if (!AssetDatabase.CopyAsset(SourceScenePath, sceneCopyPath))
            {
                throw new InvalidOperationException($"Failed to copy verification scene from {SourceScenePath}.");
            }

            Scene scene = EditorSceneManager.OpenScene(sceneCopyPath, OpenSceneMode.Single);
            if (!BasisScene.SceneTraversalFindBasisScene(scene, out BasisScene content))
            {
                throw new InvalidOperationException("Verification scene does not contain a BasisScene.");
            }

            BasisWorldInteractionFixtureBuilder.Create(content);
            content.BasisBundleDescription.AssetBundleName = $"web-world-verification-{Guid.NewGuid():N}";
            if (content.MainCamera == null)
            {
                throw new InvalidOperationException("Verification scene must contain a main camera.");
            }

            Vector3 markerWorldPosition = content.MainCamera.transform.position + content.MainCamera.transform.forward * 3f;
            BasisBeeRuntimeCapabilityFixture.Attach(
                content.gameObject,
                verificationRoot,
                BasisBeeRuntimeCapabilityFormat.World,
                content.transform.InverseTransformPoint(markerWorldPosition));
            EditorSceneManager.SaveScene(scene);

            (bool success, string message) = await BasisBundleBuild.SceneBundleBuild(
                null,
                content,
                new List<BuildTarget> { BuildTarget.WebGL },
                true,
                password);
            if (!success)
            {
                throw new InvalidOperationException($"World BEE build failed: {message}");
            }

            bundleFolder = Path.Combine(
                BasisBundleBuild.PathConversion(settings.AssetBundleDirectory),
                BasisBundleBuild.MakeSafeFolderName(content.BasisBundleDescription.AssetBundleName));
            string[] beePaths = Directory.GetFiles(bundleFolder, "*.BEE", SearchOption.TopDirectoryOnly);
            if (beePaths.Length != 1)
            {
                throw new InvalidOperationException($"Expected one world BEE, but found {beePaths.Length} in {bundleFolder}.");
            }

            string beePath = beePaths[0];
            BeeResult<BasisIOManagement.BeeReadResult> readResult = await BasisIOManagement.ReadRemoteBeeFromDiskEx(
                beePath,
                password,
                new BasisProgressReport(),
                includeSection: false);
            if (!readResult.IsSuccess || readResult.Value?.Connector == null)
            {
                throw new InvalidOperationException($"World BEE connector validation failed: {readResult.Error}");
            }

            long connectorLength = ReadConnectorLength(beePath);
            long fileLength = new FileInfo(beePath).Length;
            if (!BasisWebBeeArtifactValidator.TryValidate(readResult.Value.Connector, connectorLength, fileLength, out string validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            string outputPath = Path.Combine(outputRoot, Path.GetFileName(beePath));
            File.Copy(beePath, outputPath);
            File.Copy(
                Path.Combine(bundleFolder, $"{settings.ProtectedPasswordFileName}.txt"),
                Path.Combine(outputRoot, $"{settings.ProtectedPasswordFileName}.txt"));
            Debug.Log($"[BasisWorldBeeWebBuildRunner] BEE_PATH={outputPath}");
            Debug.Log($"[BasisWorldBeeWebBuildRunner] BEE_PASSWORD={password}");
            return outputPath;
        }
        finally
        {
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
            throw new InvalidOperationException($"World BEE header must contain {header.Length} bytes, but contained {bytesRead}.");
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
            Debug.Log($"[BasisWorldBeeWebBuildRunner] Completed: {beePath}");
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
