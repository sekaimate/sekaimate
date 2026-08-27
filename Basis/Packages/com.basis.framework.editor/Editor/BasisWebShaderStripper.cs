using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

public sealed class BasisWebShaderStripper : IPreprocessShaders
{
    private const string JiggleShaderName = "Jiggle/ProceduralPrimitiveURP";
    private const string VoxelizeShaderName = "Hidden/VoxelizeShader";
    public int callbackOrder => 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            return;
        }

        for (int index = variants.Count - 1; index >= 0; index--)
        {
            if (ShouldStrip(shader.name))
            {
                variants.RemoveAt(index);
            }
        }
    }

    public static bool ShouldStrip(string shaderName)
    {
        return shaderName == JiggleShaderName || shaderName == VoxelizeShaderName;
    }
}
