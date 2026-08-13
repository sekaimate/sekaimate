using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

public class BasisWebThreadingCoreTests
{
    private const string LogManagerPath =
        "Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/SettingsProviderParts/BasisLogManager.cs";
    private const string LogInitializerPath =
        "Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/SettingsProviderParts/BasisStaticLogInitializer.cs";
    private const string PlayerSettingsManagerPath =
        "Packages/com.basis.framework/Players/Common/BasisPlayerSettingsManager.cs";

    [Test]
    public async Task RuntimeLogIsAvailableAfterQueueProcessing()
    {
        string message = $"web-startup-{Guid.NewGuid():N}";

        BasisLogManager.HandleLog(message, string.Empty, LogType.Log);

        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        while (!BasisLogManager.GetAllLogsPlainText().Contains(message) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        StringAssert.Contains(message, BasisLogManager.GetAllLogsPlainText());
    }

    [Test]
    public void WebGlLogsUsePlayerLoopInsteadOfThreadingPrimitives()
    {
        string managerSource = File.ReadAllText(LogManagerPath);
        string initializerSource = File.ReadAllText(LogInitializerPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", managerSource);
        StringAssert.Contains("ProcessQueuedLogs", managerSource);
        StringAssert.Contains("BasisLogManager.ProcessQueuedLogs();", initializerSource);
        StringAssert.Contains("private void Update()", initializerSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", initializerSource);
    }

    [Test]
    public async Task PlayerSettingsRoundTripThroughPersistentFile()
    {
        string uuid = $"web-settings-{Guid.NewGuid():N}";
        string key = uuid;
        string path = Path.Combine(Application.persistentDataPath, "PlayerSettings", $"{key}.json");
        var settings = new BasisPlayerSettingsData(uuid, 2.5f, false, true, true, true);

        try
        {
            await BasisPlayerSettingsManager.SetPlayerSettings(settings);
            Assert.That(File.Exists(path), Is.True);

            ClearPlayerSettingsCache(key);
            BasisPlayerSettingsData loaded = await BasisPlayerSettingsManager.RequestPlayerSettings(uuid);

            Assert.That(loaded.UUID, Is.EqualTo(uuid));
            Assert.That(loaded.VolumeLevel, Is.EqualTo(2.5f));
            Assert.That(loaded.AvatarVisible, Is.False);
            Assert.That(loaded.AvatarInteraction, Is.True);
            Assert.That(loaded.IsBlocked, Is.True);
        }
        finally
        {
            ClearPlayerSettingsCache(key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void WebGlPlayerSettingsAvoidThreadPoolDispatch()
    {
        string source = File.ReadAllText(PlayerSettingsManagerPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("File.ReadAllText(path)", source);
        StringAssert.Contains("File.WriteAllText(path, json)", source);
        StringAssert.Contains("await Task.Yield();", source);
    }

    private static void ClearPlayerSettingsCache(string key)
    {
        FieldInfo cacheField = typeof(BasisPlayerSettingsManager).GetField(
            "cache",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(cacheField, Is.Not.Null);

        var cache = (ConcurrentDictionary<string, BasisPlayerSettingsData>)cacheField.GetValue(null);
        cache.TryRemove(key, out _);
    }
}
