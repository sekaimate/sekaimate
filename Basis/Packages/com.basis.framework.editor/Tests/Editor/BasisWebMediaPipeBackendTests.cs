using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisWebMediaPipeBackendTests
{
    private const string BrowserPluginPath = "Packages/com.basis.mediapipe/Runtime/WebGL/BasisMediaPipeWeb.jslib";
    private const string BridgePath = "Packages/com.basis.mediapipe/Runtime/WebGL/BasisMediaPipeWebBackend.cs";
    private const string WorkerPath = "Packages/com.basis.mediapipe/Web~/BasisMediaPipeWorker.mjs";
    private const string WebAssemblyDefinitionPath = "Packages/com.basis.mediapipe/Runtime/WebGL/BasisMediaPipe.WebGL.asmdef";
    private const string NativeAssemblyDefinitionPath = "Packages/com.basis.mediapipe/Runtime/Homuler/BasisMediaPipe.Homuler.asmdef";

    [Test]
    public void BrowserPluginIsEnabledOnlyForWebGl()
    {
        PluginImporter importer = AssetImporter.GetAtPath(BrowserPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void WebGlBackendRegistersWithoutChangingNativeBackend()
    {
        AssemblyDefinitionSettings webSettings = ReadAssemblyDefinition(WebAssemblyDefinitionPath);
        AssemblyDefinitionSettings nativeSettings = ReadAssemblyDefinition(NativeAssemblyDefinitionPath);

        Assert.That(webSettings.includePlatforms, Is.EqualTo(new[] { "WebGL" }));
        Assert.That(nativeSettings.excludePlatforms, Does.Contain("WebGL"));
        StringAssert.Contains("BasisMediaPipeBackendRegistry.Register", File.ReadAllText(BridgePath));
        StringAssert.Contains("HomulerMediaPipeBackend", File.ReadAllText(
            "Packages/com.basis.mediapipe/Runtime/Homuler/HomulerMediaPipeBackend.cs"));
    }

    [Test]
    public void BrowserInferenceRunsInAWorkerWithLocalTasksVisionAssets()
    {
        string plugin = File.ReadAllText(BrowserPluginPath);
        string worker = File.ReadAllText(WorkerPath);

        StringAssert.Contains("new Worker", plugin);
        StringAssert.Contains("createImageBitmap", plugin);
        StringAssert.Contains("BasisMediaPipeWorker.mjs", plugin);
        StringAssert.Contains("FaceLandmarker.createFromOptions", worker);
        StringAssert.Contains("HandLandmarker.createFromOptions", worker);
        StringAssert.Contains("PoseLandmarker.createFromOptions", worker);
        StringAssert.Contains("detectForVideo", worker);
        StringAssert.Contains("vision_bundle.mjs", worker);
        StringAssert.DoesNotContain("https://cdn", worker);

        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_bundle.mjs"), Is.True);
        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_wasm_internal.js"), Is.True);
        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_wasm_internal.wasm"), Is.True);
    }

    [Test]
    public void WebResultContractIncludesFaceHandsAndPose()
    {
        string bridge = File.ReadAllText(BridgePath);
        string worker = File.ReadAllText(WorkerPath);

        StringAssert.Contains("FaceBlendshapes", bridge);
        StringAssert.Contains("LeftHandLandmarks", bridge);
        StringAssert.Contains("RightHandLandmarks", bridge);
        StringAssert.Contains("PoseLandmarks", bridge);
        StringAssert.Contains("PoseWorldLandmarks", bridge);
        StringAssert.Contains("facialTransformationMatrixes", worker);
        StringAssert.Contains("handedness", worker);
        StringAssert.Contains("worldLandmarks", worker);
    }

    [Test]
    public void WebDistributionCopiesRuntimeModelsAndWorkerAssets()
    {
        string source = File.ReadAllText("Packages/com.basis.mediapipe/Editor/BasisMediaPipeWebBuild.cs");

        StringAssert.Contains("BuildTarget.WebGL", source);
        StringAssert.Contains("Web~", source);
        StringAssert.Contains("face_landmarker.task.bytes", source);
        StringAssert.Contains("hand_landmarker.task.bytes", source);
        StringAssert.Contains("pose_landmarker_lite.task.bytes", source);
    }

    private static AssemblyDefinitionSettings ReadAssemblyDefinition(string path)
    {
        return JsonUtility.FromJson<AssemblyDefinitionSettings>(File.ReadAllText(path));
    }

    [System.Serializable]
    private sealed class AssemblyDefinitionSettings
    {
        public string[] includePlatforms;
        public string[] excludePlatforms;
    }
}
