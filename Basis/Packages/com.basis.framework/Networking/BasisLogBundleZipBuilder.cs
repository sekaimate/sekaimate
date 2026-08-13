using System;
using System.IO;
using System.IO.Compression;
using System.Text;

internal sealed class BasisLogBundleZipBuilder : IDisposable
{
    private const int MaxEntries = 4_000_000;
    private static readonly char[] PortableInvalidCharacters = { '<', '>', ':', '"', '|', '?', '*' };

    private readonly MemoryStream containerStream;
    private readonly BinaryReader containerReader;
    private readonly MemoryStream zipStream;
    private readonly ZipArchive zipArchive;
    private readonly int totalEntries;
    private bool completed;

    public int CompletedEntries { get; private set; }

    public BasisLogBundleZipBuilder(byte[] container, int containerLength)
    {
        if (container == null)
        {
            throw new ArgumentNullException(nameof(container));
        }
        if (containerLength < sizeof(int) || containerLength > container.Length)
        {
            throw new InvalidDataException("Log container length is invalid.");
        }

        containerStream = new MemoryStream(container, 0, containerLength, writable: false);
        containerReader = new BinaryReader(containerStream, Encoding.UTF8, leaveOpen: true);
        totalEntries = containerReader.ReadInt32();
        if (totalEntries < 0 || totalEntries > MaxEntries)
        {
            throw new InvalidDataException($"Log container entry count {totalEntries} is invalid.");
        }

        zipStream = new MemoryStream();
        zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);
    }

    public bool AppendNext()
    {
        if (completed)
        {
            throw new InvalidOperationException("The ZIP archive is already complete.");
        }
        if (CompletedEntries == totalEntries)
        {
            return false;
        }

        string entryPath;
        int entryLength;
        try
        {
            entryPath = NormalizeEntryPath(containerReader.ReadString());
            entryLength = containerReader.ReadInt32();
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Log container ended before an entry header was complete.", exception);
        }

        if (entryLength < 0 || entryLength > containerStream.Length - containerStream.Position)
        {
            throw new InvalidDataException("Log container declared a file length past the end of the buffer.");
        }

        ZipArchiveEntry zipEntry = zipArchive.CreateEntry(entryPath, CompressionLevel.Fastest);
        using Stream destination = zipEntry.Open();
        CopyEntry(destination, entryLength);
        CompletedEntries++;
        return true;
    }

    public byte[] Complete()
    {
        if (CompletedEntries != totalEntries)
        {
            throw new InvalidOperationException($"Only {CompletedEntries} of {totalEntries} log entries were appended.");
        }
        if (containerStream.Position != containerStream.Length)
        {
            throw new InvalidDataException("Log container has trailing bytes.");
        }
        if (!completed)
        {
            zipArchive.Dispose();
            completed = true;
        }
        return zipStream.ToArray();
    }

    public void Dispose()
    {
        if (!completed)
        {
            zipArchive.Dispose();
            completed = true;
        }
        containerReader.Dispose();
        containerStream.Dispose();
        zipStream.Dispose();
    }

    private void CopyEntry(Stream destination, int entryLength)
    {
        byte[] buffer = new byte[Math.Min(entryLength, 64 * 1024)];
        int remaining = entryLength;
        while (remaining > 0)
        {
            int read = containerStream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new InvalidDataException("Log container ended before the declared file length.");
            }
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string NormalizeEntryPath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) || entryPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Log container entry path is empty or absolute.");
        }

        string[] segments = entryPath.Replace('\\', '/').Split('/');
        StringBuilder normalized = new StringBuilder(entryPath.Length);
        foreach (string segmentValue in segments)
        {
            if (string.IsNullOrEmpty(segmentValue) || segmentValue == "." || segmentValue == "..")
            {
                throw new InvalidDataException($"Log container entry path is unsafe: {entryPath}");
            }

            string segment = segmentValue;
            foreach (char invalidCharacter in PortableInvalidCharacters)
            {
                segment = segment.Replace(invalidCharacter, '_');
            }
            if (normalized.Length > 0)
            {
                normalized.Append('/');
            }
            normalized.Append(segment);
        }
        return normalized.ToString();
    }
}
