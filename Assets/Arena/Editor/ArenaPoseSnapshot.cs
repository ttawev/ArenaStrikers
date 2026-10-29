// Arena → Снимок позы героев: проигрывает бег в редакторе и сохраняет деформированную модель в Logs/pose_<герой>_<кадр>.obj,
// чтобы Claude мог посмотреть, как Unity на самом деле гнёт кожу.
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ArenaPoseSnapshot
{
    [MenuItem("Arena/Снимок позы героев")]
    static void Run()
    {
        var ci = CultureInfo.InvariantCulture;
        AssetDatabase.Refresh(); // pick up models just rebuilt in Blender
        foreach (var d in Directory.GetDirectories("Assets/Arena/Resources/Heroes"))
        {
            string mp = d.Replace('\\', '/') + "/model.fbx";
            if (File.Exists(mp)) AssetDatabase.ImportAsset(mp, ImportAssetOptions.ForceUpdate);
        }
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Arena/Resources/Heroes/Anim/run.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == "run");
        if (clip == null) { Debug.LogWarning("нет клипа run"); return; }
        Directory.CreateDirectory("Logs");
        foreach (var dir in Directory.GetDirectories("Assets/Arena/Resources/Heroes"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir.Replace('\\', '/') + "/model.fbx");
            if (prefab == null) continue;
            var go = Object.Instantiate(prefab);
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
            AnimationMode.StartAnimationMode();
            foreach (float f in new[] { 0f, 0.25f, 0.5f })
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(go, clip, clip.length * f);
                AnimationMode.EndSampling();
                var m = new Mesh();
                skin.BakeMesh(m, true);
                var sb = new StringBuilder();
                var tr = skin.transform;
                foreach (var v in m.vertices) { var w = go.transform.InverseTransformPoint(tr.TransformPoint(v)); sb.Append("v ").Append(w.x.ToString(ci)).Append(' ').Append(w.y.ToString(ci)).Append(' ').Append(w.z.ToString(ci)).Append('\n'); }
                foreach (var u in m.uv) sb.Append("vt ").Append(u.x.ToString(ci)).Append(' ').Append(u.y.ToString(ci)).Append('\n');
                var t = m.triangles;
                for (int i = 0; i < t.Length; i += 3) sb.Append($"f {t[i] + 1}/{t[i] + 1} {t[i + 1] + 1}/{t[i + 1] + 1} {t[i + 2] + 1}/{t[i + 2] + 1}\n");
                File.WriteAllText($"Logs/pose_{Path.GetFileName(dir)}_{(int)(f * 100)}.obj", sb.ToString());
                Object.DestroyImmediate(m);
            }
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(go);
        }
        Debug.Log("[Arena] снимки поз записаны в Logs/pose_*.obj");
    }
}
