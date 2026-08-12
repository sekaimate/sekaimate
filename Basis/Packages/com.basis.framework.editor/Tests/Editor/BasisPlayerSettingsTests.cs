using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class BasisPlayerSettingsTests
{
    [Test]
    public void CameraUsageDescriptionIsConfigured()
    {
        string projectSettings = File.ReadAllText("ProjectSettings/ProjectSettings.asset");
        Match setting = Regex.Match(projectSettings, @"(?m)^  cameraUsageDescription:\s*(.+)$");

        Assert.That(setting.Success, Is.True);
        Assert.That(setting.Groups[1].Value.Trim(), Is.Not.Empty);
    }
}
