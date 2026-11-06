// Toon-обводка героев: к каждой сетке модели (тело, хвост) добавляется копия с шейдером Arena/Outline,
// которая рисуется изнанкой чуть шире модели — по краю получается контур. Копия следует за теми же костями.
// Включение, толщина и цвет — Inspector → Arena → Hero Art → «Обводка»; в игре F2 — выкл / тёмная / в цвет команды.
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public partial class HeroModel
    {
        static readonly Material[] outlineMat = new Material[2];
        static Shader outlineShader;
        Team outlineTeam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOutlineStatics() { outlineMat[0] = outlineMat[1] = null; outlineShader = null; }

        static Material OutlineMat(Team t)
        {
            var m = outlineMat[(int)t];
            if (m != null) return m;
            if (outlineShader == null) outlineShader = Shader.Find("Arena/Outline");
            if (outlineShader == null) { Debug.LogWarning("[Arena] нет шейдера Arena/Outline — обводки не будет"); return null; }
            m = outlineMat[(int)t] = new Material(outlineShader) { name = "outline_" + t };
            return m;
        }

        /// <summary>Копии сеток модели для обводки (после того как хвост повешен на кости).</summary>
        void BuildOutline(Team team)
        {
            outlineTeam = team;
            var mat = OutlineMat(team); if (mat == null) return;
            var made = new List<GameObject>();
            foreach (var src in Go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (src.sharedMesh == null) continue;
                var go = new GameObject("outline"); go.transform.SetParent(src.transform, false);
                var o = go.AddComponent<SkinnedMeshRenderer>();
                o.sharedMesh = src.sharedMesh; o.bones = src.bones; o.rootBone = src.rootBone; o.localBounds = src.localBounds;
                o.updateWhenOffscreen = src.updateWhenOffscreen;
                Setup(o, mat); made.Add(go);
            }
            foreach (var src in Go.GetComponentsInChildren<MeshRenderer>(true))
            {   // хвост из отдельной сетки без костей; предметы в руках (багор) не обводим — они тонкие
                if (!tails.Contains(src.transform)) continue;
                var mf = src.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject("outline"); go.transform.SetParent(src.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                Setup(go.AddComponent<MeshRenderer>(), mat); made.Add(go);
            }
            TickOutline();
        }

        static void Setup(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Каждый кадр: толщина и цвет из Inspector (выключена — толщина 0, оболочка прячется внутри модели).</summary>
        void TickOutline()
        {
            var m = outlineMat[(int)outlineTeam]; if (m == null) return;
            var T = Tune;
            Color tc = outlineTeam == Team.Home ? Mat.Home : Mat.Away;
            Color c = Color.Lerp(T.outlineColor, tc * T.outlineTeamBright, T.outlineTeam); c.a = 1;
            m.SetColor("_Color", c);
            m.SetFloat("_Width", T.outline ? T.outlineWidth : 0);
        }
    }
}
