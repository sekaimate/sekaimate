#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using UnityEngine;

public static class BasisWebCameraGpuReadback
{
    public static void ReadInto(RenderTexture source, Texture2D destination)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (source.width != destination.width || source.height != destination.height)
        {
            throw new ArgumentException("The destination texture dimensions must match the render texture.", nameof(destination));
        }

        RenderTexture readableSource = source;
        RenderTexture resolved = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (source.antiAliasing > 1)
            {
                RenderTextureDescriptor descriptor = source.descriptor;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                resolved = RenderTexture.GetTemporary(descriptor);
                Graphics.Blit(source, resolved);
                readableSource = resolved;
            }

            RenderTexture.active = readableSource;
            destination.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            destination.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
            if (resolved != null)
            {
                RenderTexture.ReleaseTemporary(resolved);
            }
        }
    }
}
#endif
