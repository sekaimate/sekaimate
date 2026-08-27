#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking;
using Basis.Scripts.Settings;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

public static class BasisWebPersistenceE2EProbe
{
    private const string QueryKey = "basisPersistenceE2E";
    private const string AvatarUrl = "https://basis.invalid/e2e/avatar.BEE";
    private const string AvatarPassword = "basis-e2e-avatar";
    private const string PropUrl = "https://basis.invalid/e2e/prop.BEE";
    private const string PropPassword = "basis-e2e-prop";
    private const string WorldUrl = "https://basis.invalid/e2e/world.BEE";
    private const string WorldPassword = "basis-e2e-world";
    private const string SettingKey = "basis.web.persistence.e2e";
    private const string SettingValue = "reload-restored";
    private const string SavedServerId = "basis-web-persistence-e2e";
    private const string SavedServerDisplayName = "Basis Web Persistence E2E";
    private const string SavedServerAddress = "persistence.basis.invalid";
    private const ushort SavedServerPort = 4297;
    private const string SavedServerWebSocketUri = "wss://persistence.basis.invalid/client";
    private const string TrustedUrl = "https://persistence.basis.invalid/*";
    private const BasisActionDriver.ActionId BindingAction =
        BasisActionDriver.ActionId.ToggleHamburgerOnSecondaryRelease;
    private const BasisBoneTrackedRole BindingRole = BasisBoneTrackedRole.CenterEye;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static async void Run()
    {
        string phase = ReadQueryValue(Application.absoluteURL, QueryKey);
        try
        {
            switch (phase)
            {
                case "seed":
                    await Seed();
                    Publish(new ProbeResult { phase = "seed", ready = true });
                    break;
                case "verify":
                    Publish(await Verify());
                    break;
            }
        }
        catch (Exception exception)
        {
            Publish(new ProbeResult
            {
                phase = phase,
                error = exception.GetType().Name + ": " + exception.Message
            });
        }
    }

    private static async Task Seed()
    {
        await WaitForWebDeviceMode();
        await BasisDataStoreAvatarKeys.LoadKeys();
        await BasisDataStoreAvatarKeys.AddNewKey(new BasisDataStoreAvatarKeys.AvatarKey
        {
            Url = AvatarUrl,
            Pass = AvatarPassword
        });
        await BasisDataStoreItemKeys.AddNewKey(CreateItem(
            BundledContentHolder.Mode.Prop,
            PropUrl,
            PropPassword));
        await BasisDataStoreItemKeys.AddNewKey(CreateItem(
            BundledContentHolder.Mode.World,
            WorldUrl,
            WorldPassword));

        BasisActionDriver.Bind(BindingAction, BindingRole);
        await BasisActionDriver.SaveFromDriver();

        BasisSettingsSystem.LoadAllSettings();
        BasisSettingsSystem.SaveString(SettingKey, SettingValue);

        List<SavedServerEntry> savedServers = SavedServerStore.Load();
        savedServers.RemoveAll(server => server != null && server.Id == SavedServerId);
        savedServers.Add(new SavedServerEntry
        {
            Id = SavedServerId,
            DisplayName = SavedServerDisplayName,
            Address = SavedServerAddress,
            Port = SavedServerPort,
            Password = string.Empty,
            HasPassword = false,
            WebSocketUri = SavedServerWebSocketUri
        });
        SavedServerStore.Save(savedServers);

        await BasisTrustedUrls.InitializeAsync();
        BasisTrustedUrls.Add(TrustedUrl);
    }

    private static async Task<ProbeResult> Verify()
    {
        await WaitForWebDeviceMode();
        await BasisDataStoreAvatarKeys.LoadKeys();
        await BasisDataStoreItemKeys.LoadKeys();
        await BasisActionDriver.LoadApplyToDriverAsync();
        BasisSettingsSystem.LoadAllSettings();
        await BasisTrustedUrls.InitializeAsync();

        BasisDataStoreAvatarKeys.AvatarKey[] avatars = BasisDataStoreAvatarKeys.DisplayKeys();
        BasisDataStoreItemKeys.ItemKey[] items = BasisDataStoreItemKeys.DisplayKeys();
        return new ProbeResult
        {
            phase = "verify",
            ready = true,
            avatar = avatars.Any(key => key.Url == AvatarUrl && key.Pass == AvatarPassword),
            prop = ContainsItem(items, BundledContentHolder.Mode.Prop, PropUrl, PropPassword),
            world = ContainsItem(items, BundledContentHolder.Mode.World, WorldUrl, WorldPassword),
            binding = BasisActionDriver.GetBindings(BindingAction).Contains(BindingRole),
            settings = BasisSettingsSystem.LoadString(SettingKey, string.Empty) == SettingValue,
            savedServers = SavedServerStore.Load().Any(server =>
                server != null &&
                server.Id == SavedServerId &&
                server.DisplayName == SavedServerDisplayName &&
                server.Address == SavedServerAddress &&
                server.Port == SavedServerPort &&
                !server.HasPassword &&
                server.WebSocketUri == SavedServerWebSocketUri),
            trustedUrls = BasisTrustedUrls.GetUserAdded().Contains(TrustedUrl)
        };
    }

    private static BasisDataStoreItemKeys.ItemKey CreateItem(
        BundledContentHolder.Mode mode,
        string url,
        string password)
    {
        return new BasisDataStoreItemKeys.ItemKey
        {
            Mode = mode,
            PlacementType = BundledContentHolder.PlacementType.SpawnAtPlayerOrigin,
            Url = url,
            Pass = password
        };
    }

    private static async Task WaitForWebDeviceMode()
    {
        float deadline = Time.realtimeSinceStartup + 60f;
        while (BasisDeviceManagement.Instance == null ||
               BasisDeviceManagement.StaticCurrentMode != BasisConstants.Web ||
               !BasisActionDriver.GetBindings(
                       BasisActionDriver.ActionId.SetMovementVectorFromPrimary2DAxis)
                   .Contains(BasisBoneTrackedRole.LeftHand))
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                throw new TimeoutException("Web device mode did not initialize within 60 seconds.");
            }

            await Task.Yield();
        }
    }

    private static bool ContainsItem(
        BasisDataStoreItemKeys.ItemKey[] items,
        BundledContentHolder.Mode mode,
        string url,
        string password)
    {
        return items.Any(item =>
            item != null && item.Mode == mode && item.Url == url && item.Pass == password);
    }

    private static string ReadQueryValue(string absoluteUrl, string key)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
        {
            return string.Empty;
        }

        string query = uri.Query.TrimStart('?');
        foreach (string field in query.Split('&'))
        {
            string[] pair = field.Split(new[] { '=' }, 2);
            if (Uri.UnescapeDataString(pair[0]) == key)
            {
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }

        return string.Empty;
    }

    private static void Publish(ProbeResult result)
    {
        BasisWebPersistenceE2EPublish(JsonUtility.ToJson(result));
    }

    [Serializable]
    private sealed class ProbeResult
    {
        public string phase;
        public bool ready;
        public bool avatar;
        public bool prop;
        public bool world;
        public bool binding;
        public bool settings;
        public bool savedServers;
        public bool trustedUrls;
        public string error;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebPersistenceE2EPublish(string resultJson);
}
#endif
