using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class BasisWebOpusPluginTests
{
    private const string OpusPackagePath = "Packages/com.avionblock.opussharp";

    [Test]
    public void StaticImportsUseInternalModuleOnWebGl()
    {
        string source = File.ReadAllText($"{OpusPackagePath}/OpusSharp.Core/StaticNativeOpus.cs");

        StringAssert.Contains("#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR", source);
        StringAssert.Contains("private const string DllName = \"__Internal\";", source);
    }

    [TestCase("Packages/com.basis.framework/Networking/Transmitters/BasisAudioTransmission.cs")]
    [TestCase("Packages/com.basis.framework/Networking/VoiceRecording/BasisVoiceObjectSource.cs")]
    public void VoiceRuntimeUsesStaticOpusOnWebGl(string sourcePath)
    {
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains("#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR", source);
    }

    [Test]
    public void WebGlPluginContainsEveryOpusSharpCtlEntryPoint()
    {
        string bindings = File.ReadAllText($"{OpusPackagePath}/OpusSharp.Core/StaticNativeOpus.cs");
        string wrapper = File.ReadAllText($"{OpusPackagePath}/NativeBuild/WebGL/opussharp_ctl.c");
        MatchCollection matches = Regex.Matches(bindings, "EntryPoint = \\\"(?<symbol>opussharp_[^\\\"]+)\\\"");
        var symbols = new HashSet<string>();

        foreach (Match match in matches)
        {
            symbols.Add(match.Groups["symbol"].Value);
        }

        Assert.That(symbols, Is.Not.Empty);
        foreach (string symbol in symbols)
        {
            StringAssert.Contains($"{symbol}(", wrapper, $"Missing WebGL wrapper for {symbol}");
        }
    }

    [Test]
    public void WebGlArchiveIsImportedOnlyForWebGl()
    {
        string importer = File.ReadAllText($"{OpusPackagePath}/Plugins/webgl/libopus.a.meta");

        StringAssert.Contains("WebGL: WebGL", importer);
        StringAssert.Contains("enabled: 1", importer);
        StringAssert.Contains("Editor: Editor", importer);
    }

    [Test]
    public void BuildScriptPinsSourceAndUnityToolchainVersions()
    {
        string script = File.ReadAllText($"{OpusPackagePath}/NativeBuild/WebGL/build.sh");

        StringAssert.Contains("788cc89ce4f2c42025d8c70ec1b4457dc89cd50f", script);
        StringAssert.Contains("4.0.20-git", script);
        StringAssert.Contains("https://github.com/xiph/opus.git", script);
    }
}
