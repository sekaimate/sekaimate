using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

public sealed class BasisWebLogBundleTests
{
    private const string LogReceiverPath = "Packages/com.basis.framework/Networking/BasisLogBundleReceiver.cs";
    private const string LogDownloadPath = "Packages/com.basis.framework/Networking/WebGL/BasisWebLogBundleDownload.cs";

    [Test]
    public void WebGlUsesCoroutineAndBrowserDownloadWhileNativeUsesTaskRun()
    {
        string receiver = File.ReadAllText(LogReceiverPath);
        string download = File.ReadAllText(LogDownloadPath);

        StringAssert.Contains("BasisWebLogBundleDownload.Start", receiver);
        StringAssert.Contains("Task.Run(() => ExpandAndNotify", receiver);
        StringAssert.Contains("yield return null", download);
        StringAssert.Contains("BasisWebFileDownload.Save", download);
        StringAssert.Contains("application/zip", download);
    }

    [Test]
    public void ContainerIsAppendedToZipOneEntryAtATime()
    {
        byte[] container = CreateContainer(("root/server.log", "hello"), ("nested/error.log", "world"));
        using BasisLogBundleZipBuilder builder = new BasisLogBundleZipBuilder(container, container.Length);

        Assert.That(builder.AppendNext(), Is.True);
        Assert.That(builder.CompletedEntries, Is.EqualTo(1));
        Assert.That(builder.AppendNext(), Is.True);
        Assert.That(builder.CompletedEntries, Is.EqualTo(2));
        Assert.That(builder.AppendNext(), Is.False);

        using MemoryStream zipBytes = new MemoryStream(builder.Complete());
        using ZipArchive archive = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        Assert.That(archive.Entries.Count, Is.EqualTo(2));
        Assert.That(ReadText(archive.GetEntry("root/server.log")), Is.EqualTo("hello"));
        Assert.That(ReadText(archive.GetEntry("nested/error.log")), Is.EqualTo("world"));
    }

    [Test]
    public void ContainerRejectsTraversalAndTruncatedFiles()
    {
        byte[] traversal = CreateContainer(("../outside.log", "no"));
        using BasisLogBundleZipBuilder traversalBuilder = new BasisLogBundleZipBuilder(traversal, traversal.Length);
        Assert.Throws<InvalidDataException>(() => traversalBuilder.AppendNext());

        byte[] valid = CreateContainer(("server.log", "hello"));
        using BasisLogBundleZipBuilder builder = new BasisLogBundleZipBuilder(valid, valid.Length - 1);
        Assert.Throws<InvalidDataException>(() => builder.AppendNext());
    }

    private static byte[] CreateContainer(params (string Path, string Text)[] files)
    {
        using MemoryStream memory = new MemoryStream();
        using (BinaryWriter writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(files.Length);
            foreach ((string path, string text) in files)
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                writer.Write(path);
                writer.Write(data.Length);
                writer.Write(data);
            }
        }
        return memory.ToArray();
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        Assert.That(entry, Is.Not.Null);
        using StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
