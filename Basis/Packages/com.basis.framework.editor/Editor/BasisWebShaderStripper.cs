using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BasisWebShaderStripper : IPreprocessShaders
{
    private const string JiggleShaderName = "Jiggle/ProceduralPrimitiveURP";
    private static readonly ShaderKeyword DotsInstancingKeyword = new ShaderKeyword("DOTS_INSTANCING_ON");

    public int callbackOrder => 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            return;
        }

        for (int index = variants.Count - 1; index >= 0; index--)
        {
            if (ShouldStrip(shader.name, variants[index].shaderKeywordSet.IsEnabled(DotsInstancingKeyword)))
            {
                variants.RemoveAt(index);
            }
        }
    }

    public static bool ShouldStrip(string shaderName, bool usesDotsInstancing)
    {
        return usesDotsInstancing || shaderName == JiggleShaderName;
    }
}
