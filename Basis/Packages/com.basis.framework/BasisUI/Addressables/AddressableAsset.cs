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

        private static readonly Dictionary<string, GameObject> Prefabs = new();
        private static readonly Dictionary<string, Sprite> SpriteAssets = new();
        private static readonly List<AsyncOperationHandle<GameObject>> PrefabHandles = new();
        private static readonly List<AsyncOperationHandle<Sprite>> SpriteHandles = new();
        private static Task initializeTask;
        private static bool isInitialized;

        public static Task InitializeAsync()
        {
            if (isInitialized)
            {
                return Task.CompletedTask;
            }

            return initializeTask ??= InitializeInternalAsync();
        }

        private static async Task InitializeInternalAsync()
        {
            AsyncOperationHandle<IList<IResourceLocation>> prefabLocationsHandle =
                Addressables.LoadResourceLocationsAsync(UiLabel, typeof(GameObject));
            AsyncOperationHandle<IList<IResourceLocation>> spriteLocationsHandle =
                Addressables.LoadResourceLocationsAsync(UiLabel, typeof(Sprite));
            try
            {
                await LoadAssetsAsync(prefabLocationsHandle, Prefabs, PrefabHandles);
                await LoadAssetsAsync(spriteLocationsHandle, SpriteAssets, SpriteHandles);
#if UNITY_WEBGL && !UNITY_EDITOR
                await LoadPrefabAsync("OnScreenControls");
                await LoadSpriteAsync(Sprites.Camera);
                await LoadSpriteAsync(Sprites.Mirror);
#endif
                isInitialized = true;
            }
            catch
            {
                ReleaseAll();
                initializeTask = null;
                throw;
            }
            finally
            {
                Addressables.Release(prefabLocationsHandle);
                Addressables.Release(spriteLocationsHandle);
            }
        }

        private static async Task LoadAssetsAsync<T>(
            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle,
            Dictionary<string, T> assets,
            List<AsyncOperationHandle<T>> handles) where T : UnityEngine.Object
        {
            IList<IResourceLocation> locations = await locationsHandle.Task;
            int firstHandleIndex = handles.Count;
            for (int index = 0; index < locations.Count; index++)
            {
                handles.Add(Addressables.LoadAssetAsync<T>(locations[index]));
            }

            for (int index = 0; index < locations.Count; index++)
            {
                T asset = await handles[firstHandleIndex + index].Task;
                assets.Add(locations[index].PrimaryKey, asset);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static async Task LoadPrefabAsync(string address)
        {
            if (Prefabs.ContainsKey(address))
            {
                return;
            }

            AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
            PrefabHandles.Add(handle);
            Prefabs.Add(address, await handle.Task);
        }

        private static async Task LoadSpriteAsync(string address)
        {
            if (SpriteAssets.ContainsKey(address))
            {
                return;
            }

            AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
            SpriteHandles.Add(handle);
            SpriteAssets.Add(address, await handle.Task);
        }
#endif

        public static GameObject GetPrefab(string path)
        {
            if (Prefabs.TryGetValue(path, out GameObject prefab))
            {
                return prefab;
            }

            throw new InvalidOperationException($"UI prefab was not preloaded: {path}");
        }

        public static Sprite GetSprite(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (SpriteAssets.TryGetValue(path, out Sprite sprite))
            {
                return sprite;
            }

            throw new InvalidOperationException($"UI sprite was not preloaded: {path}");
        }

        public static void ReleaseAllSprites()
        {
            ReleaseAll();
        }

        private static void ReleaseAll()
        {
            for (int index = 0; index < PrefabHandles.Count; index++)
            {
                AsyncOperationHandle<GameObject> handle = PrefabHandles[index];
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            for (int index = 0; index < SpriteHandles.Count; index++)
            {
                AsyncOperationHandle<Sprite> handle = SpriteHandles[index];
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            PrefabHandles.Clear();
            SpriteHandles.Clear();
            Prefabs.Clear();
            SpriteAssets.Clear();
            initializeTask = null;
            isInitialized = false;
        }

        public static bool AddressExists(string key)
        {
            return !string.IsNullOrEmpty(key) && (Prefabs.ContainsKey(key) || SpriteAssets.ContainsKey(key));
        }

        public static void Release(UnityEngine.Object obj)
        {
            Addressables.Release(obj);
        }
    }
}
