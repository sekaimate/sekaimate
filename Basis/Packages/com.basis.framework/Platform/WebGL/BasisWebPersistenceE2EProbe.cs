#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using Basis.Scripts.Settings;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.IO;
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
    private const float CameraFov = 73.5f;
    private const BasisActionDriver.ActionId BindingAction =
        BasisActionDriver.ActionId.ToggleHamburgerOnSecondaryRelease;
    private const BasisBoneTrackedRole BindingRole = BasisBoneTrackedRole.CenterEye;

    private static string CameraSettingsPath => Path.Combine(
        Application.persistentDataPath,
        BasisHandHeldCameraUI.CameraSettingsJson);

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

        var cameraSettings = new BasisHandHeldCameraUI.CameraSettings { fov = CameraFov };
        File.WriteAllText(CameraSettingsPath, JsonUtility.ToJson(cameraSettings));

        BasisSettingsSystem.LoadAllSettings();
        BasisSettingsSystem.SaveString(SettingKey, SettingValue);
    }

    private static async Task<ProbeResult> Verify()
    {
        await BasisDataStoreAvatarKeys.LoadKeys();
        await BasisDataStoreItemKeys.LoadKeys();
        await BasisActionDriver.LoadApplyToDriverAsync();
        BasisSettingsSystem.LoadAllSettings();

        BasisDataStoreAvatarKeys.AvatarKey[] avatars = BasisDataStoreAvatarKeys.DisplayKeys();
        BasisDataStoreItemKeys.ItemKey[] items = BasisDataStoreItemKeys.DisplayKeys();
        BasisHandHeldCameraUI.CameraSettings cameraSettings = JsonUtility.FromJson<BasisHandHeldCameraUI.CameraSettings>(
            File.ReadAllText(CameraSettingsPath));

        return new ProbeResult
        {
            phase = "verify",
            ready = true,
            avatar = avatars.Any(key => key.Url == AvatarUrl && key.Pass == AvatarPassword),
            prop = ContainsItem(items, BundledContentHolder.Mode.Prop, PropUrl, PropPassword),
            world = ContainsItem(items, BundledContentHolder.Mode.World, WorldUrl, WorldPassword),
            binding = BasisActionDriver.GetBindings(BindingAction).Contains(BindingRole),
            camera = cameraSettings != null && Mathf.Approximately(cameraSettings.fov, CameraFov),
            settings = BasisSettingsSystem.LoadString(SettingKey, string.Empty) == SettingValue
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
        public bool camera;
        public bool settings;
        public string error;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebPersistenceE2EPublish(string resultJson);
}
#endif
