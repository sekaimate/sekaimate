using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices.Desktop;
using System;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    [Serializable]
    public sealed class BasisWebManagement : BasisDesktopManagement
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private BasisWebXRBackend webXRBackend;
#endif

        public override void StartSDK()
        {
            base.StartSDK();
#if UNITY_WEBGL && !UNITY_EDITOR
            GameObject backendObject = new GameObject(nameof(BasisWebXRBackend));
            backendObject.transform.SetParent(BasisLocalPlayer.Instance.transform, false);
            webXRBackend = backendObject.AddComponent<BasisWebXRBackend>();
            webXRBackend.Initialize(this);
#endif
        }

        public override void StopSDK()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (webXRBackend != null)
            {
                webXRBackend.Shutdown(false);
                UnityEngine.Object.Destroy(webXRBackend.gameObject);
                webXRBackend = null;
            }
#endif
            base.StopSDK();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        internal void BeginImmersiveSession()
        {
            ReleaseDesktopEye();
            BasisCursorManagement.UnlockCursorBypassChecks(nameof(BasisWebXRBackend));
        }

        internal void EndImmersiveSession()
        {
            EnsureDesktopEye();
        }
#endif

        public override bool IsDeviceBootable(string bootRequest)
        {
            return bootRequest == BasisConstants.Web;
        }
    }
}
