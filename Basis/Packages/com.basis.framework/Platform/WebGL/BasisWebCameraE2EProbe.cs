#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BasisWebCameraE2EProbe : MonoBehaviour
{
    private const string QueryKey = "basisCameraE2E";
    private const int FlatWidth = 320;
    private const int FlatHeight = 180;
    private const int PanoramaWidth = 256;
    private const int PanoramaHeight = 128;
    private const int PanoramaFaceSize = 128;
    private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
    private string mode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
        string mode = ReadQueryValue(Application.absoluteURL, QueryKey);
        if (!IsCaptureMode(mode))
        {
            return;
        }

        var probe = new GameObject(nameof(BasisWebCameraE2EProbe));
        UnityEngine.Object.DontDestroyOnLoad(probe);
        probe.AddComponent<BasisWebCameraE2EProbe>().Initialize(mode);
    }

    private static bool IsCaptureMode(string mode)
    {
        return mode == "flat-png" ||
               mode == "flat-exr" ||
               mode == "panorama-png" ||
               mode == "panorama-exr";
    }

    private static string ReadQueryValue(string absoluteUrl, string key)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
        {
            return string.Empty;
        }

        foreach (string field in uri.Query.TrimStart('?').Split('&'))
        {
            string[] pair = field.Split(new[] { '=' }, 2);
            if (Uri.UnescapeDataString(pair[0]) == key)
            {
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }

        return string.Empty;
    }

    private static void Publish(
        string mode,
        string stage,
        int width,
        int height,
        int distinctPixelSamples = 0,
        string error = "")
    {
        BasisWebCameraE2EPublish(JsonUtility.ToJson(new ProbeResult
        {
            mode = mode,
            stage = stage,
            width = width,
            height = height,
            distinctPixelSamples = distinctPixelSamples,
            error = error
        }));
    }

    [DllImport("__Internal")]
    private static extern void BasisWebCameraE2EPublish(string resultJson);

    [Serializable]
    private sealed class ProbeResult
    {
        public string mode;
        public string stage;
        public int width;
        public int height;
        public int distinctPixelSamples;
        public string error;
    }

    public void Initialize(string captureMode)
    {
        mode = captureMode;
        StartCoroutine(Capture());
    }

    private IEnumerator Capture()
    {
        Publish(mode, "rendering", 0, 0);
        yield return new WaitForEndOfFrame();

        try
        {
            if (mode.StartsWith("flat-", StringComparison.Ordinal))
            {
                CaptureFlat(mode.EndsWith("-exr", StringComparison.Ordinal));
            }
            else
            {
                CapturePanorama(mode.EndsWith("-exr", StringComparison.Ordinal));
            }
        }
        catch (Exception exception)
        {
            Publish(mode, "failed", 0, 0, 0, exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            ReleaseResources();
        }
    }

    private void CaptureFlat(bool exr)
    {
        Camera camera = CreateCamera();
        CreateSubject(camera.transform.position + camera.transform.forward * 3f, Color.cyan);

        RenderTextureFormat renderFormat = exr ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGB32;
        var target = new RenderTexture(FlatWidth, FlatHeight, 24, renderFormat)
        {
            antiAliasing = 1
        };
        target.Create();
        resources.Add(target);
        camera.targetTexture = target;
        camera.Render();

        TextureFormat textureFormat = exr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;
        var texture = new Texture2D(FlatWidth, FlatHeight, textureFormat, false);
        resources.Add(texture);
        BasisWebCameraGpuReadback.ReadInto(target, texture);
        byte[] raw = texture.GetRawTextureData<byte>().ToArray();

        byte[] imageData = exr ? texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP) : texture.EncodeToPNG();
        string extension = exr ? "exr" : "png";
        string contentType = exr ? "application/octet-stream" : "image/png";
        BasisWebFileDownload.Save($"Screenshot_E2E_{FlatWidth}x{FlatHeight}.{extension}", imageData, contentType);
        Publish(mode, "downloaded", FlatWidth, FlatHeight, CountDistinctPixelSamples(raw, exr ? 16 : 4));
    }

    private void CapturePanorama(bool exr)
    {
        Camera camera = CreateCamera();
        Vector3 center = camera.transform.position;
        CreateSubject(center + Vector3.forward * 3f, Color.red);
        CreateSubject(center + Vector3.back * 3f, Color.green);
        CreateSubject(center + Vector3.right * 3f, Color.blue);
        CreateSubject(center + Vector3.left * 3f, Color.yellow);
        CreateSubject(center + Vector3.up * 3f, Color.magenta);
        CreateSubject(center + Vector3.down * 3f, Color.cyan);

        var cubemap = new RenderTexture(
            PanoramaFaceSize,
            PanoramaFaceSize,
            24,
            RenderTextureFormat.ARGBFloat)
        {
            dimension = TextureDimension.Cube,
            useMipMap = false,
            autoGenerateMips = false
        };
        cubemap.Create();
        resources.Add(cubemap);

        var equirect = new RenderTexture(
            PanoramaWidth,
            PanoramaHeight,
            0,
            RenderTextureFormat.ARGBFloat)
        {
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            sRGB = false
        };
        equirect.Create();
        resources.Add(equirect);

        if (!camera.RenderToCubemap(cubemap, 63, Camera.MonoOrStereoscopicEye.Mono))
        {
            throw new InvalidOperationException("RenderToCubemap failed.");
        }

        cubemap.ConvertToEquirect(equirect, Camera.MonoOrStereoscopicEye.Mono);
        var readback = new Texture2D(PanoramaWidth, PanoramaHeight, TextureFormat.RGBAFloat, false);
        resources.Add(readback);
        BasisWebCameraGpuReadback.ReadInto(equirect, readback);
        byte[] raw = readback.GetRawTextureData<byte>().ToArray();

        byte[] imageData;
        if (exr)
        {
            imageData = readback.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
        }
        else
        {
            byte[] rgba = BasisHandHeldCamera.TonemapEquirectToRgba32(raw, PanoramaWidth, PanoramaHeight, 1f, 1f, 1f);
            var output = new Texture2D(PanoramaWidth, PanoramaHeight, TextureFormat.RGBA32, false);
            resources.Add(output);
            output.LoadRawTextureData(rgba);
            output.Apply(false);
            imageData = output.EncodeToPNG();
        }

        string extension = exr ? "exr" : "png";
        string contentType = exr ? "application/octet-stream" : "image/png";
        BasisWebFileDownload.Save($"Screenshot360_Mono_E2E_{PanoramaWidth}x{PanoramaHeight}.{extension}", imageData, contentType);
        Publish(mode, "downloaded", PanoramaWidth, PanoramaHeight, CountDistinctPixelSamples(raw, 16));
    }

    private static int CountDistinctPixelSamples(byte[] raw, int bytesPerPixel)
    {
        int pixelCount = raw.Length / bytesPerPixel;
        int stride = Mathf.Max(1, pixelCount / 128);
        var hashes = new HashSet<uint>();
        for (int pixel = 0; pixel < pixelCount; pixel += stride)
        {
            int offset = pixel * bytesPerPixel;
            uint hash = 2166136261u;
            for (int byteIndex = 0; byteIndex < bytesPerPixel; byteIndex++)
            {
                hash = (hash ^ raw[offset + byteIndex]) * 16777619u;
            }
            hashes.Add(hash);
        }
        return hashes.Count;
    }

    private Camera CreateCamera()
    {
        var cameraObject = new GameObject("Web Camera E2E Camera");
        resources.Add(cameraObject);
        cameraObject.transform.position = new Vector3(10000f, 10000f, 10000f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.04f, 0.08f, 1f);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 20f;
        camera.fieldOfView = 60f;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        return camera;
    }

    private void CreateSubject(Vector3 position, Color color)
    {
        GameObject subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        resources.Add(subject);
        subject.transform.position = position;
        subject.transform.localScale = Vector3.one * 1.25f;
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        resources.Add(material);
        material.SetColor("_BaseColor", color);
        subject.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void ReleaseResources()
    {
        foreach (UnityEngine.Object resource in resources)
        {
            if (resource is RenderTexture renderTexture)
            {
                renderTexture.Release();
            }
            Destroy(resource);
        }
        resources.Clear();
        Destroy(gameObject);
    }
}
#endif
