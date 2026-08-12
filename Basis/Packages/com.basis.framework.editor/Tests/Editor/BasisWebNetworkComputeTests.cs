using System.IO;
using NUnit.Framework;

public class BasisWebNetworkComputeTests
{
    private const string NetworkManagementPath =
        "Packages/com.basis.framework/Networking/BasisNetworkManagement.cs";

    [Test]
    public void WebGlComputeRunsSequentiallyWithoutThreadPoolDispatch()
    {
        string source = File.ReadAllText(NetworkManagementPath);

        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n            RunSequentialCompute(snapshot, receiverCount, UnscaledDeltaTime);",
            source);
    }

    [Test]
    public void NativeComputeKeepsFivePlayerParallelThreshold()
    {
        string source = File.ReadAllText(NetworkManagementPath);

        StringAssert.Contains("if (receiverCount > 4)", source);
        StringAssert.Contains("s_computeTask = Task.Run(s_runParallelCompute);", source);
    }

    [Test]
    public void SequentialComputePreservesReceiverOrderAndAudioApplyIndices()
    {
        string source = File.ReadAllText(NetworkManagementPath);

        StringAssert.Contains("for (int i = 0; i < receiverCount; i++)", source);
        StringAssert.Contains("snapshot[i].ComputeData(unscaledDeltaTime);", source);
        StringAssert.Contains("s_decodedIndices[s_decodedCount++] = i;", source);
    }
}
