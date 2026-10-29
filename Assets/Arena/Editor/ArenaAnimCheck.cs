// Arena → Проверить анимации: проигрывает клипы на каждом герое в редакторе и пишет в Logs/arena-anim-check.log,
// одинаково ли двигаются левая и правая нога (высота стопы, наклон стопы, сгиб колена). Для поиска «хромоты».
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class ArenaAnimCheck
{
    [MenuItem("Arena/Проверить анимации (хромота)")]
    static void Run()
    {
        var sb = new StringBuilder();
        string animDir = "Assets/Arena/Resources/Heroes/Anim/";
        foreach (var dir in Directory.GetDirectories("Assets/Arena/Resources/Heroes"))
        {
            string modelPath = dir.Replace('\\', '/') + "/model.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null) continue;
            foreach (var clipName in new[] { "run", "idle" })
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(animDir + clipName + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) { sb.AppendLine(clipName + ": нет клипа"); continue; }
                var go = Object.Instantiate(prefab);
                var anim = go.GetComponent<Animator>();
                if (anim == null || !anim.isHuman) { sb.AppendLine(dir + ": не гуманоид"); Object.DestroyImmediate(go); continue; }
                anim.applyRootMotion = false;
                AnimationMode.StartAnimationMode();
                var stat = new float[2, 6]; // side: footH min/max, pitch min/max, knee min/max
                for (int s = 0; s < 2; s++) { stat[s, 0] = stat[s, 2] = stat[s, 4] = 1e9f; stat[s, 1] = stat[s, 3] = stat[s, 5] = -1e9f; }
                int N = 60;
                for (int i = 0; i <= N; i++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, clip, clip.length * i / N);
                    AnimationMode.EndSampling();
                    for (int s = 0; s < 2; s++)
                    {
                        var up = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                        var lo = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                        var ft = anim.GetBoneTransform(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                        var toe = ft.childCount > 0 ? ft.GetChild(0) : null;
                        float h = go.transform.InverseTransformPoint(ft.position).y;
                        float pitch = 0;
                        if (toe != null) { var d = toe.position - ft.position; pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; }
                        float knee = Vector3.Angle(lo.position - up.position, ft.position - lo.position);
                        float[] v = { h, pitch, knee };
                        for (int k = 0; k < 3; k++) { stat[s, k * 2] = Mathf.Min(stat[s, k * 2], v[k]); stat[s, k * 2 + 1] = Mathf.Max(stat[s, k * 2 + 1], v[k]); }
                    }
                }
                sb.AppendLine($"{Path.GetFileName(dir)} / {clipName} ({clip.length:0.00}s):");
                string[] nm = { "высота стопы", "наклон стопы°", "сгиб колена°" };
                for (int k = 0; k < 3; k++)
                    sb.AppendLine($"   {nm[k],-15} Л {stat[0, k * 2],7:0.000}..{stat[0, k * 2 + 1],7:0.000}   П {stat[1, k * 2],7:0.000}..{stat[1, k * 2 + 1],7:0.000}");
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/arena-anim-check.log", sb.ToString());
        Debug.Log("[Arena] проверка анимаций записана в Logs/arena-anim-check.log\n" + sb);
    }
}
