using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisWebMediaPipeBackendTests
{
    private const string BrowserPluginPath = "Packages/com.basis.mediapipe/Runtime/WebGL/BasisMediaPipeWeb.jslib";
    private const string BridgePath = "Packages/com.basis.mediapipe/Runtime/WebGL/BasisMediaPipeWebBackend.cs";
    private const string WorkerPath = "Packages/com.basis.mediapipe/Web~/BasisMediaPipeWorker.mjs";
    private const string DevelopmentHarnessPath = "Packages/com.basis.mediapipe/Tests~/Development/WebGL/index.html";
    private const string DevelopmentHarnessScriptPath = "Packages/com.basis.mediapipe/Tests~/Development/WebGL/mediapipe-e2e.mjs";
    private const string PlaywrightSpecPath = "Packages/com.basis.mediapipe/Tests~/Development/WebGL/mediapipe-worker.spec.mjs";
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
    public void NativeHomulerBackendRetainsThreadedUnityCameraPath()
    {
        string nativeBackend = File.ReadAllText(
            "Packages/com.basis.mediapipe/Runtime/Homuler/HomulerMediaPipeBackend.cs");
        AssemblyDefinitionSettings nativeSettings = ReadAssemblyDefinition(NativeAssemblyDefinitionPath);

        Assert.That(nativeSettings.excludePlatforms, Is.EqualTo(new[] { "WebGL" }));
        StringAssert.Contains("UsesUnityCamera => true", nativeBackend);
        StringAssert.Contains("new Thread(WorkerLoop)", nativeBackend);
        StringAssert.Contains("new AutoResetEvent(false)", nativeBackend);
        StringAssert.Contains("Addressables.LoadAssetAsync<TextAsset>", nativeBackend);
        StringAssert.Contains("frame.GetPixels32", nativeBackend);
    }

    [Test]
    public void BrowserInferenceRunsInAWorkerWithLocalTasksVisionAssets()
    {
        string plugin = File.ReadAllText(BrowserPluginPath);
        string worker = File.ReadAllText(WorkerPath);

        StringAssert.Contains("new Worker", plugin);
        StringAssert.Contains("createImageBitmap", plugin);
        StringAssert.Contains("navigator.mediaDevices.enumerateDevices", plugin);
        StringAssert.Contains("deviceId: { exact:", plugin);
        StringAssert.Contains("BasisMediaPipeWorker.mjs", plugin);
        StringAssert.Contains("FaceLandmarker.createFromOptions", worker);
        StringAssert.Contains("HandLandmarker.createFromOptions", worker);
        StringAssert.Contains("PoseLandmarker.createFromOptions", worker);
        StringAssert.Contains("detectForVideo", worker);
        StringAssert.Contains("appliedConfig", worker);
        StringAssert.Contains("vision_bundle.mjs", worker);
        StringAssert.DoesNotContain("https://cdn", worker);

        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_bundle.mjs"), Is.True);
        StringAssert.Contains("vision_wasm_module_internal.js", worker);
        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_wasm_module_internal.js"), Is.True);
        Assert.That(File.Exists("Packages/com.basis.mediapipe/Web~/vision_wasm_module_internal.wasm"), Is.True);
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
        StringAssert.Contains("vision_wasm_module_internal.js", source);
        StringAssert.Contains("vision_wasm_module_internal.wasm", source);
        StringAssert.Contains("face_landmarker.task.bytes", source);
        StringAssert.Contains("hand_landmarker.task.bytes", source);
        StringAssert.Contains("pose_landmarker_lite.task.bytes", source);
    }

    [Test]
    public void DevelopmentE2ECoversWorkerInferenceSettingsAndAvatarSignals()
    {
        Assert.That(File.Exists(DevelopmentHarnessPath), Is.True);
        Assert.That(File.Exists(DevelopmentHarnessScriptPath), Is.True);
        Assert.That(File.Exists(PlaywrightSpecPath), Is.True);

        string harness = File.ReadAllText(DevelopmentHarnessScriptPath);
        string spec = File.ReadAllText(PlaywrightSpecPath);

        StringAssert.Contains("new Worker", harness);
        StringAssert.Contains("captureStream", harness);
        StringAssert.Contains("createImageBitmap", harness);
        StringAssert.Contains("faceDetected", harness);
        StringAssert.Contains("leftHandDetected", harness);
        StringAssert.Contains("rightHandDetected", harness);
        StringAssert.Contains("poseDetected", harness);
        StringAssert.Contains("avatarSignals", harness);
        StringAssert.Contains("mirror", spec);
        StringAssert.Contains("swapHands", spec);
        StringAssert.Contains("faceDetected", spec);
        StringAssert.Contains("leftHandDetected", spec);
        StringAssert.Contains("rightHandDetected", spec);
        StringAssert.Contains("poseDetected", spec);
        StringAssert.Contains("avatarSignals", spec);
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
