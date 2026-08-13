#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

internal sealed class BasisWebLogBundleDownload : MonoBehaviour
{
    private const string ContentType = "application/zip";

    public static void Start(byte[] payload, int payloadLength, int rawLength, bool compressed, string serverNameSafe)
    {
        GameObject runnerObject = new GameObject(nameof(BasisWebLogBundleDownload));
        DontDestroyOnLoad(runnerObject);
        BasisWebLogBundleDownload runner = runnerObject.AddComponent<BasisWebLogBundleDownload>();
        runner.StartCoroutine(runner.BuildAndDownload(payload, payloadLength, rawLength, compressed, serverNameSafe));
    }

    private IEnumerator BuildAndDownload(
        byte[] payload,
        int payloadLength,
        int rawLength,
        bool compressed,
        string serverNameSafe)
    {
        yield return null;
        BasisLogBundleZipBuilder builder = null;
        Exception failure = null;
        try
        {
            byte[] raw = BasisLogBundleReceiver.DecodePayload(payload, payloadLength, rawLength, compressed);
            builder = new BasisLogBundleZipBuilder(raw, raw.Length);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        if (failure != null)
        {
            BasisLogBundleReceiver.FailBrowserDownload(failure);
            Destroy(gameObject);
            yield break;
        }

        while (true)
        {
            bool appended;
            try
            {
                appended = builder.AppendNext();
            }
            catch (Exception exception)
            {
                failure = exception;
                appended = false;
            }
            if (failure != null)
            {
                builder.Dispose();
                BasisLogBundleReceiver.FailBrowserDownload(failure);
                Destroy(gameObject);
                yield break;
            }
            if (!appended)
            {
                break;
            }
            yield return null;
        }

        try
        {
            byte[] zip = builder.Complete();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            BasisWebFileDownload.Save($"{serverNameSafe}_{timestamp}.zip", zip, ContentType);
            BasisLogBundleReceiver.CompleteBrowserDownload(builder.CompletedEntries);
        }
        catch (Exception exception)
        {
            BasisLogBundleReceiver.FailBrowserDownload(exception);
        }
        finally
        {
            builder.Dispose();
            Destroy(gameObject);
        }
    }
}
#endif
