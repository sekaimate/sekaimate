using System;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Web
{
    [Serializable]
    public sealed class BasisWebXRSnapshot
    {
        public int schemaVersion;
        public int frame;
        public bool supported;
        public bool sessionActive;
        public string referenceSpace;
        public BasisWebXRPose head = new BasisWebXRPose();
        public BasisWebXRSource[] sources = Array.Empty<BasisWebXRSource>();
    }

    [Serializable]
    public sealed class BasisWebXRPose
    {
        public bool valid;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }

    [Serializable]
    public sealed class BasisWebXRSource
    {
        public string handedness = string.Empty;
        public string targetRayMode = string.Empty;
        public string[] profiles = Array.Empty<string>();
        public bool hasGripPose;
        public BasisWebXRPose gripPose = new BasisWebXRPose();
        public BasisWebXRPose targetRayPose = new BasisWebXRPose();
        public bool handTracked;
        public BasisWebXRJoint[] joints = Array.Empty<BasisWebXRJoint>();
        public float[] buttons = Array.Empty<float>();
        public float[] axes = Array.Empty<float>();
    }

    [Serializable]
    public sealed class BasisWebXRJoint
    {
        public string name = string.Empty;
        public bool valid;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float radius;
    }

    public readonly struct BasisWebXRControllerState
    {
        public readonly float trigger;
        public readonly float grip;
        public readonly Vector2 primaryAxis;
        public readonly bool axisClick;
        public readonly bool primaryButton;
        public readonly bool secondaryButton;

        public BasisWebXRControllerState(
            float trigger,
            float grip,
            Vector2 primaryAxis,
            bool axisClick,
            bool primaryButton,
            bool secondaryButton)
        {
            this.trigger = trigger;
            this.grip = grip;
            this.primaryAxis = primaryAxis;
            this.axisClick = axisClick;
            this.primaryButton = primaryButton;
            this.secondaryButton = secondaryButton;
        }
    }

    public static class BasisWebXRInputMapping
    {
        public const int JointCount = 25;
        public const int WristIndex = 0;
        public const int ThumbTipIndex = 4;
        public const int IndexMetacarpalIndex = 5;
        public const int IndexTipIndex = 9;

        public static void ConvertToUnity(ref BasisWebXRPose pose)
        {
            if (pose == null)
            {
                return;
            }

            pose.position.z = -pose.position.z;
            pose.rotation = new Quaternion(
                -pose.rotation.x,
                -pose.rotation.y,
                pose.rotation.z,
                pose.rotation.w);
        }

        public static void ConvertToUnity(BasisWebXRSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            ConvertToUnity(ref snapshot.head);
            BasisWebXRSource[] sources = snapshot.sources ?? Array.Empty<BasisWebXRSource>();
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                BasisWebXRSource source = sources[sourceIndex];
                if (source == null)
                {
                    continue;
                }

                ConvertToUnity(ref source.gripPose);
                ConvertToUnity(ref source.targetRayPose);
                BasisWebXRJoint[] joints = source.joints ?? Array.Empty<BasisWebXRJoint>();
                for (int jointIndex = 0; jointIndex < joints.Length; jointIndex++)
                {
                    BasisWebXRJoint joint = joints[jointIndex];
                    if (joint == null)
                    {
                        continue;
                    }

                    joint.position.z = -joint.position.z;
                    joint.rotation = new Quaternion(
                        -joint.rotation.x,
                        -joint.rotation.y,
                        joint.rotation.z,
                        joint.rotation.w);
                }
            }
        }

        public static BasisWebXRControllerState MapController(BasisWebXRSource source)
        {
            float[] buttons = source?.buttons ?? Array.Empty<float>();
            float[] axes = source?.axes ?? Array.Empty<float>();
            Vector2 primaryAxis = axes.Length >= 4
                ? new Vector2(axes[2], axes[3])
                : axes.Length >= 2
                    ? new Vector2(axes[0], axes[1])
                    : Vector2.zero;

            return new BasisWebXRControllerState(
                Read(buttons, 0),
                Read(buttons, 1),
                primaryAxis,
                Read(buttons, 3) >= 0.5f,
                Read(buttons, 4) >= 0.5f,
                Read(buttons, 5) >= 0.5f);
        }

        public static float CalculatePinch(BasisWebXRJoint[] joints)
        {
            if (!HasValidJoint(joints, WristIndex) ||
                !HasValidJoint(joints, IndexMetacarpalIndex) ||
                !HasValidJoint(joints, ThumbTipIndex) ||
                !HasValidJoint(joints, IndexTipIndex))
            {
                return 0f;
            }

            float handScale = Vector3.Distance(
                joints[WristIndex].position,
                joints[IndexMetacarpalIndex].position);
            if (handScale <= Mathf.Epsilon)
            {
                return 0f;
            }

            float normalizedDistance = Vector3.Distance(
                joints[ThumbTipIndex].position,
                joints[IndexTipIndex].position) / handScale;
            return 1f - Mathf.InverseLerp(0.08f, 0.5f, normalizedDistance);
        }

        public static float CalculateFingerCurl(BasisWebXRJoint[] joints, int proximalIndex)
        {
            if (!HasValidJoint(joints, proximalIndex) ||
                !HasValidJoint(joints, proximalIndex + 1) ||
                !HasValidJoint(joints, proximalIndex + 2))
            {
                return 0f;
            }

            Vector3 proximal = joints[proximalIndex].position;
            Vector3 intermediate = joints[proximalIndex + 1].position;
            Vector3 distal = joints[proximalIndex + 2].position;
            float angle = Vector3.Angle(proximal - intermediate, distal - intermediate);
            return Mathf.Clamp01((180f - angle) / 90f);
        }

        public static BasisWebXRJoint[] CreateEmptyJointArray()
        {
            BasisWebXRJoint[] joints = new BasisWebXRJoint[JointCount];
            for (int index = 0; index < joints.Length; index++)
            {
                joints[index] = new BasisWebXRJoint { valid = true };
            }
            return joints;
        }

        private static bool HasValidJoint(BasisWebXRJoint[] joints, int index)
        {
            return joints != null &&
                   index >= 0 &&
                   index < joints.Length &&
                   joints[index] != null &&
                   joints[index].valid;
        }

        private static float Read(float[] values, int index)
        {
            return index < values.Length ? values[index] : 0f;
        }
    }
}
