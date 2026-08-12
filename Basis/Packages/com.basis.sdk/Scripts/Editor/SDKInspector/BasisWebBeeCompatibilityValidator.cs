using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.BasisSdk;
using UnityEditor;
using UnityEngine;

public static class BasisWebBeeCompatibilityValidator
{
    private static readonly string[] UnsupportedComponentTypeNames =
    {
        "UnityEngine.VFX.VisualEffect",
        "UnityEngine.VFX.VFXRenderer",
    };

    private static readonly string[] UnsupportedShaderNames =
    {
        "Jiggle/ProceduralPrimitiveURP",
        "Hidden/VoxelizeShader",
    };

    public static bool TryValidate(
        BasisContentBase content,
        IReadOnlyCollection<BuildTarget> targets,
        out string error)
    {
        error = string.Empty;
        if (!targets.Contains(BuildTarget.WebGL))
        {
            return true;
        }

        Component[] components = GetComponents(content);
        string[] componentTypeNames = components
            .Where(component => component != null)
            .Select(component => component.GetType().FullName)
            .Where(typeName => !string.IsNullOrEmpty(typeName))
            .ToArray();
        string[] shaderNames = components
            .OfType<Renderer>()
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null && material.shader != null)
            .Select(material => material.shader.name)
            .ToArray();
        string[] unsupportedContent = FindUnsupportedContent(targets, componentTypeNames, shaderNames);

        if (unsupportedContent.Length == 0)
        {
            return true;
        }

        error = "WebGL BEE build failed because the content uses unsupported features:\n- "
            + string.Join("\n- ", unsupportedContent);
        return false;
    }

    public static string[] FindUnsupportedContent(
        IReadOnlyCollection<BuildTarget> targets,
        IEnumerable<string> componentTypeNames,
        IEnumerable<string> shaderNames)
    {
        if (!targets.Contains(BuildTarget.WebGL))
        {
            return Array.Empty<string>();
        }

        HashSet<string> components = new HashSet<string>(componentTypeNames);
        HashSet<string> shaders = new HashSet<string>(shaderNames);
        return UnsupportedComponentTypeNames
            .Where(components.Contains)
            .Select(typeName => $"Component: {typeName}")
            .Concat(UnsupportedShaderNames
                .Where(shaders.Contains)
                .Select(shaderName => $"Shader: {shaderName}"))
            .ToArray();
    }

    private static Component[] GetComponents(BasisContentBase content)
    {
        if (content is BasisScene)
        {
            return content.gameObject.scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .ToArray();
        }

        return content.GetComponentsInChildren<Component>(true);
    }
}
