// Стадион вокруг поля: ступенчатые трибуны со зрителями, мачты прожекторов с лучами,
// светящиеся рекламные борта и табло со счётом на дальнем борту.
// Только картинка: на игру не влияет. Зрители прыгают и снимают на телефоны, когда забивают гол.
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using static Arena.Sim.Field;

namespace Arena.Game
{
    public class StadiumView
    {
        const float S = W.S;
        const int ROWS = 5;            // рядов на каждой трибуне (невысокие, как на референсе)
        const float D0 = 70, DEP = 80; // отступ первого ряда от борта, глубина ряда (единицы поля, 1 = 1 см)
        const float RISE = 45, H0 = 85; // на сколько каждый ряд выше предыдущего; высота первого ряда (выше рекламы)
        readonly Transform root;

        public StadiumView(Transform parent)
        {
            root = new GameObject("Stadium").transform; root.SetParent(parent, false);
            BuildStands();
            BuildFloodlights();
            BuildAds();
            BuildNightLights();
            BuildHaze();
            BuildGlares();
        }

        // ---------- ночные огни ----------
        void BuildNightLights()
        {   // неоновая полоса по краю трибун и тёплые фонари по верху задних стенок — светятся и дают ореол
            var neon = new Material(Mat.Glow(new Color(0.25f, 1.3f, 2.2f))); neonMat = neon; // своя копия: мигает после гола
            var lamp = Mat.Glow(new Color(2.6f, 2.2f, 1.7f));
            var cube = new Vector3(1, 1, 1);
            foreach (var sd in Sides())
            {
                StandBox(sd, sd.Off + D0 + 1.5f, H0 + 1.5f, 3, 3, neon);
                float back = sd.Off + D0 + ROWS * DEP + 10, top = H0 + ROWS * RISE + 120;
                for (float a = 150; a < sd.Len - 100; a += 300)
                {
                    float fx = sd.X0 + sd.Ax * a + sd.Nx * (back - 12), fy = sd.Y0 + sd.Ay * a + sd.Ny * (back - 12);
                    var g = Shapes.Prim(PrimitiveType.Cube, root, W.Pos(fx, fy, top + 6), new Vector3(28 * S, 12 * S, 28 * S), lamp, "StandLamp");
                    g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        // пылинки, медленно плавающие в свете прожекторов над полем
        void BuildHaze()
        {
            var go = new GameObject("Haze"); go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0, 2.6f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.maxParticles = 400; main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6, 11);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.75f, 0.55f), new Color(0.8f, 0.9f, 1f, 0.35f));
            var em = ps.emission; em.enabled = true; em.rateOverTime = 32;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(FH * S + 4, 4.2f, FW * S + 3);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.12f; noise.frequency = 0.3f;
            var m = Mat.ParticleMat(true); m.SetTexture("_BaseMap", SoftDot()); ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
            ps.Play();
        }

        static Texture2D softDot;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { softDot = null; flatQuad = null; beamMesh = null; bulbTex = null; }
        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int N = 32; softDot = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "hazeDot", wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
                    p[y * N + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
            softDot.SetPixels32(p); softDot.Apply();
            return softDot;
        }

        // ---------- трибуны ----------
        // сторона: точка начала, направление вдоль, наружу (в координатах поля), длина, чьи болельщики
        struct Side { public float X0, Y0, Ax, Ay, Nx, Ny, Len, Off; public float HomeShare; }

        Side[] Sides()
        {
            float end = NET + 20; // трибуны по длинным сторонам кончаются у линии ворот — углы свободны для мачт и пальм
            return new[]
            {
                new Side { X0 = 0, Y0 = -end, Ax = 0, Ay = 1, Nx = -1, Ny = 0, Len = FH + 2 * end, Off = 0, HomeShare = 0.5f },   // дальняя (её видно сверху кадра)
                new Side { X0 = FW, Y0 = -end, Ax = 0, Ay = 1, Nx = 1, Ny = 0, Len = FH + 2 * end, Off = 0, HomeShare = 0.5f },   // ближняя (под камерой)
                new Side { X0 = 0, Y0 = 0, Ax = 1, Ay = 0, Nx = 0, Ny = -1, Len = FW, Off = NET, HomeShare = 0.25f },            // за воротами гостей
                new Side { X0 = 0, Y0 = FH, Ax = 1, Ay = 0, Nx = 0, Ny = 1, Len = FW, Off = NET, HomeShare = 0.75f },            // за воротами хозяев
            };
        }

        class Row { public Transform T; public float Phase; public float HomeShare; }
        readonly List<Row> rows = new List<Row>();
        readonly List<Vector3> heads = new List<Vector3>(); // где вспыхивают телефоны

        void BuildStands()
        {
            var crowdMat = new Material(Mat.ParticleShader); // неосвещённый, с цветом вершин: тысячи зрителей одним материалом
            crowdMat.SetFloat("_Surface", 0); crowdMat.SetColor("_BaseColor", Color.white);
            crowdMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            Color[] riser = { Mat.Hex("#151c33"), Mat.Hex("#19213b") }; // тёмно-синие, как ночной стадион на референсе
            Color[] seats = { Mat.Hex("#24325c"), Mat.Hex("#2a3b6b") };
            var rnd = new System.Random(7);
            foreach (var sd in Sides())
            {
                for (int r = 0; r < ROWS; r++)
                {
                    float o = sd.Off + D0 + r * DEP, h = H0 + r * RISE;
                    // ступень: бетон + полоса сидений
                    StandBox(sd, o + DEP / 2, h / 2, DEP, h, Mat.Solid(riser[r % 2], 0.05f));
                    StandBox(sd, o + DEP * 0.7f, h + 4, DEP * 0.35f, 8, Mat.Solid(seats[r % 2], 0.1f));
                    if (sd.Nx > 0 && r > 2) continue; // ближнюю трибуну камера почти не видит — там зрители только в первых рядах
                    // зрители этого ряда — один меш, чтобы ряд можно было «подбрасывать»
                    var go = new GameObject("CrowdRow"); go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = CrowdMesh(sd, o + DEP * 0.55f, h + 2, rnd, 1f - r * 0.065f); // дальние ряды темнее — прожекторы до них не достают
                    go.AddComponent<MeshRenderer>().sharedMaterial = crowdMat;
                    go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rows.Add(new Row { T = go.transform, Phase = (float)rnd.NextDouble() * 6.28f, HomeShare = sd.HomeShare });
                }
                // задняя стенка над последним рядом
                float back = sd.Off + D0 + ROWS * DEP;
                StandBox(sd, back + 10, (H0 + ROWS * RISE + 120) / 2, 20, H0 + ROWS * RISE + 120, Mat.Solid(Mat.Hex("#0e1428"), 0.05f));
            }
        }

        void StandBox(Side sd, float off, float hMid, float depth, float height, Material m)
        {   // коробка вдоль всей стороны на расстоянии off от линии поля
            float cx = sd.X0 + sd.Ax * sd.Len / 2 + sd.Nx * off, cy = sd.Y0 + sd.Ay * sd.Len / 2 + sd.Ny * off;
            float w = sd.Ax != 0 ? sd.Len : depth, d = sd.Ax != 0 ? depth : sd.Len; // размер по x и по y поля
            var g = Shapes.Prim(PrimitiveType.Cube, root, W.Pos(cx, cy, hMid), new Vector3(d * S, height * S, w * S), m, "Stand");
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static readonly Color[] Skin = { Mat.Hex("#f1c9a5"), Mat.Hex("#d9a47a"), Mat.Hex("#b07850"), Mat.Hex("#7a4e30"), Mat.Hex("#e8b892") };
        static readonly Color[] Hair = { Mat.Hex("#1c1410"), Mat.Hex("#3b2a1e"), Mat.Hex("#6b4a2a"), Mat.Hex("#c9a46a"), Mat.Hex("#2a2a2a") };
        static readonly Color[] Pants = { Mat.Hex("#26324a"), Mat.Hex("#1e1e24"), Mat.Hex("#3a3f46"), Mat.Hex("#4a3a2a") };
        static readonly Color[] Other = { Mat.Hex("#e8eef0"), Mat.Hex("#3d8a5a"), Mat.Hex("#6a4fa0"), Mat.Hex("#222a30"), Mat.Hex("#c94a3a") };
        // болельщики — в цветах своих команд (хозяева синие)
        static readonly Color FanHome = Mat.Home, FanAway = Mat.Away;

        /// <summary>Объёмные низкополигональные зрители: туловище, голова с волосами, руки, у части — шапка, поднятые руки, шарф.
        /// Освещение «запечено» в цвет вершин, материал неосвещённый — дёшево даже для тысяч фигурок.</summary>
        Mesh CrowdMesh(Side sd, float off, float baseH, System.Random rnd, float dim)
        {
            var mb = new MeshBuild { Dim = dim };
            Vector3 n = W.Dir(sd.Nx, sd.Ny).normalized;      // наружу
            Vector3 face = -n;                                // лицом к полю
            float R() => (float)rnd.NextDouble();
            for (float a = 30; a < sd.Len - 30; a += 50 + R() * 16)
            {
                if (R() < 0.1) continue; // пустое место
                float fx = sd.X0 + sd.Ax * a + sd.Nx * (off + (R() - 0.5f) * 12);
                float fy = sd.Y0 + sd.Ay * a + sd.Ny * (off + (R() - 0.5f) * 12);
                if (fx > -5 && fx < FW + 5 && fy > -NET - 5 && fy < FH + NET + 5) continue; // на поле зрителей не сажаем
                double pick = R();
                Color shirt = pick < sd.HomeShare * 0.88 ? FanHome : pick < 0.88 ? FanAway : Other[rnd.Next(Other.Length)];
                float lum = 0.8f + R() * 0.2f; shirt = new Color(shirt.r * lum, shirt.g * lum, shirt.b * lum);
                bool standing = R() < 0.35, cheer = R() < 0.22, scarf = cheer && R() < 0.5;
                float tall = 0.9f + R() * 0.2f, yaw = (R() - 0.5f) * 30;
                var rot = Quaternion.LookRotation(Quaternion.Euler(0, yaw, 0) * face, Vector3.up);
                var M = Matrix4x4.TRS(W.Pos(fx, fy, baseH), rot, Vector3.one * (S * tall)); // дальше всё в сантиметрах: x вправо, y вверх, z к полю
                float y0 = standing ? 0 : -38; // сидящий ниже стоящего
                if (standing) { mb.Box(M, new Vector3(-7, 22, 0), new Vector3(11, 44, 12), Pants[rnd.Next(Pants.Length)]); mb.Box(M, new Vector3(7, 22, 0), new Vector3(11, 44, 12), Pants[rnd.Next(Pants.Length)]); }
                float tb = y0 + 44;                                   // низ туловища
                mb.Prism(M, new Vector3(0, tb, 0), 15, 10, 19, 11, 48, shirt);        // туловище: шире в плечах
                Color skin = Skin[rnd.Next(Skin.Length)];
                var head = new Vector3(0, tb + 62, 1);
                mb.Sphere(M, head, 11, skin);
                if (R() < 0.3) mb.Sphere(M, head + new Vector3(0, 5, -1), 10.5f, shirt, true);           // шапка в цветах
                else mb.Sphere(M, head + new Vector3(0, 3, -2.5f), 11.2f, Hair[rnd.Next(Hair.Length)], true); // волосы
                for (int sdn = -1; sdn <= 1; sdn += 2)
                {
                    if (cheer) mb.Box(M, new Vector3(sdn * 21, tb + 70, 2), new Vector3(7, 40, 7), shirt, sdn * 12);   // руки вверх
                    else mb.Box(M, new Vector3(sdn * 22, tb + 26, 3), new Vector3(7, 40, 7), shirt, sdn * 6);          // руки вдоль тела
                }
                if (scarf) mb.Box(M, new Vector3(0, tb + 95, 3), new Vector3(52, 9, 2), pick < sd.HomeShare ? FanHome : FanAway); // шарф над головой
                heads.Add(M.MultiplyPoint3x4(head));
            }
            return mb.ToMesh();
        }

        /// <summary>Сборщик меша с запечённым светом: цвет вершины = цвет × (рассеянный + направленный свет от прожекторов).</summary>
        class MeshBuild
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Color32> c = new List<Color32>(); readonly List<int> t = new List<int>();
            static readonly Vector3 L = new Vector3(0.25f, 0.85f, -0.45f).normalized;
            public float Dim = 1;
            Color Lit(Color col, Vector3 nrm) { float k = (0.45f + 0.6f * Mathf.Max(0, Vector3.Dot(nrm, L))) * Dim; var r = col * k; r.a = 1; return r; }
            void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col)
            {
                var nrm = Vector3.Cross(b - a, cc - a).normalized; var lc = (Color32)Lit(col, nrm);
                int k = v.Count; v.Add(a); v.Add(b); v.Add(cc); c.Add(lc); c.Add(lc); c.Add(lc); t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            void QuadF(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) { Tri(a, b, cc, col); Tri(a, cc, d, col); }
            public void Box(Matrix4x4 M, Vector3 ctr, Vector3 size, Color col, float tiltZ = 0)
            {
                var R = Matrix4x4.TRS(ctr, Quaternion.Euler(0, 0, tiltZ), size * 0.5f); var MM = M * R;
                Vector3 P(float x, float y, float z) => MM.MultiplyPoint3x4(new Vector3(x, y, z));
                Vector3 a = P(-1, -1, -1), b = P(1, -1, -1), cc = P(1, 1, -1), d = P(-1, 1, -1), e = P(-1, -1, 1), f = P(1, -1, 1), g = P(1, 1, 1), h = P(-1, 1, 1);
                QuadF(e, f, g, h, col); QuadF(b, a, d, cc, col); QuadF(a, e, h, d, col); QuadF(f, b, cc, g, col); QuadF(h, g, cc, d, col); QuadF(a, b, f, e, col);
            }
            public void Prism(Matrix4x4 M, Vector3 bottom, float rx0, float rz0, float rx1, float rz1, float h, Color col)
            {   // восьмигранная призма, сужающаяся/расширяющаяся кверху
                const int N = 6; var lo = new Vector3[N]; var hi = new Vector3[N];
                for (int i = 0; i < N; i++)
                {
                    float an = i * Mathf.PI * 2 / N, cx = Mathf.Cos(an), sz = Mathf.Sin(an);
                    lo[i] = M.MultiplyPoint3x4(bottom + new Vector3(cx * rx0, 0, sz * rz0));
                    hi[i] = M.MultiplyPoint3x4(bottom + new Vector3(cx * rx1 * 0.92f, h, sz * rz1));
                }
                var top = M.MultiplyPoint3x4(bottom + new Vector3(0, h + 3, 0));
                for (int i = 0; i < N; i++) { int j = (i + 1) % N; QuadF(lo[j], lo[i], hi[i], hi[j], col); Tri(hi[j], hi[i], top, col); }
            }
            public void Sphere(Matrix4x4 M, Vector3 ctr, float r, Color col, bool topHalf = false)
            {   // низкополигональный шар (или верхняя половина — волосы, шапка)
                const int SEG = 6, RING = 4; int r0 = topHalf ? RING / 2 : 0;
                for (int i = r0; i < RING; i++)
                    for (int j = 0; j < SEG; j++)
                    {
                        Vector3 Pt(int ii, int jj) { float th = Mathf.PI * ii / RING, ph = Mathf.PI * 2 * jj / SEG; return M.MultiplyPoint3x4(ctr + new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * r); }
                        var a = Pt(i, j); var b = Pt(i, j + 1); var cc = Pt(i + 1, j + 1); var d = Pt(i + 1, j);
                        if (i == 0) Tri(a, d, cc, col); else if (i == RING - 1) Tri(a, cc, b, col); else QuadF(a, d, cc, b, col); // наружу
                    }
            }
            public Mesh ToMesh()
            {
                var m = new Mesh { name = "crowd", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds();
                return m;
            }
        }

        // ---------- прожекторы ----------
        readonly List<Material> lampMats = new List<Material>();
        readonly List<Vector3> lampPos = new List<Vector3>();
        readonly List<Transform> glares = new List<Transform>();
        readonly List<Transform> shadows = new List<Transform>(); Material shadowMat;

        static Mesh flatQuad;
        static Mesh FlatQuad()
        {   // квадрат -1..1 в плоскости XZ лицом вверх, с UV (у Shapes.Disc их нет, а мягкой текстуре они нужны)
            if (flatQuad != null) return flatQuad;
            flatQuad = new Mesh { name = "flatQuad" };
            flatQuad.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            flatQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            flatQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            flatQuad.RecalculateNormals(); flatQuad.RecalculateBounds();
            return flatQuad;
        }

        // ореол вокруг ламп (виден, когда камера у края поля) — плоскость, всегда повёрнутая к камере
        void BuildGlares()
        {
            var m = Mat.Clear(new Color(1.6f, 1.45f, 1.2f, 0.55f), true); m.SetTexture("_BaseMap", SoftDot());
            var quad = FlatQuad();
            foreach (var lp in lampPos)
            {
                var g = Shapes.MeshObj(quad, root, m, "FloodGlare").transform;
                g.position = lp; g.localScale = Vector3.one * 1.3f;
                glares.Add(g);
            }
        }

        // тени героев от каждого прожектора: четыре бледные вытянутые тени в разные стороны — как на настоящем ночном стадионе
        void SyncShadows(Match m)
        {
            if (shadowMat == null)
            {
                shadowMat = Mat.Clear(new Color(0, 0.02f, 0.04f, 0.13f)); shadowMat.SetTexture("_BaseMap", SoftDot());
            }
            int need = m.Players.Count * lampPos.Count;
            while (shadows.Count < need)
            {
                var t = Shapes.MeshObj(FlatQuad(), root, shadowMat, "FloodShadow").transform;
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadows.Add(t);
            }
            float hh = HeroModel.Tune.height;
            int k = 0;
            foreach (var p in m.Players)
                foreach (var lp in lampPos)
                {
                    var sh = shadows[k++];
                    bool on = !p.Invisible;
                    sh.gameObject.SetActive(on);
                    if (!on) continue;
                    var foot = W.Pos(p.X, p.Y, 0.5f);
                    var away = foot - lp; float horiz = new Vector2(away.x, away.z).magnitude; away.y = 0; away.Normalize();
                    float len = Mathf.Clamp(hh * horiz / Mathf.Max(1, lp.y) * 0.7f, 0.5f, 1.6f);
                    sh.position = foot + away * (len * 0.45f) + Vector3.up * 0.008f;
                    sh.rotation = Quaternion.LookRotation(away, Vector3.up);
                    sh.localScale = new Vector3(0.28f, 1, len * 0.55f);
                }
            for (; k < shadows.Count; k++) shadows[k].gameObject.SetActive(false);
        }
        // Мачты прожекторов прямо у углов поля (как на референсе): тонкий столб, панель из ламп,
        // мягкий луч (конус с прозрачностью, тающей к земле), тёплый свет слева и холодный справа.
        readonly System.Collections.Generic.List<Light> spots = new System.Collections.Generic.List<Light>();
        readonly System.Collections.Generic.List<Color> spotCols = new System.Collections.Generic.List<Color>();
        /// <summary>Вспышки прожекторов после гола: сила (0 — выкл), длительность, с.</summary>
        public static float GoalFlash = 1.6f, GoalFlashTime = 1.8f;
        readonly System.Collections.Generic.List<Material> pools = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<Color> poolCols = new System.Collections.Generic.List<Color>();
        Material neonMat;

        void BuildFloodlights()
        {
            const float H = 1400, C = 95; // высота мачты (выше — свет ложится шире и ровнее), отступ от угла поля
            var pole = Mat.Solid(Mat.Hex("#8a96a0"), 0.6f);
            var frame = Mat.Solid(Mat.Hex("#1b2328"), 0.4f);
            foreach (var (x, y) in new[] { (-C, -NET - C), (-C, FH + NET + C), (FW + C, -NET - C), (FW + C, FH + NET + C) })
            {
                bool warm = y > FH / 2; // слева в кадре — тёплый, справа — холодный
                Color lc = warm ? Mat.Hex("#ffd9a6") : Mat.Hex("#d2e4ff");
                var mast = Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(x, y, H / 2), new Vector3(14 * S, H / 2 * S, 14 * S), pole, "FloodMast");
                Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(x, y, 6), new Vector3(34 * S, 6 * S, 34 * S), frame, "FloodBase");
                float tx = x < FW / 2 ? FW * 0.42f : FW * 0.58f, ty = y < FH / 2 ? FH * 0.36f : FH * 0.64f; // целятся ближе к центру — пятна сливаются
                Vector3 lamp = W.Pos(x, y, H + 30), target = W.Pos(tx, ty, 0);
                lampPos.Add(lamp);
                var head = Shapes.Prim(PrimitiveType.Cube, root, lamp, new Vector3(150 * S, 110 * S, 22 * S), frame, "FloodHead");
                // мачта и голова прожектора не отбрасывают тень от «луны» (иначе на поле лежит тёмный квадрат)
                foreach (var r in new[] { mast.GetComponent<MeshRenderer>(), head.GetComponent<MeshRenderer>() }) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                head.transform.rotation = Quaternion.LookRotation(target - lamp);
                var lm = new Material(Mat.Glow(lc * 3.2f)); lm.SetTexture("_BaseMap", BulbTexture()); lampMats.Add(lm);
                var panelGo = Shapes.Prim(PrimitiveType.Quad, head.transform, new Vector3(0, 0, 0.51f), new Vector3(0.9f, 0.86f, 1), lm, "FloodPanel");
                panelGo.transform.localRotation = Quaternion.Euler(0, 180, 0); // лицом вперёд
                panelGo.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 d = target - lamp; float len = d.magnitude;
                // вместо геометрического луча (сверху он выглядит плоским листом) — мягкое световое пятно на газоне:
                // широкое тёплое/холодное сияние, плавно тающее к краям, вытянутое от мачты к центру поля
                Vector3 flat = new Vector3(d.x, 0, d.z).normalized;
                var poolM = PoolMat(lc, 0.075f); pools.Add(poolM); poolCols.Add(new Color(lc.r, lc.g, lc.b, 0.075f));
                var pool = Shapes.MeshObj(FlatQuad(), root, poolM, "LightPool").transform;
                pool.position = W.Pos(tx, ty, 0.3f) - flat * 1.2f; pool.rotation = Quaternion.LookRotation(flat, Vector3.up);
                pool.localScale = new Vector3(10f, 1, 13f);
                var core = Shapes.MeshObj(FlatQuad(), root, PoolMat(lc, 0.035f), "LightPoolCore").transform;   // яркая сердцевина ближе к мачте
                core.position = W.Pos(tx, ty, 0.35f) - flat * 5f; core.rotation = pool.rotation; core.localScale = new Vector3(5f, 1, 7f);
                // настоящий свет — пятно на газоне (без теней, чтобы не тормозило)
                var lgo = new GameObject("FloodSpot"); lgo.transform.SetParent(root, false);
                lgo.transform.position = lamp; lgo.transform.rotation = Quaternion.LookRotation(d);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Spot; l.color = lc; l.intensity = 3.2f; l.range = len * 2f; l.spotAngle = 100; l.innerSpotAngle = 10; // очень мягкий край
                l.shadows = LightShadows.None;
                spots.Add(l); spotCols.Add(lc);
                // пальмы у мачты
                Palm(x + (x < 0 ? -40 : 40), y + (y < 0 ? 30 : -30), 1f);
                Palm(x + (x < 0 ? -30 : 30), y + (y < 0 ? -45 : 45), 0.75f);
            }
        }

        Material PoolMat(Color c, float a)
        {   // аддитивное мягкое пятно (радиальный градиент)
            var m = Mat.Clear(new Color(c.r, c.g, c.b, a), true); m.SetTexture("_BaseMap", SoftDot());
            return m;
        }

        Material BeamMat(Color c)
        {   // аддитивный, с цветом вершин, видно с обеих сторон
            var m = Mat.ParticleMat(true); m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1)); m.SetFloat("_Cull", 0);
            return m;
        }

        static Mesh beamMesh;
        static Mesh BeamMesh()
        {   // конус вдоль +Z длиной 1, радиус основания 1; у лампы ярче, к земле и к краям прозрачнее
            if (beamMesh != null) return beamMesh;
            const int SEG = 32, LEN = 6;
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            for (int i = 0; i <= LEN; i++)
            {
                float z = i / (float)LEN;
                byte a = (byte)(255 * 0.16f * Mathf.Pow(1 - z, 1.6f) * (0.25f + 0.75f * Mathf.Clamp01(z * 6))); // тает к земле, у самой лампы не слепит
                for (int j = 0; j <= SEG; j++)
                {
                    float an = j * Mathf.PI * 2 / SEG;
                    v.Add(new Vector3(Mathf.Cos(an) * z, Mathf.Sin(an) * z, z)); c.Add(new Color32(255, 255, 255, a));
                }
            }
            for (int i = 0; i < LEN; i++)
                for (int j = 0; j < SEG; j++)
                {
                    int k = i * (SEG + 1) + j, k2 = k + SEG + 1;
                    t.Add(k); t.Add(k2); t.Add(k + 1); t.Add(k + 1); t.Add(k2); t.Add(k2 + 1);
                }
            beamMesh = new Mesh { name = "floodBeam" }; beamMesh.SetVertices(v); beamMesh.SetColors(c); beamMesh.SetTriangles(t, 0); beamMesh.RecalculateBounds();
            return beamMesh;
        }

        static Texture2D bulbTex;
        static Texture2D BulbTexture()
        {   // панель 4×3 круглых ламп на тёмной раме
            if (bulbTex != null) return bulbTex;
            const int Wd = 128, Ht = 96; bulbTex = new Texture2D(Wd, Ht, TextureFormat.RGBA32, true) { name = "floodBulbs", wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[Wd * Ht];
            for (int y = 0; y < Ht; y++)
                for (int x = 0; x < Wd; x++)
                {
                    float cx = (x + 0.5f) / Wd * 4, cy = (y + 0.5f) / Ht * 3;
                    float dx = cx - Mathf.Floor(cx) - 0.5f, dy = cy - Mathf.Floor(cy) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float k = Mathf.Clamp01((0.42f - d) * 14);
                    byte b = (byte)(255 * (0.06f + 0.94f * k));
                    p[y * Wd + x] = new Color32(b, b, b, 255);
                }
            bulbTex.SetPixels32(p); bulbTex.Apply(true);
            return bulbTex;
        }

        // низкополигональная пальма: изогнутый ствол из сегментов и веер листьев
        void Palm(float x, float y, float k)
        {
            var trunk = Mat.Solid(Mat.Hex("#5a4632"), 0.1f);
            var leaf = Mat.Solid(Mat.Hex("#1f5a3a"), 0.2f);
            Vector3 p = W.Pos(x, y, 0); float lean = 8f;
            for (int i = 0; i < 5; i++)
            {
                var seg = Shapes.Prim(PrimitiveType.Cylinder, root, p + new Vector3(0, 0.12f * k, 0), new Vector3((10 - i) * S * k, 0.12f * k, (10 - i) * S * k), trunk, "PalmTrunk");
                seg.transform.localRotation = Quaternion.Euler(lean * i * 0.6f, 0, 0);
                p += seg.transform.up * 0.24f * k;
            }
            for (int i = 0; i < 7; i++)
            {
                var f = Shapes.Prim(PrimitiveType.Cube, root, p, new Vector3(0.16f * k, 0.02f * k, 0.9f * k), leaf, "PalmLeaf");
                f.transform.rotation = Quaternion.Euler(0, i * 360f / 7, 0) * Quaternion.Euler(28, 0, 0);
                f.transform.position = p + f.transform.forward * 0.42f * k;
            }
        }

        // ---------- рекламные борта и табло ----------
        class Board { public TextMesh Text; public string Msg; public float Phase; }
        readonly List<Board> boards = new List<Board>();
        readonly List<Material> stripMats = new List<Material>();
        TextMesh scoreText;
        static readonly string[] Msgs = { "АРЕНА СТРАЙКЕРОВ", "ЖАР • БАГОР • МИГ", "ВИХРЬ • ТЕНЬ • СТЕНА", "ИГРАЙ С ДРУЗЬЯМИ" };
        Font font;

        void BuildAds()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var panel = Mat.Solid(Mat.Hex("#081216"), 0.6f);
            const float BOFF = 30, BH = 70, BT = 8; // за бортом поля, высота и толщина рекламного щита
            // дальний борт (его видно сверху кадра) — с табло посередине
            AdStrip(-BOFF, -NET - 20, -BOFF, FH + NET + 20, BH, BT, panel, true);
            AdStrip(FW + BOFF, -NET - 20, FW + BOFF, FH + NET + 20, BH, BT, panel, false);
            AdStrip(0, -NET - BOFF, FW, -NET - BOFF, BH, BT, panel, false);
            AdStrip(0, FH + NET + BOFF, FW, FH + NET + BOFF, BH, BT, panel, false);
        }

        void AdStrip(float x0, float y0, float x1, float y1, float bh, float bt, Material panel, bool scoreboard)
        {
            float len = new Vector2(x1 - x0, y1 - y0).magnitude, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            Vector3 along = W.Dir(x1 - x0, y1 - y0).normalized;
            Vector3 toField = W.Pos(FW / 2, FH / 2) - W.Pos(cx, cy); toField.y = 0; toField = Vector3.Project(toField, Vector3.Cross(Vector3.up, along)).normalized;
            var g = Shapes.Prim(PrimitiveType.Cube, root, W.Pos(cx, cy, bh / 2), new Vector3(bt * S, bh * S, len * S), panel, "AdBoard");
            g.transform.rotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.identity;
            g.transform.localScale = new Vector3(bt * S, bh * S, len * S);
            // светящиеся полосы сверху и снизу щита (меняют цвет)
            for (int k = 0; k < 2; k++)
            {
                var sm = new Material(Mat.Glow(new Color(1.6f, 1.6f, 1.6f))); stripMats.Add(sm);
                var s = Shapes.Prim(PrimitiveType.Cube, root, W.Pos(cx, cy, k == 0 ? bh - 3 : 4) + toField * (bt * 0.6f * S), new Vector3(1, 1, 1), sm, "AdStrip");
                s.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
                s.transform.localScale = new Vector3(1.5f * S, 4 * S, len * S);
            }
            // надписи вдоль щита, лицом к полю
            Quaternion faceRot = Quaternion.LookRotation(-toField, Vector3.up); // TextMesh читается, если смотреть на его -Z... сторону; разворачиваем к полю
            int count = Mathf.Max(1, Mathf.RoundToInt(len / 700));
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.5f) / count;
                if (scoreboard && Mathf.Abs(a - 0.5f) < 0.6f / count) continue; // середина дальнего борта — табло
                var pos = Vector3.Lerp(W.Pos(x0, y0, bh / 2), W.Pos(x1, y1, bh / 2), a) + toField * ((bt / 2 + 3) * S); // на 3 см впереди щита: иначе щит местами перекрывает текст
                var tm = MakeText(pos, faceRot, 0.07f, Color.white);
                boards.Add(new Board { Text = tm, Msg = Msgs[(i + boards.Count) % Msgs.Length], Phase = i * 0.7f });
            }
            if (scoreboard)
            {
                var screenPos = W.Pos(cx, cy, bh / 2) + toField * ((bt / 2 + 1) * S);
                var pos = screenPos + toField * (2 * S); // текст счёта — на 2 см впереди экрана табло
                var back = Shapes.Prim(PrimitiveType.Cube, root, screenPos - toField * (0.5f * S), Vector3.one, Mat.Solid(Mat.Hex("#02070a"), 0.8f), "ScoreScreen");
                back.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
                back.transform.localScale = new Vector3(1 * S, (bh - 10) * S, 520 * S);
                scoreText = MakeText(pos, faceRot, 0.085f, Color.white);
                scoreText.richText = true;
            }
        }

        TextMesh MakeText(Vector3 pos, Quaternion rot, float size, Color c)
        {
            var go = new GameObject("AdText"); go.transform.SetParent(root, false);
            go.transform.position = pos; go.transform.rotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.fontSize = 64; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = c; tm.fontStyle = FontStyle.Bold;
            go.GetComponent<MeshRenderer>().sharedMaterial = TextMat();
            return tm;
        }

        // текст с проверкой глубины: герои закрывают надписи (встроенный материал шрифта рисует их поверх всего)
        Material textMat;
        Material TextMat()
        {
            if (textMat != null) return textMat;
            var sh = Shader.Find("Arena/WorldText");
            if (sh == null) return textMat = font.material; // шейдера нет — как раньше
            textMat = new Material(sh) { name = "WorldText", renderQueue = 3000 };
            textMat.mainTexture = font.material.mainTexture;
            Font.textureRebuilt += f => { if (f == font && textMat != null) textMat.mainTexture = font.material.mainTexture; }; // атлас шрифта пересобрался
            return textMat;
        }

        // ---------- каждый кадр ----------
        int lastHome = -1, lastAway = -1;
        float goalT; Team goalTeam;

        public void Sync(Match m, float t, float dt)
        {
            // гол: кто забил
            if (lastHome >= 0 && (m.ScoreHome > lastHome || m.ScoreAway > lastAway))
            {
                goalTeam = m.ScoreHome > lastHome ? Team.Home : Team.Away; goalT = 4f;
                PhoneFlashes(90);
            }
            lastHome = m.ScoreHome; lastAway = m.ScoreAway;
            goalT = Mathf.Max(0, goalT - dt);
            float party = Mathf.Clamp01(goalT / 1.5f);

            // зрители: лёгкое покачивание, после гола — прыжки (болельщики забившей команды прыгают выше)
            foreach (var r in rows)
            {
                float share = goalTeam == Team.Home ? r.HomeShare : 1 - r.HomeShare;
                float idle = Mathf.Sin(t * 2.1f + r.Phase) * 0.006f;
                float jump = party * Mathf.Abs(Mathf.Sin(t * 9 + r.Phase)) * (0.05f + 0.12f * share);
                r.T.localPosition = new Vector3(0, idle + jump, 0);
            }
            if (Random.value < dt * 5) PhoneFlashes(1);

            // реклама: цвета перетекают между цветами команд; после гола — мигает «ГОЛ!»
            Color home = Mat.Home, away = Mat.Away, gc = goalTeam == Team.Home ? home : away;
            for (int i = 0; i < stripMats.Count; i++)
            {
                Color c = goalT > 0 ? gc * (Mathf.Sin(t * 20) > 0 ? 3f : 0.6f) : Color.Lerp(home, away, 0.5f + 0.5f * Mathf.Sin(t * 0.8f + i * 0.9f)) * 1.8f;
                c.a = 1; stripMats[i].SetColor("_BaseColor", c);
            }
            foreach (var b in boards)
            {
                if (goalT > 0) { b.Text.text = "ГОЛ!"; b.Text.color = Mathf.Sin(t * 20 + b.Phase) > 0 ? gc : Color.white; continue; }
                int k = (int)((t + b.Phase) / 4f) % Msgs.Length;
                b.Text.text = Msgs[(k + System.Array.IndexOf(Msgs, b.Msg)) % Msgs.Length];
                b.Text.color = Color.Lerp(Color.white, Color.Lerp(home, away, 0.5f + 0.5f * Mathf.Sin(t * 0.8f + b.Phase)), 0.35f);
            }
            if (scoreText != null)
            {
                string hc = ColorUtility.ToHtmlStringRGB(home), ac = ColorUtility.ToHtmlStringRGB(away);
                if (goalT > 0 && Mathf.Sin(t * 14) > 0) scoreText.text = "<color=#" + ColorUtility.ToHtmlStringRGB(gc) + ">ГОЛ!</color>";
                else if (m.Mode == GameMode.Training) scoreText.text = "ТРЕНИРОВКА   <color=#" + hc + ">" + m.ScoreHome + "</color>";
                else
                {
                    int sec = Mathf.Max(0, Mathf.CeilToInt(m.Clock));
                    scoreText.text = "<color=#" + hc + ">" + m.ScoreHome + "</color> : <color=#" + ac + ">" + m.ScoreAway + "</color>   " + (sec / 60) + ":" + (sec % 60).ToString("00");
                }
            }
            SyncShadows(m);
            var cam = Camera.main;
            if (cam != null) foreach (var g in glares) g.rotation = Quaternion.FromToRotation(Vector3.up, (cam.transform.position - g.position).normalized);
            // лампы прожекторов слегка мерцают; после гола — стробоскоп в цвет забившей команды, мачты по очереди
            float since = 4f - goalT, flash = GoalFlashTime > 0 ? Mathf.Clamp01(1 - since / GoalFlashTime) : 0;
            for (int i = 0; i < lampMats.Count; i++)
            {
                float pulse = flash * Mathf.Max(0, Mathf.Sin(t * 22 + i * 1.6f)) * GoalFlash;
                Color baseC = new Color(3f, 2.8f, 2.4f) * (0.97f + 0.03f * Mathf.PerlinNoise(t * 3, i));
                lampMats[i].SetColor("_BaseColor", Color.Lerp(baseC, gc * 4f, Mathf.Clamp01(pulse)) * (1 + pulse));
                if (i < spots.Count)
                {
                    spots[i].intensity = 3.2f * (1 + pulse * 3f);
                    spots[i].color = Color.Lerp(spotCols[i], gc, Mathf.Clamp01(pulse * 0.8f));
                }
                if (i < pools.Count)
                {   // световое пятно на газоне вспыхивает цветом команды — его видно в кадре всегда
                    var pc = Color.Lerp(poolCols[i], new Color(gc.r, gc.g, gc.b, 0.5f), Mathf.Clamp01(pulse));
                    pools[i].SetColor("_BaseColor", pc);
                }
            }
            if (neonMat != null)
            {   // неон по краю трибун: после гола мигает цветом команды
                float np = flash * GoalFlash * (Mathf.Sin(t * 16) > 0 ? 1 : 0.15f);
                var nc = Color.Lerp(new Color(0.25f, 1.3f, 2.2f), gc * 3.5f, Mathf.Clamp01(np)); nc.a = 1;
                neonMat.SetColor("_BaseColor", nc);
            }
        }

        // вспышки телефонов на трибунах
        ParticleSystem flashPs;
        void PhoneFlashes(int n)
        {
            if (heads.Count == 0) return;
            if (flashPs == null)
            {
                var go = new GameObject("PhoneFlashes"); go.transform.SetParent(root, false);
                flashPs = go.AddComponent<ParticleSystem>();
                flashPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = flashPs.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.maxParticles = 300; main.loop = true;
                var em = flashPs.emission; em.enabled = false; var sh = flashPs.shape; sh.enabled = false;
                var col = flashPs.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                                  new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                col.color = g;
                var mat = Mat.ParticleMat(true); mat.SetColor("_BaseColor", new Color(3, 3, 3, 1)); flashPs.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
                flashPs.Play();
            }
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                e.position = heads[Random.Range(0, heads.Count)] + Vector3.up * 0.06f; e.velocity = Vector3.zero;
                e.startSize = Random.Range(0.06f, 0.12f); e.startLifetime = Random.Range(0.08f, 0.2f); e.startColor = Color.white;
                flashPs.Emit(e, 1);
            }
        }
    }
}
