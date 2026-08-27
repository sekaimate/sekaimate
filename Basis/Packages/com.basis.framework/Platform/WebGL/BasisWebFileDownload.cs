#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

public static class BasisWebFileDownload
{
    public static void Save(string filename, byte[] data, string contentType)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("A browser download filename is required.", nameof(filename));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Browser download data must not be empty.", nameof(data));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("A browser download content type is required.", nameof(contentType));
        }

        BasisWebDownloadFile(data, data.Length, filename, contentType);
    }

    [DllImport("__Internal")]
    private static extern void BasisWebDownloadFile(byte[] data, int length, string filename, string contentType);
}
#endif
