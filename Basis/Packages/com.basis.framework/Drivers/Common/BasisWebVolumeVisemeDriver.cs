using System;
using Basis.Scripts.BasisSdk;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    public sealed class BasisWebVolumeVisemeDriver : IDisposable
    {
        public const int MouthOpenVisemeIndex = 10;

        private const float NoiseFloor = 0.01f;
        private const float FullScaleLevel = 0.2f;
        private const float AttackSeconds = 0.04f;
        private const float ReleaseSeconds = 0.12f;
        private const float BlendShapeWriteEpsilon = 0.25f;

        private SkinnedMeshRenderer meshRenderer;
        private int blendShapeIndex = -1;
        private float targetLevel;
        private float currentLevel;
        private float lastAppliedWeight = -1f;
        private bool faceVisible = true;

        public bool Initialize(BasisAvatar avatar)
        {
            if (avatar?.FaceVisemeMesh?.sharedMesh == null ||
                avatar.FaceVisemeMovement == null ||
                avatar.FaceVisemeMovement.Length <= MouthOpenVisemeIndex)
            {
                return false;
            }

            int mappedBlendShape = avatar.FaceVisemeMovement[MouthOpenVisemeIndex];
            if (mappedBlendShape < 0 || mappedBlendShape >= avatar.FaceVisemeMesh.sharedMesh.blendShapeCount)
            {
                return false;
            }

            meshRenderer = avatar.FaceVisemeMesh;
            blendShapeIndex = mappedBlendShape;
            return true;
        }

        public void ProcessAudioSamples(float[] samples, int sampleCount)
        {
            targetLevel = MeasureNormalizedLevel(samples, sampleCount);
        }

        public void Simulate(float deltaTime)
        {
            currentLevel = UpdateEnvelope(currentLevel, targetLevel, deltaTime);
        }

        public void Apply()
        {
            if (!faceVisible || meshRenderer == null || blendShapeIndex < 0)
            {
                return;
            }

            float weight = currentLevel * 100f;
            if (Mathf.Abs(weight - lastAppliedWeight) < BlendShapeWriteEpsilon)
            {
                return;
            }

            meshRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
            lastAppliedWeight = weight;
        }

        public void SetFaceVisible(bool isVisible)
        {
            faceVisible = isVisible;
            if (!isVisible)
            {
                ZeroViseme();
            }
        }

        public void ZeroViseme()
        {
            targetLevel = 0f;
            currentLevel = 0f;
            if (meshRenderer != null && blendShapeIndex >= 0)
            {
                meshRenderer.SetBlendShapeWeight(blendShapeIndex, 0f);
                lastAppliedWeight = 0f;
            }
        }

        public void Dispose()
        {
            ZeroViseme();
            meshRenderer = null;
            blendShapeIndex = -1;
        }

        public static float MeasureNormalizedLevel(float[] samples, int sampleCount)
        {
            if (samples == null || sampleCount <= 0)
            {
                return 0f;
            }

            int count = Math.Min(sampleCount, samples.Length);
            if (count == 0)
            {
                return 0f;
            }

            double sumOfSquares = 0d;
            for (int index = 0; index < count; index++)
            {
                float sample = samples[index];
                sumOfSquares += sample * sample;
            }

            float rootMeanSquare = Mathf.Sqrt((float)(sumOfSquares / count));
            return Mathf.InverseLerp(NoiseFloor, FullScaleLevel, rootMeanSquare);
        }

        public static float UpdateEnvelope(float current, float target, float deltaTime)
        {
            float clampedCurrent = Mathf.Clamp01(current);
            float clampedTarget = Mathf.Clamp01(target);
            float timeConstant = clampedTarget > clampedCurrent ? AttackSeconds : ReleaseSeconds;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / timeConstant);
            return Mathf.Lerp(clampedCurrent, clampedTarget, blend);
        }
    }
}
