using System.IO;
using NUnit.Framework;

public class BasisWebAudioSettingsTests
{
    [Test]
    public void SteamAudioDoesNotEnableWebGlRuntime()
    {
        const string buildScriptPath = "Packages/com.steam.steamaudio/Editor/Build.cs";
        string source = File.ReadAllText(buildScriptPath);

        StringAssert.DoesNotContain("NamedBuildTarget.iOS,\n                NamedBuildTarget.WebGL", source);
        StringAssert.Contains("webDefineList.Remove(\"STEAMAUDIO_ENABLED\")", source);
    }
}
