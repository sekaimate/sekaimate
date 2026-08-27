using System;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class BasisWebRuntimePcmAudioSource : MonoBehaviour
{
    private const int SampleRate = 48000;
    private const int DurationSeconds = 2;
    private const float Frequency = 440f;
    private AudioClip runtimeClip;

    private void OnEnable()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            return;
        }

        runtimeClip = AudioClip.Create(
            $"{gameObject.name}-RuntimePCM",
            SampleRate * DurationSeconds,
            1,
            SampleRate,
            false);
        runtimeClip.hideFlags = HideFlags.DontSave;

        var samples = new float[SampleRate * DurationSeconds];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = Mathf.Sin(2f * Mathf.PI * Frequency * index / SampleRate) * 0.2f;
        }

        if (!runtimeClip.SetData(samples, 0))
        {
            throw new InvalidOperationException("Failed to initialize runtime PCM audio.");
        }

        audioSource.clip = runtimeClip;
        if (Application.isPlaying && audioSource.playOnAwake)
        {
            audioSource.Play();
        }
    }

    private void OnDisable()
    {
        if (runtimeClip == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeClip);
        }
        else
        {
            DestroyImmediate(runtimeClip);
        }
        runtimeClip = null;
    }
}
