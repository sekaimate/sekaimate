using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

public class BasisWebEncryptionTests
{
    private const string GoldenEncryptedPayload =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh+sywcehYcq8kSk97GppN8mBbIDRXDBCx1QTMuyBPqchg==";

    [Test]
    public async Task ExistingBeeCiphertextDecryptsWithoutFormatChanges()
    {
        byte[] encryptedData = Convert.FromBase64String(GoldenEncryptedPayload);
        var password = new BasisEncryptionWrapper.BasisPassword { VP = "basis-web-compatible" };

        BasisEncryptionWrapper.BasisDecryptResult result =
            await BasisEncryptionWrapper.DecryptFromBytesAsync("golden", password, encryptedData, null);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(Encoding.UTF8.GetString(result.Data), Is.EqualTo("BEE avatar and world payload"));
    }

    [Test]
    public void WebGlEncryptionOperationsRunWithoutThreadPoolDispatch()
    {
        string source = File.ReadAllText("Packages/com.basis.sdk/Scripts/BasisEncryptionWrapper.cs");

        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n        return EncryptFileInternalAsync(UniqueID, password, inputPath, outputPath, reportProgress);",
            source);
        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n        return DecryptFromBytesInternalAsync(UniqueID, password, encryptedData, reportProgress, ct);",
            source);
    }

    [Test]
    public void WebGlBeeCacheUsesSynchronousFileOperations()
    {
        string ioSource = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisIOManagement.cs");
        string metadataSource = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisLoadhandler.cs");

        StringAssert.Contains("CreateCacheReadStream", ioSource);
        StringAssert.Contains("CreateCacheWriteStream", ioSource);
        StringAssert.Contains("stream.Read(buffer, offset, count)", ioSource);
        StringAssert.Contains("fs.Write(sizeLE, 0, sizeLE.Length);", ioSource);
        StringAssert.Contains("File.WriteAllBytes(filePath, serializedData);", metadataSource);
    }
}
