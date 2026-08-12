using System.IO;
using NUnit.Framework;

public class BasisWebNamePlateShaderTests
{
    [TestCase("Packages/com.basis.sdk/Shaders/BasisNamePlatePanel.shader")]
    [TestCase("Packages/com.basis.sdk/Shaders/BasisNamePlateText.shader")]
    public void ShaderModel45IsLimitedToGpuVariant(string shaderPath)
    {
        string source = File.ReadAllText(shaderPath);

        StringAssert.Contains("#pragma target 4.5 BASIS_NAMEPLATE_GPU", source);
    }

    [Test]
    public void RendererChecksGpuCapabilities()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/UI/NamePlate/BasisGlobalNamePlateRenderer.cs");

        StringAssert.Contains("SystemInfo.supportsComputeShaders", source);
        StringAssert.Contains("SystemInfo.maxComputeBufferInputsVertex > 0", source);
        StringAssert.Contains("SystemInfo.graphicsShaderLevel >= 45", source);
        StringAssert.Contains("bm.EnableKeyword(GpuKeyword)", source);
    }
}
