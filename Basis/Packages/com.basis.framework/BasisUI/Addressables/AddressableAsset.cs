using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Basis.BasisUI
{
    public static class AddressableAssets
    {
        private const string UiLabel = "basis-ui";

        public static class Sprites
        {
            public static string Settings = "Packages/com.basis.sdk/Sprites/Icons/IonIcon Settings.png";
            public static string Servers = "Packages/com.basis.sdk/Textures/Runtime/server-outline.png";
            public static string Avatars = "Packages/com.basis.sdk/Textures/Runtime/avatarWhite.png";
            public static string Calibrate = "Packages/com.basis.sdk/Textures/Runtime/calibrateWhite.png";
            public static string Respawn = "Packages/com.basis.sdk/Textures/Runtime/Teleport.png";
            public static string Camera = "Packages/com.basis.sdk/Textures/Runtime/camera-outline.png";
            public static string CameraSettings = "Packages/com.basis.sdk/Textures/Runtime/sliders.png";
            public static string Mirror = "Packages/com.basis.sdk/Textures/Runtime/Mirror.png";
            public static string Exit = "Packages/com.basis.sdk/Textures/Runtime/exit-outline.png";
            public static string Items = "Packages/com.basis.sdk/Textures/Runtime/items.png";
            public static string Library = "Packages/com.basis.sdk/Textures/Runtime/library.png";
            public static string Search = "Packages/com.basis.sdk/Textures/Runtime/search.png";
            public static string Add = "Packages/com.basis.sdk/Textures/Runtime/add.png";
            public static string List = "Packages/com.basis.sdk/Textures/Runtime/list.png";
            public static string Network = "Packages/com.basis.sdk/Textures/Runtime/network.png";
            public static string World = "Packages/com.basis.sdk/Textures/Runtime/worlds.png";
            public static string Locked = "Packages/com.basis.sdk/Textures/Runtime/padlock-locked.png";
            public static string Unlocked = "Packages/com.basis.sdk/Textures/Runtime/padlock-unlocked.png";
            public static string FileTray = "Packages/com.basis.sdk/Textures/Runtime/file-tray.png";
            public static string HourGlass = "Packages/com.basis.sdk/Textures/Runtime/hour-glass.png";
            public static string Clock = "Packages/com.basis.sdk/Textures/Runtime/clock.png";
            public static string Pin = "Packages/com.basis.sdk/Textures/Runtime/pin.png";
            public static string Computer = "Packages/com.basis.sdk/Textures/Runtime/computer.png";
            public static string Information = "Packages/com.basis.sdk/Textures/Runtime/information.png";
            public static string Admin = "Packages/com.basis.sdk/Textures/Runtime/admin.png";
            // panel header controls (see BasisPanelMoveHandle)
            public static string Move = "Packages/com.basis.sdk/Textures/Runtime/move-outline.png";
            public static string Reset = "Packages/com.basis.sdk/Textures/Runtime/reset.png";
            public static string Microphone = "Packages/com.basis.sdk/Textures/Runtime/microphone-solid.png";
            public static string MicrophoneMute = "Packages/com.basis.sdk/Textures/Runtime/microphone-mute-solid.png";
            public static string People = "Packages/com.basis.sdk/Textures/Runtime/people-outline.png";
            public static string Select = "Packages/com.basis.sdk/Textures/Runtime/scan-outline.png";
            public static string TeleportTo = "Packages/com.basis.sdk/Textures/Runtime/Teleport.png";
            public static string Trash = "Packages/com.basis.sdk/Textures/Runtime/trash-bin-outline.png";
            public static string Link = "Packages/com.basis.sdk/Textures/Runtime/link-outline.png";
            public static string Unlink = "Packages/com.basis.sdk/Textures/Runtime/unlink-outline.png";
            public static string Embedded = "Packages/com.basis.sdk/Textures/Runtime/embedded.png";
            public static string Polygons = "Packages/com.basis.sdk/Textures/Runtime/polygons.png";
            public static string Materials = "Packages/com.basis.sdk/Textures/Runtime/materials.png";
            public static string Bones = "Packages/com.basis.sdk/Textures/Runtime/bones.png";
            public static string PlatformMobileAndroid = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-android.png";
            public static string PlatformMobileiOS = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-ios.png";
            public static string PlatformStandaloneOSX = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-mac.png";
            public static string PlatformStandaloneLinux64 = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-tux.png";
            public static string PlatformStandaloneWindows64 = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-windows.png";
            // The platform-agnostic glTF fallback section (BasisBundleConnector.GenericPlatform).
            // It has no vendor to show a logo for — it runs anywhere — so a globe stands in.
            public static string PlatformGeneric = "Packages/com.basis.sdk/Textures/Runtime/Platform Icons/logo-generic.png";
        }

        private static readonly Dictionary<string, UnityEngine.Object> Assets = new();
        private static readonly List<AsyncOperationHandle<UnityEngine.Object>> Handles = new();
        private static Task initializeTask;

        public static Task InitializeAsync()
        {
            if (Assets.Count > 0)
            {
                return Task.CompletedTask;
            }

            return initializeTask ??= InitializeInternalAsync();
        }

        private static async Task InitializeInternalAsync()
        {
            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
                Addressables.LoadResourceLocationsAsync(UiLabel, typeof(UnityEngine.Object));
            try
            {
                IList<IResourceLocation> locations = await locationsHandle.Task;
                for (int index = 0; index < locations.Count; index++)
                {
                    IResourceLocation location = locations[index];
                    AsyncOperationHandle<UnityEngine.Object> assetHandle =
                        Addressables.LoadAssetAsync<UnityEngine.Object>(location);
                    Handles.Add(assetHandle);
                }

                for (int index = 0; index < locations.Count; index++)
                {
                    UnityEngine.Object asset = await Handles[index].Task;
                    Assets.Add(locations[index].PrimaryKey, asset);
                }
            }
            catch
            {
                ReleaseAll();
                initializeTask = null;
                throw;
            }
            finally
            {
                Addressables.Release(locationsHandle);
            }
        }

        public static T Get<T>(string path) where T : UnityEngine.Object
        {
            if (!Assets.TryGetValue(path, out UnityEngine.Object asset))
            {
                throw new InvalidOperationException($"UI asset was not preloaded: {path}");
            }

            if (asset is not T typedAsset)
            {
                throw new InvalidOperationException($"UI asset at {path} is not {typeof(T).Name}");
            }

            return typedAsset;
        }

        public static Sprite GetSprite(string path)
        {
            return string.IsNullOrEmpty(path) ? null : Get<Sprite>(path);
        }

        public static void ReleaseAllSprites()
        {
            ReleaseAll();
        }

        private static void ReleaseAll()
        {
            for (int index = 0; index < Handles.Count; index++)
            {
                AsyncOperationHandle<UnityEngine.Object> handle = Handles[index];
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            Handles.Clear();
            Assets.Clear();
            initializeTask = null;
        }

        public static bool AddressExists(string key)
        {
            return !string.IsNullOrEmpty(key) && Assets.ContainsKey(key);
        }

        public static void Release(UnityEngine.Object obj)
        {
            Addressables.Release(obj);
        }
    }
}
