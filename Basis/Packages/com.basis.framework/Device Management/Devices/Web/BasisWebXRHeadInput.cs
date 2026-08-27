#if UNITY_WEBGL && !UNITY_EDITOR
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    public sealed class BasisWebXRHeadInput : BasisInput
    {
        private BasisWebXRBackend backend;

        public void Initialize(BasisWebXRBackend source)
        {
            backend = source;
            ClassName = nameof(BasisWebXRHeadInput);
            InitializeTracking(
                BasisWebXRBackend.HeadId,
                BasisWebXRBackend.HeadId,
                BasisWebXRBackend.Subsystem,
                true,
                BasisBoneTrackedRole.CenterEye);
        }

        public override void LateDoPollData()
        {
        }

        public override void RenderPollData()
        {
            BasisWebXRPose pose = backend?.CurrentSnapshot?.head;
            if (pose?.valid != true)
            {
                return;
            }

            ComputeUnscaledDeviceCoord(ref UnscaledDeviceCoord, pose.position);
            UnscaledDeviceCoord.rotation = pose.rotation;
            ConvertToScaledDeviceCoord();
            ControlOnlyAsDevice();
            ComputeRaycastDirection(ScaledDeviceCoord.position, ScaledDeviceCoord.rotation, Quaternion.identity);
            UpdateInputEvents();
        }

        public override void ShowTrackedVisual()
        {
            ShowTrackedVisualDefaultImplementation();
        }

        public override void PlayHaptic(float duration = 0.25f, float amplitude = 0.5f, float frequency = 0.5f)
        {
        }

        public override void PlaySoundEffect(string soundEffectName, float volume)
        {
            PlaySoundEffectDefaultImplementation(soundEffectName, volume);
        }
    }
}
#endif
