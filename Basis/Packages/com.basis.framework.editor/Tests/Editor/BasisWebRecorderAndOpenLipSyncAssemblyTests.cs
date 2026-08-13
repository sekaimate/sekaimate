using System.IO;
using NUnit.Framework;

public class BasisWebRecorderAndOpenLipSyncAssemblyTests
{
    private const string RecorderPath = "Packages/com.basis.developer.recorder/Runtime/BasisAvatarRecorder.cs";
    private const string CommonAssemblyPath = "Packages/com.basis.openlipsync/Basis.OpenLipSync.Runtime.asmdef";
    private const string NativeAssemblyPath = "Packages/com.basis.openlipsync/Runtime/Native/Basis.OpenLipSync.NativeBackend.asmdef";
    private const string NativeBackendPath = "Packages/com.basis.openlipsync/Runtime/Native/OpenLipSyncBackend.cs";

    [Test]
    public void RecorderDownloadsMemoryBufferInBrowserAndKeepsNativeFileStream()
    {
        string source = File.ReadAllText(RecorderPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("new MemoryStream()", source);
        StringAssert.Contains("BasisWebFileDownload.Save", source);
        StringAssert.Contains("application/octet-stream", source);
        StringAssert.Contains("new FileStream", source);
    }

    [Test]
    public void CommonOpenLipSyncAssemblyDoesNotReferenceOnnxRuntime()
    {
        string source = File.ReadAllText(CommonAssemblyPath);

        StringAssert.DoesNotContain("Microsoft.ML.OnnxRuntime.dll", source);
        StringAssert.Contains("Newtonsoft.Json.dll", source);
    }

    [Test]
    public void NativeOnnxBackendIsIsolatedFromWebGl()
    {
        string assembly = File.ReadAllText(NativeAssemblyPath);
        string backend = File.ReadAllText(NativeBackendPath);

        StringAssert.Contains("Basis.OpenLipSync.NativeBackend", assembly);
        StringAssert.Contains("\"excludePlatforms\": [", assembly);
        StringAssert.Contains("\"WebGL\"", assembly);
        StringAssert.Contains("Microsoft.ML.OnnxRuntime.dll", assembly);
        StringAssert.Contains("Basis.OpenLipSync.Runtime", assembly);
        StringAssert.Contains("Microsoft.ML.OnnxRuntime", backend);
    }

    [Test]
    public void CommonDriverUsesRegisteredNativeBackendWithoutOnnxTypes()
    {
        string driver = File.ReadAllText("Packages/com.basis.openlipsync/Runtime/BasisOpenLipSyncDriver.cs");
        string backend = File.ReadAllText(NativeBackendPath);

        StringAssert.Contains("IBasisOpenLipSyncBackend", driver);
        StringAssert.Contains("BasisOpenLipSyncBackendRegistry.Create()", driver);
        StringAssert.DoesNotContain("new OpenLipSyncBackend()", driver);
        StringAssert.Contains("BasisOpenLipSyncBackendRegistry.Register", backend);
    }
}
