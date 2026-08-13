#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    public sealed class BasisWebXRBackend : MonoBehaviour
    {
        internal const string Subsystem = "BasisWebXR";
        internal const string HeadId = "WebXR Head";
        internal const string LeftHandId = "WebXR Left Hand";
        internal const string RightHandId = "WebXR Right Hand";

        public static bool IsImmersiveSessionActive { get; private set; }
        public BasisWebXRSnapshot CurrentSnapshot { get; private set; } = new BasisWebXRSnapshot();

        private BasisWebManagement management;
        private BasisWebXRHeadInput headInput;
        private BasisWebXRHandInput leftHandInput;
        private BasisWebXRHandInput rightHandInput;
        private bool isShutdown;

        public void Initialize(BasisWebManagement owner)
        {
            management = owner;
            BasisWebXRInitialize();
        }

        private void Update()
        {
            IntPtr snapshotPointer = BasisWebXRGetSnapshot();
            if (snapshotPointer == IntPtr.Zero)
            {
                return;
            }

            try
            {
                string json = Marshal.PtrToStringAnsi(snapshotPointer);
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                BasisWebXRSnapshot snapshot = JsonUtility.FromJson<BasisWebXRSnapshot>(json);
                if (snapshot == null)
                {
                    return;
                }

                BasisWebXRInputMapping.ConvertToUnity(snapshot);
                CurrentSnapshot = snapshot;
                SynchronizeSession(snapshot);
            }
            finally
            {
                BasisWebXRReleaseSnapshot(snapshotPointer);
            }
        }

        private void SynchronizeSession(BasisWebXRSnapshot snapshot)
        {
            if (!snapshot.sessionActive)
            {
                if (IsImmersiveSessionActive)
                {
                    EndImmersiveSession(true);
                }
                return;
            }

            if (!IsImmersiveSessionActive)
            {
                IsImmersiveSessionActive = true;
                management.BeginImmersiveSession();
            }

            if (snapshot.head?.valid == true && headInput == null)
            {
                headInput = CreateHeadInput();
            }

            BasisWebXRSource leftSource = FindSource(snapshot, "left");
            BasisWebXRSource rightSource = FindSource(snapshot, "right");
            SynchronizeHand(ref leftHandInput, leftSource, LeftHandId, BasisBoneTrackedRole.LeftHand);
            SynchronizeHand(ref rightHandInput, rightSource, RightHandId, BasisBoneTrackedRole.RightHand);
        }

        private BasisWebXRHeadInput CreateHeadInput()
        {
            GameObject inputObject = new GameObject(HeadId);
            inputObject.transform.SetParent(BasisLocalPlayer.Instance.transform, false);
            BasisWebXRHeadInput input = inputObject.AddComponent<BasisWebXRHeadInput>();
            input.Initialize(this);
            BasisDeviceManagement.Instance.TryAdd(input);
            return input;
        }

        private void SynchronizeHand(
            ref BasisWebXRHandInput input,
            BasisWebXRSource source,
            string id,
            BasisBoneTrackedRole role)
        {
            if (source == null)
            {
                if (input != null)
                {
                    BasisDeviceManagement.Instance.RemoveDevicesFrom(Subsystem, id);
                    input = null;
                }
                return;
            }

            if (input == null)
            {
                GameObject inputObject = new GameObject(id);
                inputObject.transform.SetParent(BasisLocalPlayer.Instance.transform, false);
                input = inputObject.AddComponent<BasisWebXRHandInput>();
                input.Initialize(this, id, role);
                BasisDeviceManagement.Instance.TryAdd(input);
            }
        }

        internal BasisWebXRSource GetSource(BasisBoneTrackedRole role)
        {
            return FindSource(
                CurrentSnapshot,
                role == BasisBoneTrackedRole.LeftHand ? "left" : "right");
        }

        public void Shutdown(bool restoreDesktop)
        {
            if (isShutdown)
            {
                return;
            }

            isShutdown = true;
            BasisWebXREndSession();
            EndImmersiveSession(restoreDesktop);
        }

        private void EndImmersiveSession(bool restoreDesktop)
        {
            BasisDeviceManagement deviceManagement = BasisDeviceManagement.Instance;
            if (deviceManagement != null)
            {
                deviceManagement.RemoveDevicesFrom(Subsystem, HeadId);
                deviceManagement.RemoveDevicesFrom(Subsystem, LeftHandId);
                deviceManagement.RemoveDevicesFrom(Subsystem, RightHandId);
            }

            headInput = null;
            leftHandInput = null;
            rightHandInput = null;
            IsImmersiveSessionActive = false;
            if (restoreDesktop && management != null)
            {
                management.EndImmersiveSession();
            }
        }

        private void OnDestroy()
        {
            if (!isShutdown)
            {
                Shutdown(true);
            }
        }

        private static BasisWebXRSource FindSource(BasisWebXRSnapshot snapshot, string handedness)
        {
            BasisWebXRSource[] sources = snapshot?.sources ?? Array.Empty<BasisWebXRSource>();
            for (int index = 0; index < sources.Length; index++)
            {
                BasisWebXRSource source = sources[index];
                if (source != null && string.Equals(source.handedness, handedness, StringComparison.Ordinal))
                {
                    return source;
                }
            }
            return null;
        }

        [DllImport("__Internal")]
        private static extern void BasisWebXRInitialize();

        [DllImport("__Internal")]
        private static extern IntPtr BasisWebXRGetSnapshot();

        [DllImport("__Internal")]
        private static extern void BasisWebXRReleaseSnapshot(IntPtr pointer);

        [DllImport("__Internal")]
        private static extern void BasisWebXREndSession();
    }
}
#endif
