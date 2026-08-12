using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;

public static class BasisWebBuildConfiguration
{
    private const string AutomaticSyncProperty = "autoSyncPersistentDataPath: true,";
    private static readonly Regex ExistingAutomaticSync = new(
        @"(?m)^(?<indent>[ \t]*)autoSyncPersistentDataPath[ \t]*:[ \t]*(?:true|false)[ \t]*,?");
    private static readonly Regex ConfigDeclaration = new(
        @"(?m)^(?<indent>[ \t]*)(?:var|let|const)[ \t]+config[ \t]*=[ \t]*\{[ \t]*$");

    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.WebGL)
        {
            return;
        }

        string indexPath = Path.Combine(buildPath, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new BuildFailedException($"Web build output is missing {indexPath}.");
        }

        string html = File.ReadAllText(indexPath);
        string configured = ConfigureGeneratedIndex(target, html);
        if (configured != html)
        {
            File.WriteAllText(indexPath, configured, new UTF8Encoding(false));
        }
    }

    public static string ConfigureGeneratedIndex(BuildTarget target, string html)
    {
        return target == BuildTarget.WebGL ? AddAutomaticPersistentDataSync(html) : html;
    }

    public static string AddAutomaticPersistentDataSync(string html)
    {
        Match existingProperty = ExistingAutomaticSync.Match(html);
        if (existingProperty.Success)
        {
            string replacement = existingProperty.Groups["indent"].Value + AutomaticSyncProperty;
            return ExistingAutomaticSync.Replace(html, replacement, 1);
        }

        Match configDeclaration = ConfigDeclaration.Match(html);
        if (!configDeclaration.Success)
        {
            throw new InvalidDataException("Web build index does not contain the Unity config object.");
        }

        string newline = html.Contains("\r\n") ? "\r\n" : "\n";
        string propertyIndent = configDeclaration.Groups["indent"].Value + "  ";
        return html.Insert(
            configDeclaration.Index + configDeclaration.Length,
            newline + propertyIndent + AutomaticSyncProperty);
    }
}
