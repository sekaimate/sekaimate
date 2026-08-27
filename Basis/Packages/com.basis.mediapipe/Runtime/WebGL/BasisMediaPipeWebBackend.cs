#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace Basis.MediaPipe.WebGL
{
    public sealed class BasisMediaPipeWebBackend : IBasisMediaPipeBackend
    {
        private const int HeaderLength = 7;
        private const int FaceDataLength = 20;

        private delegate void StateCallback(int state);
        private delegate void ResultCallback(IntPtr values, int valueCount);

        private static readonly StateCallback StateChanged = HandleStateChanged;
        private static readonly ResultCallback ResultReceived = HandleResult;
        private static BasisMediaPipeWebBackend instance;

        private BasisMediaPipeResult latest;
        private bool hasLatest;

        public bool IsAvailable { get; private set; }
        public string BackendName => "MediaPipe Tasks Vision Web";
        public bool UsesUnityCamera => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            BasisMediaPipeBackendRegistry.Register(() => new BasisMediaPipeWebBackend());
        }

        public void Initialize(BasisMediaPipeConfig config)
        {
            instance = this;
            IsAvailable = true;
            BasisMediaPipeWebInitialize(
                config.EnableFace ? 1 : 0,
                config.EnableHands ? 1 : 0,
                config.EnablePose ? 1 : 0,
                config.MirrorHorizontally ? 1 : 0,
                config.SwapHands ? 1 : 0,
                config.CameraDeviceName,
                config.CameraWidth,
                config.CameraHeight,
                config.TargetFps,
                StateChanged,
                ResultReceived);
        }

        public void SubmitFrame(WebCamTexture frame, double timestampMs)
        {
            if (IsAvailable)
            {
                BasisMediaPipeWebPump(timestampMs);
            }
        }

        public bool TryGetLatestResult(out BasisMediaPipeResult result)
        {
            if (hasLatest)
            {
                result = latest;
                hasLatest = false;
                return true;
            }

            result = default;
            return false;
        }

        public string TimingBreakdown() => string.Empty;

        public void Shutdown()
        {
            BasisMediaPipeWebShutdown();
            IsAvailable = false;
            hasLatest = false;
            if (instance == this)
            {
                instance = null;
            }
        }

        [MonoPInvokeCallback(typeof(StateCallback))]
        private static void HandleStateChanged(int state)
        {
            if (instance == null)
            {
                return;
            }

            if (state == 1)
            {
                instance.IsAvailable = true;
                BasisDebug.Log("BasisMediaPipe(web): browser camera and inference worker are ready.");
                return;
            }

            instance.IsAvailable = false;
            BasisDebug.LogError($"BasisMediaPipe(web): initialization failed with state {state}.");
        }

        [MonoPInvokeCallback(typeof(ResultCallback))]
        private static void HandleResult(IntPtr values, int valueCount)
        {
            if (instance == null || valueCount < HeaderLength + FaceDataLength)
            {
                return;
            }

            float[] data = new float[valueCount];
            Marshal.Copy(values, data, 0, valueCount);
            if (!TryParseResult(data, out BasisMediaPipeResult result))
            {
                BasisDebug.LogError("BasisMediaPipe(web): received an invalid inference result.");
                return;
            }

            instance.latest = result;
            instance.hasLatest = true;
        }

        private static bool TryParseResult(float[] data, out BasisMediaPipeResult result)
        {
            result = default;
            int flags = (int)data[1];
            int blendshapeCount = (int)data[2];
            int leftHandCount = (int)data[3];
            int rightHandCount = (int)data[4];
            int poseCount = (int)data[5];
            int poseWorldCount = (int)data[6];
            int expectedLength = HeaderLength + FaceDataLength + blendshapeCount
                + (leftHandCount + rightHandCount + poseCount + poseWorldCount) * 3;
            if (blendshapeCount < 0 || leftHandCount < 0 || rightHandCount < 0
                || poseCount < 0 || poseWorldCount < 0 || expectedLength != data.Length)
            {
                return false;
            }

            int offset = HeaderLength;
            result.TimestampMs = data[0];
            result.HasFace = (flags & 1) != 0;
            result.HasLeftHand = (flags & 2) != 0;
            result.HasRightHand = (flags & 4) != 0;
            result.HasPose = (flags & 8) != 0;
            result.FaceTransform = ReadMatrix(data, ref offset);
            result.HeadImagePosition = new Vector2(data[offset++], data[offset++]);
            result.FaceImageSize = data[offset++];
            result.TongueOut = data[offset++];
            result.FaceBlendshapes = ReadScalars(data, ref offset, blendshapeCount);
            result.LeftHandLandmarks = ReadVectors(data, ref offset, leftHandCount);
            result.RightHandLandmarks = ReadVectors(data, ref offset, rightHandCount);
            result.PoseLandmarks = ReadVectors(data, ref offset, poseCount);
            result.PoseWorldLandmarks = ReadVectors(data, ref offset, poseWorldCount);
            return true;
        }

        private static Matrix4x4 ReadMatrix(float[] data, ref int offset)
        {
            Matrix4x4 matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    matrix[row, column] = data[offset++];
                }
            }
            return matrix;
        }

        private static float[] ReadScalars(float[] data, ref int offset, int count)
        {
            float[] values = new float[count];
            Array.Copy(data, offset, values, 0, count);
            offset += count;
            return values;
        }

        private static Vector3[] ReadVectors(float[] data, ref int offset, int count)
        {
            Vector3[] values = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = new Vector3(data[offset++], data[offset++], data[offset++]);
            }
            return values;
        }

        [DllImport("__Internal")]
        private static extern void BasisMediaPipeWebInitialize(
            int enableFace,
            int enableHands,
            int enablePose,
            int mirror,
            int swapHands,
            string cameraDeviceName,
            int width,
            int height,
            int targetFps,
            StateCallback onStateChanged,
            ResultCallback onResult);

        [DllImport("__Internal")]
        private static extern void BasisMediaPipeWebPump(double timestampMs);

        [DllImport("__Internal")]
        private static extern void BasisMediaPipeWebShutdown();
    }
}
#endif
