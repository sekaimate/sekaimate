using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisWebAudioSettingsTests
{
    private static readonly string[] MicrophoneToggleSounds =
    {
        "Packages/com.basis.sdk/Sounds/boop on.wav",
        "Packages/com.basis.sdk/Sounds/boop off.wav",
    };

    private static readonly string[] BrowserUiSounds =
    {
        "Packages/com.basis.sdk/Sounds/button hover.wav",
        "Packages/com.basis.sdk/Sounds/button press 2.wav",
        "Packages/com.basis.sdk/Sounds/button press.wav",
        "Packages/com.basis.sdk/Sounds/shuttersound.mp3",
    };

    [Test]
    public void SteamAudioDoesNotEnableWebGlRuntime()
    {
        const string buildScriptPath = "Packages/com.steam.steamaudio/Editor/Build.cs";
        string source = File.ReadAllText(buildScriptPath);

        StringAssert.DoesNotContain("NamedBuildTarget.iOS,\n                NamedBuildTarget.WebGL", source);
        StringAssert.Contains("webDefineList.Remove(\"STEAMAUDIO_ENABLED\")", source);
    }

    [Test]
    public void MicrophoneToggleSoundsDoNotPreloadBrowserIncompatibleCompressedData()
    {
        foreach (string path in MicrophoneToggleSounds)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

            Assert.That(importer, Is.Not.Null, path);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            Assert.That(settings.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad), path);
            Assert.That(settings.preloadAudioData, Is.False, path);
        }
    }

    [Test]
    public void BrowserUiSoundsUseCompressedLoading()
    {
        foreach (string path in BrowserUiSounds)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

            Assert.That(importer, Is.Not.Null, path);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            Assert.That(settings.loadType, Is.EqualTo(AudioClipLoadType.CompressedInMemory), path);
            Assert.That(settings.preloadAudioData, Is.False, path);
        }
    }
}
