// Шейдеры для собранной игры. Игра создаёт материалы кодом (Shader.Find), а Unity кладёт в сборку только шейдеры
// и их варианты (ключевые слова), которые использует хоть один материал проекта. Без этого в .exe пустой экран.
// Скрипт сам создаёт материалы-якоря в Resources/ShaderRefs со всеми нужными сочетаниями.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.EditorTools
{
    [InitializeOnLoad]
    static class ArenaShaderRefs
    {
        const string Dir = "Assets/Arena/Resources/ShaderRefs";
        static ArenaShaderRefs() { EditorApplication.delayCall += Ensure; }

        [MenuItem("Arena/Обновить шейдеры для сборки")]
        static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Arena/Resources")) AssetDatabase.CreateFolder("Assets/Arena", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Arena/Resources", "ShaderRefs");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var part = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Make("lit", lit, false, false, false, false);
            Make("lit_normal", lit, true, false, false, false);
            Make("lit_emission", lit, false, true, false, false);
            Make("lit_normal_emission", lit, true, true, false, false);
            Make("unlit", unlit, false, false, false, false);
            Make("unlit_alpha", unlit, false, false, true, false);
            Make("unlit_add", unlit, false, false, true, true);
            Make("particles_alpha", part, false, false, true, false);
            Make("particles_add", part, false, false, true, true);
            AssetDatabase.SaveAssets();
        }

        static void Make(string name, Shader sh, bool normal, bool emission, bool transparent, bool additive)
        {
            if (sh == null) { Debug.LogWarning("[Arena] нет шейдера для " + name); return; }
            string path = Dir + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var m = new Material(sh) { name = name };
            if (normal) { m.EnableKeyword("_NORMALMAP"); m.SetTexture("_BumpMap", Texture2D.normalTexture); }
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); m.SetTexture("_EmissionMap", Texture2D.whiteTexture); }
            if (transparent)
            {   // так же, как Mat.Clear / Mat.ParticleMat
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(m, path);
        }
    }
}
