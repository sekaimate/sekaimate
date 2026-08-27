using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
namespace Basis.BasisUI.Styling
{
    public static class UiStyleSettings
    {
        public static UiStyleLibrary Library;
        public static UiStylePalette Palette;

        private static UiStylePalette _runtimePalette;
        private static Task _initializationTask;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeClone()
        {
            _runtimePalette = null;
            _initializationTask = null;
        }

        public static Task InitializeAsync()
        {
            if (Library != null && Palette != null)
            {
                return Task.CompletedTask;
            }

            return _initializationTask ??= LoadRuntimeAssetsAsync();
        }

        private static async Task LoadRuntimeAssetsAsync()
        {
#if UNITY_EDITOR
            Library = AssetDatabase.LoadAssetAtPath<UiStyleLibrary>(
                "Packages/com.basis.sdk/Settings/StyleLibrary.asset");
            Palette = AssetDatabase.LoadAssetAtPath<UiStylePalette>(
                "Packages/com.basis.sdk/Settings/StylePalette.asset");
#else
            Library = await Addressables.LoadAssetAsync<UiStyleLibrary>("StyleLibrary").Task;
            Palette = await Addressables.LoadAssetAsync<UiStylePalette>("StylePalette").Task;
#endif
        }

        public static UiStyleLibrary GetActiveStyles()
        {
#if UNITY_EDITOR
                if (Library == null)
                {
                    Library = AssetDatabase.LoadAssetAtPath<UiStyleLibrary>(
                        "Packages/com.basis.sdk/Settings/StyleLibrary.asset");
                }
#endif
            if (Library == null)
            {
                throw new InvalidOperationException($"{nameof(UiStyleSettings)} must be initialized before use.");
            }
            return Library;
        }

        public static UiStylePalette GetActivePalette()
        {
#if UNITY_EDITOR
            if (Palette == null)
            {
                Palette = AssetDatabase.LoadAssetAtPath<UiStylePalette>(
                    "Packages/com.basis.sdk/Settings/StylePalette.asset");
            }
#endif
            if (Palette == null)
            {
                throw new InvalidOperationException($"{nameof(UiStyleSettings)} must be initialized before use.");
            }

            if (Application.isPlaying)
            {
                if (_runtimePalette == null && Palette != null)
                {
                    _runtimePalette = UnityEngine.Object.Instantiate(Palette);
                }
                return _runtimePalette;
            }
            return Palette;
        }

        public static void SetActiveStyles(UiStyleLibrary library)
        {
            Library = library;
            UpdateAllStyleComponents();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(Library);
#endif
        }

        public static void SetActivePalette(UiStylePalette palette)
        {
            Palette = palette;
            UpdateAllStyleComponents();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(Palette);
#endif
        }

        public static void UpdateAllStyleComponents()
        {
            BaseUiStyleComponent[] components =
                UnityEngine.Object.FindObjectsByType<BaseUiStyleComponent>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (BaseUiStyleComponent comp in components)
            {
                if (!comp || !comp.enabled) continue;

                UiStyleUtilities.RecordComponent(comp);
                comp.ApplyActiveStyle();
            }
        }
    }
}
