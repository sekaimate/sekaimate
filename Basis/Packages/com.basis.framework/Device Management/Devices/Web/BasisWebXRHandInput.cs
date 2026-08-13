#if UNITY_WEBGL && !UNITY_EDITOR
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    public sealed class BasisWebXRHandInput : BasisInputController
    {
        private const float ButtonDownThreshold = 0.5f;

        public BasisWebXRJoint[] Joints { get; private set; } = System.Array.Empty<BasisWebXRJoint>();
        public float Pinch { get; private set; }

        private BasisWebXRBackend backend;

        public void Initialize(BasisWebXRBackend source, string id, BasisBoneTrackedRole role)
        {
            backend = source;
            ClassName = nameof(BasisWebXRHandInput);
            leftHandToIKRotationOffset = new Vector3(0f, 0f, -180f);
            rightHandToIKRotationOffset = Vector3.zero;
            leftHandToIKPositionOffset = new Vector3(0f, 0f, -0.05f);
            rightHandToIKPositionOffset = new Vector3(0f, 0f, -0.05f);
            InitializeTracking(id, id, BasisWebXRBackend.Subsystem, true, role, true);
        }

        public override void LateDoPollData()
        {
            UpdateState();
        }

        public override void RenderPollData()
        {
            if (!TryGetRole(out BasisBoneTrackedRole role))
            {
                return;
            }

            BasisWebXRSource source = backend?.GetSource(role);
            if (source == null)
            {
                return;
            }

            UpdateState(source);
            BasisWebXRPose pose = SelectHandPose(source);
            if (pose?.valid != true)
            {
                return;
            }

            ComputeUnscaledDeviceCoord(ref UnscaledDeviceCoord, pose.position);
            UnscaledDeviceCoord.rotation = pose.rotation;
            ConvertToScaledDeviceCoord();
            HandFinal.position = ScaledDeviceCoord.position;
            HandFinal.rotation = HandleHandFinalRotation(ScaledDeviceCoord.rotation);
            ApplyHandPositionOffset(role);
            ControlOnlyAsHand(HandFinal.position, HandFinal.rotation);

            BasisWebXRPose pointerPose = source.targetRayPose?.valid == true
                ? source.targetRayPose
                : pose;
            BasisCalibratedCoords pointer = new BasisCalibratedCoords();
            ComputeUnscaledDeviceCoord(ref pointer, pointerPose.position);
            pointer.rotation = pointerPose.rotation;
            BasisCalibratedCoords scaledPointer = new BasisCalibratedCoords();
            ConvertToScaledDeviceCoord(ref pointer, ref scaledPointer);
            UpdateRaycastOffset();
            ComputeRaycastDirection(scaledPointer.position, scaledPointer.rotation, ActiveRaycastOffset);
            UpdateInputEvents();
        }

        private void UpdateState()
        {
            if (TryGetRole(out BasisBoneTrackedRole role))
            {
                UpdateState(backend?.GetSource(role));
            }
        }

        private void UpdateState(BasisWebXRSource source)
        {
            if (source == null)
            {
                return;
            }

            BasisWebXRControllerState controller = BasisWebXRInputMapping.MapController(source);
            Joints = source.joints ?? System.Array.Empty<BasisWebXRJoint>();
            Pinch = source.handTracked ? BasisWebXRInputMapping.CalculatePinch(Joints) : 0f;
            float trigger = source.handTracked ? Pinch : controller.trigger;
            float grip = source.handTracked ? CalculateHandGrip(Joints) : controller.grip;

            CurrentInputState.Trigger = trigger;
            CurrentInputState.SecondaryTrigger = grip;
            CurrentInputState.GripButton = grip >= ButtonDownThreshold;
            CurrentInputState.Primary2DAxisRaw = controller.primaryAxis;
            CurrentInputState.Primary2DAxisClick = controller.axisClick;
            CurrentInputState.PrimaryButtonGetState = controller.primaryButton;
            CurrentInputState.SecondaryButtonGetState = controller.secondaryButton;
            UpdateFingerPose(source.handTracked);
        }

        private void UpdateFingerPose(bool handTracked)
        {
            if (!TryGetRole(out BasisBoneTrackedRole role))
            {
                return;
            }

            BasisFingerPose fingerPose = role == BasisBoneTrackedRole.LeftHand
                ? BasisLocalPlayer.Instance.LocalHandDriver.LeftHand
                : BasisLocalPlayer.Instance.LocalHandDriver.RightHand;
            if (handTracked)
            {
                fingerPose.ThumbPercentage = new Vector2(ToBasisFinger(BasisWebXRInputMapping.CalculateFingerCurl(Joints, 1)), 0f);
                fingerPose.IndexPercentage = new Vector2(ToBasisFinger(BasisWebXRInputMapping.CalculateFingerCurl(Joints, 6)), 0f);
                fingerPose.MiddlePercentage = new Vector2(ToBasisFinger(BasisWebXRInputMapping.CalculateFingerCurl(Joints, 11)), 0f);
                fingerPose.RingPercentage = new Vector2(ToBasisFinger(BasisWebXRInputMapping.CalculateFingerCurl(Joints, 16)), 0f);
                fingerPose.LittlePercentage = new Vector2(ToBasisFinger(BasisWebXRInputMapping.CalculateFingerCurl(Joints, 21)), 0f);
                return;
            }

            float triggerFinger = ToBasisFinger(CurrentInputState.Trigger);
            float gripFinger = ToBasisFinger(CurrentInputState.SecondaryTrigger);
            fingerPose.IndexPercentage = new Vector2(triggerFinger, 0f);
            fingerPose.MiddlePercentage = new Vector2(gripFinger, 0f);
            fingerPose.RingPercentage = new Vector2(gripFinger, 0f);
            fingerPose.LittlePercentage = new Vector2(gripFinger, 0f);
        }

        private void ApplyHandPositionOffset(BasisBoneTrackedRole role)
        {
            if (!UseIKPositionOffset)
            {
                return;
            }

            Vector3 offset = role == BasisBoneTrackedRole.LeftHand
                ? leftHandToIKPositionOffset
                : rightHandToIKPositionOffset;
            HandFinal.position += HandFinal.rotation * (offset * BasisHeightDriver.DeviceScale);
        }

        private static BasisWebXRPose SelectHandPose(BasisWebXRSource source)
        {
            if (source.handTracked &&
                source.joints != null &&
                source.joints.Length > BasisWebXRInputMapping.WristIndex &&
                source.joints[BasisWebXRInputMapping.WristIndex]?.valid == true)
            {
                BasisWebXRJoint wrist = source.joints[BasisWebXRInputMapping.WristIndex];
                return new BasisWebXRPose
                {
                    valid = true,
                    position = wrist.position,
                    rotation = wrist.rotation,
                };
            }

            return source.gripPose;
        }

        private static float CalculateHandGrip(BasisWebXRJoint[] joints)
        {
            return (
                BasisWebXRInputMapping.CalculateFingerCurl(joints, 11) +
                BasisWebXRInputMapping.CalculateFingerCurl(joints, 16) +
                BasisWebXRInputMapping.CalculateFingerCurl(joints, 21)) / 3f;
        }

        private static float ToBasisFinger(float value)
        {
            return Mathf.Clamp01(value) * 2f - 1f;
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
