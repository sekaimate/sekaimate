using System.IO;
using NUnit.Framework;

public sealed class BasisWebStreamingMetaTests
{
    private const string StreamingRuntimePath = "Packages/com.basis.framework/Streaming/BasisStreamingMetaRuntime.cs";
    private const string StreamingBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebStreamingMetaBridge.cs";
    private const string StreamingPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebStreamingMeta.jslib";

    [Test]
    public void WebGlStreamingPublishesStatsWithoutStartingHttpListener()
    {
        string runtime = File.ReadAllText(StreamingRuntimePath);
        string bridge = File.ReadAllText(StreamingBridgePath);
        string plugin = File.ReadAllText(StreamingPluginPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", runtime);
        StringAssert.Contains("BasisWebStreamingMetaBridge.Publish", runtime);
        StringAssert.Contains("BasisStreamingMetaServer", runtime);
        StringAssert.Contains("[DllImport(\"__Internal\")]", bridge);
        StringAssert.Contains("globalThis.BasisStreamingMeta", plugin);
        StringAssert.Contains("snapshot", plugin);
        StringAssert.Contains("subscribe", plugin);
    }
}
