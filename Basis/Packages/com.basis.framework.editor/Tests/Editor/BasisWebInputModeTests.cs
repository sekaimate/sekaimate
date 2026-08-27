using System.Linq;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Device_Management.Devices.Web;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisWebInputModeTests
{
    [TestCase(RuntimePlatform.WindowsPlayer, false, BasisConstants.Desktop)]
    [TestCase(RuntimePlatform.OSXPlayer, false, BasisConstants.Desktop)]
    [TestCase(RuntimePlatform.LinuxPlayer, false, BasisConstants.Desktop)]
    [TestCase(RuntimePlatform.IPhonePlayer, true, BasisConstants.Desktop)]
    [TestCase(RuntimePlatform.Android, true, BasisConstants.OpenXRLoader)]
    [TestCase(RuntimePlatform.WebGLPlayer, false, BasisConstants.Web)]
    public void RuntimePlatformMapsToExactDefaultMode(
        RuntimePlatform platform,
        bool isMobilePlatform,
        string expectedMode)
    {
        string mode = BasisDeviceManagement.ResolveDefaultMode(platform, isMobilePlatform, false);

        Assert.That(mode, Is.EqualTo(expectedMode));
    }

    [Test]
    public void ServerBuildRemainsHeadless()
    {
        string mode = BasisDeviceManagement.ResolveDefaultMode(RuntimePlatform.WebGLPlayer, false, true);

        Assert.That(mode, Is.EqualTo(BasisConstants.Headless));
    }

    [Test]
    public void WebAndDesktopManagersDoNotAcceptEachOthersMode()
    {
        GameObject gameObject = new GameObject("Input mode managers");
        BasisDesktopManagement desktop = gameObject.AddComponent<BasisDesktopManagement>();
        BasisWebManagement web = gameObject.AddComponent<BasisWebManagement>();

        Assert.That(desktop.IsDeviceBootable(BasisConstants.Desktop), Is.True);
        Assert.That(desktop.IsDeviceBootable(BasisConstants.Web), Is.False);
        Assert.That(web.IsDeviceBootable(BasisConstants.Web), Is.True);
        Assert.That(web.IsDeviceBootable(BasisConstants.Desktop), Is.False);

        Object.DestroyImmediate(gameObject);
    }

    [Test]
    public void FrameworkPrefabRegistersWebManagerAsAnExplicitBaseType()
    {
        const string prefabPath = "Packages/com.basis.framework/Prefabs/BasisFramework.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        BasisDeviceManagement deviceManagement = prefab.GetComponent<BasisDeviceManagement>();
        BasisWebManagement web = prefab.GetComponent<BasisWebManagement>();

        Assert.That(web, Is.Not.Null);
        Assert.That(deviceManagement.BaseTypes.Contains(web), Is.True);
    }
}
