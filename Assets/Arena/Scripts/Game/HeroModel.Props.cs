// Предметы героев, которых нет в моделях Meshy (концепты делались с пустыми руками):
//   Багор — багор в правой руке (древко, острие и крюк), крепится к кости кисти и двигается с анимацией;
// Всё строится кодом, без файлов моделей. Ползунки — Inspector → Arena → Hero Art → «Предметы».
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Game
{
    public partial class HeroModel
    {
        // ---------- общие сетки ----------
        /// <summary>Трубка по ломаной с переменным радиусом; caps — закрыть торцы.</summary>
        public static Mesh Tube(IList<Vector3> path, IList<float> rad, int sides, bool caps, string name)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            int n = path.Count, R = sides + 1;
            Vector3 prevN = Vector3.zero;
            var fw = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 f = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]).normalized; fw[i] = f;
                Vector3 nr = i == 0 ? Vector3.Cross(f, Mathf.Abs(f.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                                    : Vector3.ProjectOnPlane(prevN, f).normalized; // без перекручивания
                prevN = nr;
                Vector3 bn = Vector3.Cross(f, nr);
                for (int s = 0; s <= sides; s++) { float a = s * Mathf.PI * 2 / sides; v.Add(path[i] + (nr * Mathf.Cos(a) + bn * Mathf.Sin(a)) * rad[i]); }
            }
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * R + s, b = a + R;
                    t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            if (caps)
                for (int e = 0; e < 2; e++)
                {
                    int ring = e == 0 ? 0 : n - 1, start = v.Count;
                    v.Add(path[ring]);
                    for (int s = 0; s <= sides; s++) v.Add(v[ring * R + s]);
                    for (int s = 0; s < sides; s++)
                    {
                        if (e == 1) { t.Add(start); t.Add(start + 1 + s); t.Add(start + 2 + s); }
                        else { t.Add(start); t.Add(start + 2 + s); t.Add(start + 1 + s); }
                    }
                }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Металл багра вдоль +Z от z0: втулка, острие вперёд и крюк, загнутый вниз-назад. Размеры — для героя ростом 1,35 м.</summary>
        public static Mesh HookMetalMesh(float z0)
        {
            var parts = new List<CombineInstance>();
            void Add(Mesh m) => parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
            Add(Tube(new[] { new Vector3(0, 0, z0 - 0.03f), new Vector3(0, 0, z0 + 0.015f) }, new[] { 0.015f, 0.015f }, 10, true, "ferrule"));
            Add(Tube(new[] { new Vector3(0, 0, z0), new Vector3(0, 0, z0 + 0.05f), new Vector3(0, 0, z0 + 0.10f) }, new[] { 0.009f, 0.007f, 0.0012f }, 8, true, "spike"));
            var path = new List<Vector3>(); var rad = new List<float>();
            const int N = 14; const float r = 0.042f;
            for (int i = 0; i <= N; i++)
            {
                float k = i / (float)N, a = k * 215 * Mathf.Deg2Rad;
                path.Add(new Vector3(0, -r + r * Mathf.Cos(a), z0 + 0.01f + r * Mathf.Sin(a)));
                rad.Add(Mathf.Lerp(0.0085f, 0.0015f, k * k));
            }
            Add(Tube(path, rad, 8, true, "hookCurl"));
            var mesh = new Mesh { name = "hookMetal" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            foreach (var p in parts) Object.Destroy(p.mesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        void BuildProps(Animator anim)
        {
            if (anim == null || !anim.isHuman) return;
            if (hero == Hero.Hook) BuildHook(anim);
        }

        // ---------- Багор ----------
        Transform hookPose, hookMetal, hookTip;
        bool hookOut;

        /// <summary>Откуда тянуть верёвку брошенного крюка (конец древка в руке), или null.</summary>
        public Transform RopeStart => hookTip;
        /// <summary>Крюк улетел: в руке остаётся только древко.</summary>
        public void SetHookOut(bool out_) => hookOut = out_;

        void BuildHook(Animator anim)
        {
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var fore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (hand == null || fore == null) return;
            float k = Tune.height / 1.35f;
            Vector3 fa = (hand.position - fore.position).normalized;            // предплечье → кисть (в позе покоя)
            Vector3 fwd = Go.transform.forward;
            Vector3 axis = Vector3.ProjectOnPlane(fwd, fa); axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : fwd; // древко поперёк кулака, вперёд
            Vector3 upv = Vector3.ProjectOnPlane(Vector3.up, axis); upv = upv.sqrMagnitude > 1e-4f ? upv.normalized : Vector3.up;

            var holder = new GameObject("bagor").transform;
            holder.SetPositionAndRotation(hand.position + fa * 0.07f * k, Quaternion.LookRotation(axis, upv));
            holder.localScale = Vector3.one * k;
            holder.SetParent(hand, true);
            hookPose = new GameObject("pose").transform; hookPose.SetParent(holder, false);

            var wood = Mat.Solid(Mat.Hex("#6e4b2a"), 0.25f);
            var grip = Mat.Solid(Mat.Hex("#3b2717"), 0.1f);
            var metal = Mat.Solid(Mat.Hex("#c7d2d8"), 0.8f);
            const float back = -0.14f, front = 0.40f;
            Shapes.MeshObj(Tube(new[] { new Vector3(0, 0, back), new Vector3(0, 0, front) }, new[] { 0.011f, 0.011f }, 10, true, "shaft"), hookPose, wood, "shaft");
            Shapes.MeshObj(Tube(new[] { new Vector3(0, 0, -0.06f), new Vector3(0, 0, 0.05f) }, new[] { 0.0135f, 0.0135f }, 10, true, "grip"), hookPose, grip, "grip");
            Shapes.MeshObj(Tube(new[] { new Vector3(0, 0, back - 0.012f), new Vector3(0, 0, back + 0.01f) }, new[] { 0.0125f, 0.0125f }, 10, true, "cap"), hookPose, metal, "cap");
            hookMetal = Shapes.MeshObj(HookMetalMesh(front), hookPose, metal, "hook").transform;
            hookTip = new GameObject("ropeStart").transform; hookTip.SetParent(hookPose, false); hookTip.localPosition = new Vector3(0, 0, front);
            ApplyHookTune();
        }

        void ApplyHookTune()
        {
            var T = Tune;
            hookPose.localRotation = Quaternion.Euler(-T.hookPitch, T.hookYaw, 0) * Quaternion.Euler(0, 0, T.hookRoll);
            hookPose.localScale = Vector3.one * T.hookScale;
            hookPose.localPosition = hookPose.localRotation * new Vector3(0, 0, -T.hookGrip * T.hookScale);
        }

        void TickProps(float dt)
        {
            if (hookPose != null)
            {
                ApplyHookTune();
                if (hookMetal.gameObject.activeSelf == hookOut) hookMetal.gameObject.SetActive(!hookOut);
            }
        }
    }
}
