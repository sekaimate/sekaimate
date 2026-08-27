using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sekaimate.Editor
{
    public static class MinimalWalkableWorldBeeBuilder
    {
        public const string ScenePath = "Assets/Sekaimate/World/MinimalWalkable.unity";

        private const string BuildMenuPath = "Sekaimate/UPoC/全OS対応BEEを生成";
        private static bool isBuilding;

        [MenuItem(BuildMenuPath)]
        public static async void BuildMultiPlatformBee()
        {
            if (!CanBuildMultiPlatformBee())
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                ShowError($"次のSceneを開いてください。\n{ScenePath}");
                return;
            }

            BasisScene[] basisScenes = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BasisScene>(true))
                .ToArray();
            if (basisScenes.Length != 1)
            {
                ShowError($"Basis Sceneが{basisScenes.Length}個あります。1個だけにしてください。");
                return;
            }

            BasisScene basisScene = basisScenes[0];
            string validationError = Validate(scene, basisScene);
            if (validationError != null)
            {
                ShowError(validationError);
                return;
            }

            BasisAssetBundleObject settings = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(
                BasisAssetBundleObject.AssetBundleObject);
            if (settings == null)
            {
                ShowError("Basis build settingsを読み込めませんでした。");
                return;
            }

            List<BuildTarget> buildTargets = BasisSDKConstants.GetAllPlatformBuildTargets();
            string[] unavailableTargets = buildTargets
                .Where(target => !BasisBundleBuild.CheckTarget(target))
                .Select(target => target.ToString())
                .ToArray();
            if (unavailableTargets.Length > 0)
            {
                ShowError($"次のBuild Support Moduleをインストールしてください。\n{string.Join("\n", unavailableTargets)}");
                return;
            }

            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;
            NamedBuildTarget standaloneTarget = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Standalone);
            ScriptingImplementation originalBackend = PlayerSettings.GetScriptingBackend(standaloneTarget);
            isBuilding = true;
            string beePath = null;
            string passwordPath = null;
            string errorMessage = null;
            bool settingsRestored = false;

            try
            {
                BasisContentGroupId.EnsurePersistent(basisScene);
                if (!EditorSceneManager.SaveScene(scene))
                {
                    throw new InvalidOperationException("Sceneを保存できませんでした。");
                }

                // Basis switches to Mono after building, which is too late for this Unity version.
                if (originalBackend != ScriptingImplementation.Mono2x)
                {
                    PlayerSettings.SetScriptingBackend(standaloneTarget, ScriptingImplementation.Mono2x);
                }

                (bool success, string message) = await BasisBundleBuild.SceneBundleBuild(
                    null,
                    basisScene,
                    buildTargets,
                    false,
                    string.Empty);
                if (!success)
                {
                    throw new InvalidOperationException(message);
                }

                (beePath, passwordPath) = FindBuildOutput(settings, basisScene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                errorMessage = exception.Message;
            }
            finally
            {
                settingsRestored = RestoreEditorSettings(originalTarget, standaloneTarget, originalBackend);
                isBuilding = false;
                EditorUtility.ClearProgressBar();
            }

            if (errorMessage != null)
            {
                ShowError(errorMessage);
                return;
            }

            if (!settingsRestored)
            {
                ShowError("BEEは生成されましたが、Unity設定を復元できませんでした。Consoleを確認してください。");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = beePath;
            Debug.Log($"[Sekaimate] Multi-platform BEE generated: {beePath}");
            Debug.Log($"[Sekaimate] Password sidecar: {passwordPath}");
            EditorUtility.DisplayDialog(
                "全OS対応BEEを生成しました",
                $"BEEのパスをクリップボードへコピーしました。\n\nBEE:\n{beePath}\n\nPassword:\n{passwordPath}",
                "OK");
        }

        [MenuItem(BuildMenuPath, true)]
        private static bool CanBuildMultiPlatformBee()
        {
            return !isBuilding &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static string Validate(Scene scene, BasisScene basisScene)
        {
            BasisBundleDescription description = basisScene.BasisBundleDescription;
            if (description == null ||
                string.IsNullOrWhiteSpace(description.AssetBundleName) ||
                string.IsNullOrWhiteSpace(description.AssetBundleDescription))
            {
                return "Basis Sceneのnameとdescriptionを設定してください。";
            }

            if (basisScene.SpawnPoint == null)
            {
                return "Basis SceneのSpawn Pointを設定してください。";
            }

            if (basisScene.RespawnHeight > 0f)
            {
                return "Respawn Heightは0以下にしてください。";
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    {
                        return $"{transform.name}のMissing Scriptを解消してください。";
                    }
                }
            }

            return null;
        }

        private static (string beePath, string passwordPath) FindBuildOutput(
            BasisAssetBundleObject settings,
            BasisScene basisScene)
        {
            string outputDirectory = Path.Combine(
                BasisBundleBuild.PathConversion(settings.AssetBundleDirectory),
                BasisBundleBuild.MakeSafeFolderName(basisScene.BasisBundleDescription.AssetBundleName));
            if (!Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException($"BEEの出力先がありません: {outputDirectory}");
            }

            string[] beePaths = Directory.GetFiles(
                outputDirectory,
                "*" + settings.BasisEncryptedExtension,
                SearchOption.TopDirectoryOnly);
            if (beePaths.Length != 1)
            {
                throw new InvalidOperationException(
                    $"BEEが{beePaths.Length}件見つかりました。1件だけ生成される必要があります: {outputDirectory}");
            }

            string passwordPath = Path.Combine(
                outputDirectory,
                settings.ProtectedPasswordFileName + ".txt");
            if (!File.Exists(passwordPath))
            {
                throw new FileNotFoundException("Password sidecarがありません。", passwordPath);
            }

            return (beePaths[0], passwordPath);
        }

        private static bool RestoreEditorSettings(
            BuildTarget originalTarget,
            NamedBuildTarget standaloneTarget,
            ScriptingImplementation originalBackend)
        {
            bool restored = true;

            try
            {
                if (PlayerSettings.GetScriptingBackend(standaloneTarget) != originalBackend)
                {
                    PlayerSettings.SetScriptingBackend(standaloneTarget, originalBackend);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                restored = false;
            }

            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != originalTarget &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildPipeline.GetBuildTargetGroup(originalTarget),
                        originalTarget))
                {
                    Debug.LogError($"Failed to restore build target: {originalTarget}");
                    restored = false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                restored = false;
            }

            return restored;
        }

        private static void ShowError(string message)
        {
            EditorUtility.DisplayDialog("全OS対応BEEを生成できません", message, "OK");
        }
    }
}
