using System;

public static class BasisWebBeeArtifactValidator
{
    public static bool TryValidate(
        BasisBundleConnector connector,
        long connectorLength,
        long fileLength,
        out string error)
    {
        error = string.Empty;

        if (connector == null)
        {
            error = "BEE connector is missing.";
            return false;
        }

        BasisBundleGenerated[] sections = connector.BasisBundleGenerated;
        if (sections == null || sections.Length != 1 || sections[0] == null)
        {
            error = "BEE must contain exactly one WebGL section.";
            return false;
        }

        BasisBundleGenerated section = sections[0];
        if (!string.Equals(section.Platform, "WebGL", StringComparison.Ordinal))
        {
            error = $"BEE section platform must be WebGL, but was {section.Platform}.";
            return false;
        }

        if (!string.Equals(section.AssetMode, "Scene", StringComparison.Ordinal))
        {
            error = $"BEE section asset mode must be Scene, but was {section.AssetMode}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(section.AssetToLoadName))
        {
            error = "BEE section asset name is missing.";
            return false;
        }

        if (connectorLength <= 0 || section.EndByte <= 0)
        {
            error = "BEE connector or section length is invalid.";
            return false;
        }

        long expectedFileLength;
        try
        {
            expectedFileLength = checked(BasisBeeConstants.RemoteHeaderSize + connectorLength + section.EndByte);
        }
        catch (OverflowException)
        {
            error = "BEE file length exceeds the supported range.";
            return false;
        }

        if (fileLength != expectedFileLength)
        {
            error = $"BEE file length must be {expectedFileLength}, but was {fileLength}.";
            return false;
        }

        return true;
    }
}
