using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class BasisWebBeeBuildConfiguration : ScriptableObject
{
    public const string AssetPath = "Assets/BasisWebBeeBuildConfiguration.asset";

    public string AvatarOutputRoot;
    public string AvatarPassword;
    public string PropOutputRoot;
    public string PropPassword;
    public string WorldOutputRoot;
    public string WorldPassword;
}

public static class BasisWebBeeEditorRunner
{
    private const string MenuPath = "Basis/Build/Web E2E/Build BEE Fixtures";
    private static bool running;

    [MenuItem(MenuPath)]
    public static async void Run()
    {
        if (running)
        {
            Debug.LogWarning("A Web E2E BEE build is already running.");
            return;
        }

        BasisWebBeeBuildConfiguration configuration = AssetDatabase.LoadAssetAtPath<BasisWebBeeBuildConfiguration>(
            BasisWebBeeBuildConfiguration.AssetPath);
        if (!TryValidate(configuration, out string error))
        {
            Debug.LogError(error);
            return;
        }

        running = true;
        try
        {
            string avatar = await BasisAvatarBeeWebBuildRunner.RunAsync(
                configuration.AvatarOutputRoot,
                configuration.AvatarPassword);
            string prop = await BasisPropBeeWebBuildRunner.RunAsync(
                configuration.PropOutputRoot,
                configuration.PropPassword);
            string world = await BasisWorldBeeWebBuildRunner.RunAsync(
                configuration.WorldOutputRoot,
                configuration.WorldPassword);
            Debug.Log($"[BasisWebBeeEditorRunner] Completed: Avatar={avatar}; Prop={prop}; World={world}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            running = false;
        }
    }

    public static bool TryValidate(BasisWebBeeBuildConfiguration configuration, out string error)
    {
        if (configuration == null)
        {
            error = $"Create {BasisWebBeeBuildConfiguration.AssetPath} before building Web E2E BEE fixtures.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.AvatarOutputRoot)
            || string.IsNullOrWhiteSpace(configuration.AvatarPassword)
            || string.IsNullOrWhiteSpace(configuration.PropOutputRoot)
            || string.IsNullOrWhiteSpace(configuration.PropPassword)
            || string.IsNullOrWhiteSpace(configuration.WorldOutputRoot)
            || string.IsNullOrWhiteSpace(configuration.WorldPassword))
        {
            error = "All Web E2E BEE output roots and passwords are required.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
