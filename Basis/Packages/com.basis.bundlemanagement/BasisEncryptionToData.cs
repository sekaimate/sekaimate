using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
public static class BasisEncryptionToData
{
    public static async Task<AssetBundle> GenerateBundleFromFile(string Password, byte[] Bytes, uint CRC, BasisProgressReport progressCallback)
    {
        // Define the password object for decryption
        var BasisPassword = new BasisEncryptionWrapper.BasisPassword
        {
            VP = Password
        };
        string UniqueID = BasisGenerateUniqueID.GenerateUniqueID();
        // Decrypt the file asynchronously
        var decrypted = await BasisEncryptionWrapper.DecryptFromBytesAsync(UniqueID, BasisPassword, Bytes, progressCallback);

        if (!decrypted.Success || decrypted.Data == null || decrypted.Data.Length == 0)
        {
            BasisDebug.LogError($"Decrypt failed: {decrypted.Error} | {decrypted.Message}");
            return null; // <-- critical
        }

        BasisDebug.Log("Attempting Asset Bundle Load...", BasisDebug.LogTag.Event);

#if UNITY_WEBGL && !UNITY_EDITOR
        AssetBundle assetBundle = AssetBundle.LoadFromMemory(decrypted.Data, CRC);
#else
        AssetBundleCreateRequest assetBundleCreateRequest;
        try
        {
            assetBundleCreateRequest = AssetBundle.LoadFromMemoryAsync(decrypted.Data, CRC);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"LoadFromMemoryAsync threw: {ex}");
            return null;
        }
        // Track the last reported progress
        int lastReportedProgress = -1;

        // Periodically check the progress of AssetBundleCreateRequest and report progress
        while (!assetBundleCreateRequest.isDone)
        {
            // Convert the progress to a percentage (0-100)
            int progress = Mathf.RoundToInt(assetBundleCreateRequest.progress * 100);

            // Report progress only if it has changed
            if (progress > lastReportedProgress)
            {
                lastReportedProgress = progress;

                // Call the progress callback with the current progress
                progressCallback.ReportProgress(UniqueID.ToString(), progress, "loading bundle");
            }

            // Wait a short period before checking again to avoid busy waiting
            await Task.Delay(50); // Adjust delay as needed (e.g., 50ms)
        }

        await assetBundleCreateRequest;
        AssetBundle assetBundle = assetBundleCreateRequest.assetBundle;
#endif

        if (assetBundle == null)
        {
            BasisDebug.LogError("AssetBundle load finished but the bundle is null (CRC mismatch or invalid bundle bytes).");
            return null;
        }

        progressCallback?.ReportProgress(UniqueID, 100, "loading bundle");
        return assetBundle;
    }
    public static async Task<BasisBundleConnector> GenerateMetaFromBytes(string password, byte[] encryptedBytes, BasisProgressReport progressCallback)
    {
        var basisPassword = new BasisEncryptionWrapper.BasisPassword { VP = password };
        string uniqueID = BasisGenerateUniqueID.GenerateUniqueID();

        var decryptedMeta = await BasisEncryptionWrapper.DecryptFromBytesAsync(uniqueID, basisPassword, encryptedBytes, progressCallback);


        if (decryptedMeta.Success)
        {
            return ConvertBytesToJson(decryptedMeta.Data, out var connector) ? connector : null;
        }
        else
        {
            BasisDebug.LogError($"Failed to Decrypt, {decryptedMeta.Error} | {decryptedMeta.Message} | {decryptedMeta.Exception}");
            return null;
        }
    }

    public static bool ConvertBytesToJson(byte[] data, out BasisBundleConnector connector)
    {
        connector = null;

        if (data == null || data.Length == 0)
        {
            BasisDebug.LogError($"Data for {nameof(BasisBundleConnector)} is empty or null.", BasisDebug.LogTag.Event);
            return false;
        }

        try
        {
            connector = BasisSerialization.DeserializeValue<BasisBundleConnector>(data);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"DeserializeValue<BasisBundleConnector> threw {ex.GetType().Name}: {ex.Message}. PlaintextLength={data.Length}. Head={JsonHeadPreview(data)}");
            return false;
        }

        if (connector == null)
        {
            BasisDebug.LogError($"DeserializeValue<BasisBundleConnector> returned null (decrypt OK but wrapper/Value missing). PlaintextLength={data.Length}. Head={JsonHeadPreview(data)}");
            return false;
        }

        return true;
    }

    private static string JsonHeadPreview(byte[] data)
    {
        if (data == null || data.Length == 0) return "<empty>";
        int count = Math.Min(data.Length, 256);
        string preview = Encoding.UTF8.GetString(data, 0, count);
        StringBuilder sb = new StringBuilder(preview.Length);
        for (int i = 0; i < preview.Length; i++)
        {
            char c = preview[i];
            sb.Append(c < 0x20 || c == 0x7F ? '.' : c);
        }
        if (data.Length > count) sb.Append("...");
        return sb.ToString();
    }
}
