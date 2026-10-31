// Builds the night stadium (pitch, boards, goals, stands, lights) and draws the match state:
// heroes, ball, particles, landing marker, curve aim dots.
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using static Arena.Sim.Field;

namespace Arena.Game
{
    public class WorldView
    {
        const float S = W.S;
        readonly Transform root;
        readonly List<HeroView> heroes = new List<HeroView>();
        Transform ball, ballShadow, landMark, ownerRing;
        Material ownerRingMat, landMat;
        TrailRenderer trail;
        ParticleSystem sparks, flames;
        readonly Transform[] dots = new Transform[15];
        readonly (float x, float y)[] dotBuf = new (float x, float y)[15];
        GameObject halo;
        Material haloMat;
        float punch;
        readonly Transform[] shocks = new Transform[4];
        readonly Material[] shockMats = new Material[4];
        readonly float[] shockLife = new float[4];
        int shockNext;

        public WorldView(Transform parent)
        {
            root = new GameObject("World").transform; root.SetParent(parent, false);
            BuildLights();
            BuildPitch();
            BuildBoardsAndGoals();
            BuildStands();
            BuildBall();
            BuildFx();
        }

        // ---------- static scene ----------
        void BuildLights()
        {
            var sun = new GameObject("Floodlight").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.type = LightType.Directional; sun.color = Mat.Hex("#b8c8ff"); sun.intensity = 0.75f; // ночь: холодный «лунный» свет, тёплые пятна дают прожекторы
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(58, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Mat.Hex("#34446e"); RenderSettings.ambientEquatorColor = Mat.Hex("#1a2340"); RenderSettings.ambientGroundColor = Mat.Hex("#0a0e1c"); // ночная синева
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Mat.Hex("#0a1026");
            RenderSettings.fogStartDistance = 22; RenderSettings.fogEndDistance = 50;

            // bloom: glowing fire and lava (emission brighter than 1) gets a soft halo
            var vgo = new GameObject("PostFx"); vgo.transform.SetParent(root, false);
            var vol = vgo.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true; vol.priority = 10;
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.threshold.Override(1.05f); bloom.scatter.Override(0.65f); bloom.intensity.Override(0.8f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.7f));
            // ночная атмосфера: затемнение по краям кадра, чуть больше контраста, прохладные тени и тёплый свет
            var vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vig.intensity.Override(0.3f); vig.smoothness.Override(0.55f); vig.color.Override(new Color(0.01f, 0.03f, 0.06f));
            var ca = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca.contrast.Override(14f); ca.saturation.Override(6f); ca.postExposure.Override(0.4f);
            var smh = profile.Add<UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.86f, 0.95f, 1.12f, -0.04f)); smh.highlights.Override(new Vector4(1.06f, 1.0f, 0.92f, 0f));
            vol.sharedProfile = profile;
        }

        UnityEngine.Rendering.Universal.Bloom bloom;
        public void SetBloom(float intensity) { if (bloom != null) bloom.intensity.value = intensity; }

        void BuildPitch()
        {
            // the pitch is painted into a texture, exactly like the prototype's canvas
            const int TW = 1600, TH = 1000; // texture width runs along the field length
            var px = new Color32[TW * TH];
            // газон: полосы покоса вдоль и (слабее) поперёк, пятна травы разного оттенка, вытоптано у ворот и в центре
            Color a = Mat.Hex("#3c9a3a"), b = Mat.Hex("#318a31"), worn = Mat.Hex("#6f8f45"); // сочный зелёный, как на референсе
            for (int i = 0; i < TW; i++)
            {
                float fy = (1 - i / (float)TW) * FH; // где это вдоль поля
                int stripe = (int)(i / (TW / 12f));
                for (int j = 0; j < TH; j++)
                {
                    float fx = (1 - j / (float)TH) * FW;
                    var c = stripe % 2 == 0 ? a : b;
                    if (((int)(j / (TH / 8f))) % 2 == 0) c *= 1.035f;                        // клетка покоса
                    float n = Mathf.PerlinNoise(i * 0.035f, j * 0.035f) * 0.6f + Mathf.PerlinNoise(i * 0.22f + 31, j * 0.22f + 17) * 0.4f;
                    c *= 0.9f + 0.18f * n;                                                    // неровный оттенок
                    float wear = 0;                                                           // вытоптанные места
                    { float dx = (fx - FW / 2) / 260, dy = Mathf.Min(fy, FH - fy) / 170; wear = Mathf.Exp(-(dx * dx + dy * dy)) * 0.55f; } // у ближайших ворот
                    { float dx = (fx - FW / 2) / 200, dy = (fy - FH / 2) / 200; wear = Mathf.Max(wear, Mathf.Exp(-(dx * dx + dy * dy)) * 0.25f); }
                    wear *= 0.6f + 0.8f * Mathf.PerlinNoise(i * 0.09f + 5, j * 0.09f + 9);
                    c = Color.Lerp(c, worn, Mathf.Clamp01(wear));
                    float edge = Mathf.Min(Mathf.Min(fx, FW - fx), Mathf.Min(fy, FH - fy));    // у бортов чуть темнее (тень от бортов)
                    c *= 0.88f + 0.12f * Mathf.Clamp01(edge / 60);
                    { float rx = (fx - FW / 2) / (FW / 2), ry = (fy - FH / 2) / (FH / 2); c *= 1.06f - 0.2f * Mathf.Clamp01((rx * rx + ry * ry) * 0.5f); } // середина поля светлее, к краям темнее
                    c.a = 1;
                    px[j * TW + i] = c;
                }
            }
            Color32 chalk = new Color32(230, 248, 240, 220);
            void Dot(float x, float y, float r, Color32 col)
            {
                float u = (1 - y / FH) * TW, v = (1 - x / FW) * TH, rr = r * TW / FH;
                for (int i = (int)(u - rr - 1); i <= u + rr + 1; i++)
                    for (int j = (int)(v - rr - 1); j <= v + rr + 1; j++)
                        if (i >= 0 && i < TW && j >= 0 && j < TH && (i - u) * (i - u) + (j - v) * (j - v) <= rr * rr) px[j * TW + i] = col;
            }
            void Line(float x0, float y0, float x1, float y1, float w, Color32 col)
            {
                float L = Mathf.Max(1, new Vector2(x1 - x0, y1 - y0).magnitude); int n = (int)(L / 2);
                for (int k = 0; k <= n; k++) { float t = k / (float)n; Dot(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, w, col); }
            }
            void Arc(float cx, float cy, float r, float a0, float a1, float w)
            {
                int n = (int)(r * Mathf.Abs(a1 - a0) / 2) + 8;
                for (int k = 0; k <= n; k++) { float t = a0 + (a1 - a0) * k / n; Dot(cx + Mathf.Cos(t) * r, cy + Mathf.Sin(t) * r, w, chalk); }
            }
            float lw = 2.2f;
            Line(2, 2, FW - 2, 2, lw, chalk); Line(2, FH - 2, FW - 2, FH - 2, lw, chalk);
            Line(2, 2, 2, FH - 2, lw, chalk); Line(FW - 2, 2, FW - 2, FH - 2, lw, chalk);
            Line(0, FH / 2, FW, FH / 2, lw, chalk);
            Arc(FW / 2, FH / 2, 160, 0, Mathf.PI * 2, lw); Dot(FW / 2, FH / 2, 5, chalk);
            float bw = 800, bh = 270;
            Line((FW - bw) / 2, 0, (FW - bw) / 2, bh, lw, chalk); Line((FW + bw) / 2, 0, (FW + bw) / 2, bh, lw, chalk); Line((FW - bw) / 2, bh, (FW + bw) / 2, bh, lw, chalk);
            Line((FW - bw) / 2, FH, (FW - bw) / 2, FH - bh, lw, chalk); Line((FW + bw) / 2, FH, (FW + bw) / 2, FH - bh, lw, chalk); Line((FW - bw) / 2, FH - bh, (FW + bw) / 2, FH - bh, lw, chalk);
            Arc(FW / 2, bh, 110, 0, Mathf.PI, lw); Arc(FW / 2, FH - bh, 110, Mathf.PI, Mathf.PI * 2, lw);
            Line(GX1, 0, GX2, 0, 4, Mat.Away); Line(GX1, FH, GX2, FH, 4, Mat.Home); // goal lines: whose goal it is

            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            tex.SetPixels32(px); tex.Apply(true);
            var mat = new Material(Mat.Solid(Color.white)); mat.SetTexture("_BaseMap", tex); mat.SetFloat("_Smoothness", 0.05f);
            var pitch = Shapes.Prim(PrimitiveType.Quad, root, Vector3.zero, new Vector3(FH * S, FW * S, 1), mat, "Pitch");
            pitch.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var gmat = new Material(Mat.Solid(Color.white, 0.05f)); gmat.SetTexture("_BaseMap", GravelTexture()); gmat.SetTextureScale("_BaseMap", new Vector2(35, 30));
            var ground = Shapes.Prim(PrimitiveType.Quad, root, new Vector3(0, -0.005f, 0), new Vector3(70, 60, 1), gmat, "Ground"); // тёмный гравий вокруг поля
            ground.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        GameObject Box(float x, float y, float w, float d, float h, Material m, string name)
        { // field-space box: centre (x,y), size w (field x) by d (field y), height h
            return Shapes.Prim(PrimitiveType.Cube, root, W.Pos(x, y, h / 2), new Vector3(d * S, h * S, w * S), m, name);
        }

        void BuildBoardsAndGoals()
        {
            const float BH = 16, BT = 10;
            var side = Mat.Solid(Mat.Hex("#16353a"));
            Box(-BT / 2, FH / 2, BT, FH + BT * 2, BH, side, "BoardL"); Box(FW + BT / 2, FH / 2, BT, FH + BT * 2, BH, side, "BoardR");
            float ew = GX1;
            Box(ew / 2, -BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#8a2c42")), "BoardTopA"); Box(FW - ew / 2, -BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#8a2c42")), "BoardTopB");
            Box(ew / 2, FH + BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#1d3f8a")), "BoardBotA"); Box(FW - ew / 2, FH + BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#1d3f8a")), "BoardBotB");
            Goal(0, -1, Mat.Away); Goal(FH, 1, Mat.Home);
        }

        void Goal(float y0, float dir, Color col)
        {
            var white = Mat.Solid(Color.Lerp(col, Color.white, 0.15f), 0.4f); // штанги и перекладина — в цвете команды
            foreach (float x in new[] { GX1, GX2 })
                Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(x, y0, GOALH / 2), new Vector3(POST * 2 * S, GOALH / 2 * S, POST * 2 * S), white, "Post");
            var bar = Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(FW / 2, y0, GOALH), new Vector3(POST * 2 * S, (GOAL + POST * 2) / 2 * S, POST * 2 * S), white, "Crossbar");
            bar.transform.rotation = Quaternion.Euler(90, 0, 0); // field x runs along world z
            // сетка: полупрозрачная ячейка ~8 см, плитка подгоняется под размер каждой стенки
            Material Net(float u, float v)
            {
                var m = Mat.Clear(new Color(Mathf.Lerp(col.r, 1, 0.5f), Mathf.Lerp(col.g, 1, 0.5f), Mathf.Lerp(col.b, 1, 0.5f), 0.75f));
                m.SetTexture("_BaseMap", NetTexture()); m.SetTextureScale("_BaseMap", new Vector2(u / 8, v / 8));
                return m;
            }
            float yb = y0 + dir * NET;
            Box(FW / 2, yb, GOAL, 2, GOALH, Net(GOAL, GOALH), "NetBack");
            Box(GX1, y0 + dir * NET / 2, 2, NET, GOALH, Net(NET, GOALH), "NetSideA"); Box(GX2, y0 + dir * NET / 2, 2, NET, GOALH, Net(NET, GOALH), "NetSideB");
            Box(FW / 2, y0 + dir * NET / 2, GOAL, NET, 2, Net(GOAL, NET), "NetTop").transform.position += new Vector3(0, (GOALH - 1) * S, 0);
            // лёгкая тень сетки на траве
            var tint = Mat.Clear(new Color(col.r, col.g, col.b, 0.12f));
            Box(FW / 2, y0 + dir * NET / 2, GOAL, NET, 0.4f, tint, "NetFloor");
        }

        StadiumView stadium; // трибуны со зрителями, прожекторы, реклама, табло — Game/StadiumView.cs
        static Texture2D gravelTex;
        static Texture2D GravelTexture()
        {   // тёмный сине-серый гравий
            if (gravelTex != null) return gravelTex;
            const int N = 128; gravelTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "gravel", wrapMode = TextureWrapMode.Repeat };
            var p = new Color32[N * N]; var r = new System.Random(3);
            for (int i = 0; i < p.Length; i++)
            {
                float v = 0.09f + 0.07f * (float)r.NextDouble(); if (r.NextDouble() < 0.08) v += 0.12f;
                p[i] = new Color(v * 0.85f, v * 0.9f, v * 1.15f, 1);
            }
            gravelTex.SetPixels32(p); gravelTex.Apply(true);
            return gravelTex;
        }

        static Texture2D netTex;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetNet() { netTex = null; gravelTex = null; }
        static Texture2D NetTexture()
        {   // одна ячейка сетки: светлые нити по краям, прозрачная середина (текстура повторяется)
            if (netTex != null) return netTex;
            const int N = 32; netTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "goalNet", wrapMode = TextureWrapMode.Repeat };
            var p = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int dx = Mathf.Min(x, N - 1 - x), dy = Mathf.Min(y, N - 1 - y);
                    float a = Mathf.Clamp01(1.6f - Mathf.Min(dx, dy) * 0.8f);
                    p[y * N + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            netTex.SetPixels32(p); netTex.Apply(true);
            return netTex;
        }

        void BuildStands() => stadium = new StadiumView(root);

        // ---------- ball and effects ----------
        void BuildBall()
        {
            var tex = new Texture2D(128, 64);
            var px = new Color32[128 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            int[,] patches = { { 16, 32, 8 }, { 48, 15, 7 }, { 48, 49, 7 }, { 80, 32, 8 }, { 112, 15, 7 }, { 112, 49, 7 }, { 64, 2, 7 }, { 64, 62, 7 } };
            for (int p = 0; p < patches.GetLength(0); p++)
                for (int i = 0; i < 128; i++) for (int j = 0; j < 64; j++)
                        if ((i - patches[p, 0]) * (i - patches[p, 0]) + (j - patches[p, 1]) * (j - patches[p, 1]) <= patches[p, 2] * patches[p, 2]) px[j * 128 + i] = new Color32(22, 48, 58, 255);
            tex.SetPixels32(px); tex.Apply();
            var mat = new Material(Mat.Solid(Color.white, 0.4f)); mat.SetTexture("_BaseMap", tex);
            ball = Shapes.Prim(PrimitiveType.Sphere, root, Vector3.zero, Vector3.one * BR * 2 * S, mat, "Ball").transform;
            trail = ball.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.12f; trail.startWidth = BR * 1.6f * S; trail.endWidth = 0; trail.minVertexDistance = 0.02f;
            trail.material = Mat.ParticleMat(true); trail.emitting = false;
            haloMat = Mat.Clear(new Color(1, 0.42f, 0.06f, 0.5f), true);
            halo = Shapes.Prim(PrimitiveType.Sphere, ball, Vector3.zero, Vector3.one * 1.8f, haloMat, "Halo");
            for (int i = 0; i < shocks.Length; i++)
            {
                shockMats[i] = Mat.Clear(new Color(1, 1, 1, 0), true);
                shocks[i] = Shapes.MeshObj(Shapes.Ring(0.72f, 1f, 48), root, shockMats[i], "Shockwave").transform;
                shocks[i].gameObject.SetActive(false);
            }
            halo.SetActive(false);
            ballShadow = Shapes.MeshObj(Shapes.Disc(BR * S), root, Mat.Clear(new Color(0, 0, 0, 0.38f)), "BallShadow").transform;
            ownerRingMat = Mat.Clear(Color.white);
            ownerRing = Shapes.MeshObj(Shapes.Ring((BR + 4) * S, (BR + 7) * S, 32), root, ownerRingMat, "OwnerRing").transform;
            landMat = Mat.Clear(new Color(1, 1, 1, 0.85f));
            landMark = Shapes.MeshObj(Shapes.Ring(15 * S, 21 * S, 36), root, landMat, "LandingMark").transform;
            Shapes.MeshObj(Shapes.Disc(4 * S, 12), landMark, landMat, "LandingDot");
            var dotMat = Mat.Clear(new Color(0.5f, 0.91f, 0.82f, 0.85f));
            for (int i = 0; i < dots.Length; i++) { dots[i] = Shapes.MeshObj(Shapes.Disc(4.5f * S, 10), root, dotMat, "AimDot").transform; dots[i].gameObject.SetActive(false); }
        }

        ParticleSystem MakeParticles(string name, bool additive, float gravity)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.maxParticles = 2000; main.gravityModifier = gravity; main.loop = true;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.2f));
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mat.ParticleMat(additive);
            ps.Play();
            return ps;
        }

        void BuildFx()
        {
            sparks = MakeParticles("Sparks", false, 0.35f);
            flames = MakeParticles("Flames", true, -0.15f);
            smoke = MakeSmoke();
            abilityFx = new AbilityFx(root);
        }

        AbilityFx abilityFx; // порталы Мига, перья Вихря, вода Багора, пыль рывка, волна сильного удара

        // ---------- ninja smoke (Тень vanishes / reappears) ----------
        ParticleSystem smoke;
        bool[] wasInvisible = new bool[0];

        static Texture2D cloudTex;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCloud() { cloudTex = null; }
        internal static Texture2D CloudTexture()
        {
            if (cloudTex == null) cloudTex = MakeCloudTexture();
            return cloudTex;
        }

        static Texture2D MakeCloudTexture()
        {   // a soft lumpy puff: radial falloff broken up by noise
            const int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "smokePuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - N / 2f + 0.5f) / (N / 2f), dy = (y - N / 2f + 0.5f) / (N / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.6f + Mathf.PerlinNoise(x * 0.27f + 7, y * 0.27f + 3) * 0.4f;
                    float a = Mathf.Clamp01((1 - d) * 1.6f) * Mathf.Clamp01(0.35f + n);
                    byte shade = (byte)(200 + 55 * n);
                    px[y * N + x] = new Color32(shade, shade, shade, (byte)(255 * a * a));
                }
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        ParticleSystem MakeSmoke()
        {
            var go = new GameObject("NinjaSmoke"); go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.loop = true;
            main.maxParticles = 600; main.gravityModifier = -0.04f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.15f; lim.dampen = 0.12f; // the puff bursts out, then hangs
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.82f, 0.95f), 0), new GradientColorKey(new Color(0.42f, 0.38f, 0.52f), 1) },
                      new[] { new GradientAlphaKey(0.95f, 0), new GradientAlphaKey(0.8f, 0.35f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.25f, 1f), new Keyframe(1, 1.5f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var m = Mat.ParticleMat(false); m.SetTexture("_BaseMap", CloudTexture()); r.sharedMaterial = m;
            r.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            return ps;
        }

        /// <summary>A ninja "poof": a ring of smoke bursting out at the feet and a column that hides the body.</summary>
        public void SmokePoof(float x, float y, float strength)
        {
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            Vector3 c = W.Pos(x, y, 0);
            int ring = (int)(22 * strength), column = (int)(14 * strength);
            for (int i = 0; i < ring; i++)
            {
                float a = (i + Random.value * 0.6f) / ring * Mathf.PI * 2, sp = Random.Range(1.2f, 2.6f) * strength;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                e.position = c + dir * 0.15f + Vector3.up * Random.Range(0.05f, 0.3f);
                e.velocity = dir * sp + Vector3.up * Random.Range(0.1f, 0.5f);
                e.startSize = Random.Range(0.35f, 0.6f) * strength; e.startLifetime = Random.Range(0.8f, 1.3f);
                e.rotation = Random.value * 360;
                smoke.Emit(e, 1);
            }
            for (int i = 0; i < column; i++)
            {
                float a = Random.value * Mathf.PI * 2;
                e.position = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(0, 0.25f) + Vector3.up * Random.Range(0.2f, 1.3f);
                e.velocity = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(0.2f, 0.7f) + Vector3.up * Random.Range(0.2f, 0.8f);
                e.startSize = Random.Range(0.5f, 0.85f) * strength; e.startLifetime = Random.Range(0.9f, 1.5f);
                e.rotation = Random.value * 360;
                smoke.Emit(e, 1);
            }
        }

        public void Burst(float x, float y, Color c, int n, float spd)
        {
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2, s = (80 + Random.value * 260) * spd * S;
                e.position = W.Pos(x, y, 12);
                e.velocity = new Vector3(Mathf.Cos(a) * s, (60 + Random.value * 160) * S, Mathf.Sin(a) * s);
                e.startColor = c; e.startSize = (5 + Random.value * 4) * S; e.startLifetime = 0.35f + Random.value * 0.45f;
                sparks.Emit(e, 1);
            }
        }

        /// <summary>Strong touch on the ball: a shock ring on the grass and a punch of the ball's size.</summary>
        public void Impact(float x, float y, float a)
        {
            punch = Mathf.Max(punch, Mathf.Clamp01(a));
            if (a < 0.3f) return;
            int i = shockNext++ % shocks.Length;
            shocks[i].position = W.Pos(x, y, 1.5f); shockLife[i] = 1; shocks[i].gameObject.SetActive(true);
            shocks[i].localScale = Vector3.one * 0.1f; shocks[i].GetComponent<Renderer>().sharedMaterial = shockMats[i];
            shockMats[i].SetColor("_BaseColor", a > 1.1f ? new Color(1, 0.6f, 0.2f, 0.9f) : new Color(1, 1, 1, 0.8f));
            shocks[i].name = "Shockwave " + a.ToString("0.0");
            shockStrength[i] = a;
        }
        readonly float[] shockStrength = new float[4];

        // ice fissures: a translucent ridge plus a row of jagged shards that rise as it grows
        readonly List<Transform> wallViews = new List<Transform>();
        readonly List<Transform[]> wallShards = new List<Transform[]>();
        Material wallMat, shardMat;
        const int SHARDS = 14;
        void SyncWalls(Match m, float t)
        {
            if (wallMat == null) { wallMat = Mat.Clear(new Color(0.7f, 0.93f, 1f, 0.45f)); shardMat = Mat.Solid(Mat.Hex("#d6f6ff"), 0.9f); }
            while (wallViews.Count < m.Walls.Count)
            {
                wallViews.Add(Shapes.Prim(PrimitiveType.Cube, root, Vector3.zero, Vector3.one, wallMat, "IceRidge").transform);
                var sh = new Transform[SHARDS];
                for (int k = 0; k < SHARDS; k++) sh[k] = Shapes.MeshObj(Shapes.Cone(1, 1, 5), root, shardMat, "IceShard").transform;
                wallShards.Add(sh);
            }
            for (int i = 0; i < wallViews.Count; i++)
            {
                bool on = i < m.Walls.Count; wallViews[i].gameObject.SetActive(on);
                foreach (var sh in wallShards[i]) sh.gameObject.SetActive(false);
                if (!on) continue;
                var w = m.Walls[i];
                float fade = Mathf.Clamp01(w.Life / 0.3f), len = Mathf.Max(1, w.Len);
                wallViews[i].position = W.Pos(w.X + w.Tx * len / 2, w.Y + w.Ty * len / 2, IceWall.HEIGHT * 0.35f * fade);
                wallViews[i].rotation = Quaternion.LookRotation(W.Dir(w.Tx, w.Ty), Vector3.up);
                wallViews[i].localScale = new Vector3(IceWall.THICK * S, IceWall.HEIGHT * 0.7f * fade * S, len * S);
                for (int k = 0; k < SHARDS; k++)
                {
                    float a = (k + 0.5f) / SHARDS * w.MaxLen;
                    if (a > w.Len) break;
                    float rise = Mathf.Clamp01((w.Len - a) / 120) * fade;
                    float h = IceWall.HEIGHT * (0.8f + 0.45f * Mathf.Abs(Mathf.Sin(k * 2.7f))) * rise;
                    float side = (k % 2 == 0 ? 1 : -1) * 6;
                    var sh = wallShards[i][k]; sh.gameObject.SetActive(rise > 0.02f);
                    sh.position = W.Pos(w.X + w.Tx * a - w.Ty * side, w.Y + w.Ty * a + w.Tx * side, 0);
                    sh.rotation = Quaternion.LookRotation(W.Dir(w.Tx, w.Ty), Vector3.up) * Quaternion.Euler(0, k * 37, (k % 2 == 0 ? 12 : -12));
                    sh.localScale = new Vector3(IceWall.THICK * 0.75f * S, Mathf.Max(0.001f, h * S), IceWall.THICK * 0.75f * S);
                }
            }
        }

        // ---------- per-frame sync ----------
        public void EnsureHeroes(Match m)
        {
            while (heroes.Count < m.Players.Count)
            {
                var p = m.Players[heroes.Count];
                heroes.Add(new HeroView(root, p.Team, "Hero" + heroes.Count));
            }
            for (int i = 0; i < heroes.Count; i++) heroes[i].SetVisible(i < m.Players.Count);
        }

        public void Rebuild(Match m)
        { // team colours are baked into a hero view, so rebuild when the line-up changes
            foreach (var h in heroes) Object.Destroy(h.Root);
            heroes.Clear();
            EnsureHeroes(m);
        }

        Quaternion ballRot = Quaternion.identity;

        public void Sync(Match m, float t, float dt, InputCommand localInput)
        {
            EnsureHeroes(m);
            SyncWalls(m, t);
            stadium.Sync(m, t, dt);
            for (int i = 0; i < m.Players.Count; i++) heroes[i].Sync(m, m.Players[i], t, m.Mode != GameMode.OneVsOne && m.Mode != GameMode.Training && m.Players[i] == m.Local);
            if (wasInvisible.Length != m.Players.Count) wasInvisible = new bool[m.Players.Count];
            for (int i = 0; i < m.Players.Count; i++)
            {   // Тень: a big puff when vanishing, a smaller one when reappearing
                var p = m.Players[i];
                if (p.Invisible != wasInvisible[i]) SmokePoof(p.X, p.Y, p.Invisible ? 1f : 0.6f);
                wasInvisible[i] = p.Invisible;
            }
            abilityFx.Sync(m, t, dt);

            var b = m.Ball;
            ball.position = W.Pos(b.X, b.Y, BR + b.Z);
            var v = new Vector3(-b.Vy, 0, -b.Vx) * S; float sp = v.magnitude;
            if (sp > 0.01f) { var axis = Vector3.Cross(Vector3.up, v / sp); ballRot = Quaternion.AngleAxis(sp * dt / (BR * S) * Mathf.Rad2Deg, axis) * ballRot; }
            ball.rotation = ballRot;
            bool hot = b.Fire > 0 || (b.Owner != null && b.Owner.FireT > 0);
            var ow = b.Owner;
            bool full = ow != null && ((ow.Charging && ow.Charge >= 0.99f) || (ow.Curving != 0 && ow.CurveCharge >= 0.99f) || (ow.Lobbing && ow.LobCharge >= 0.99f));
            halo.SetActive(hot || full);
            if (hot) { haloMat.SetColor("_BaseColor", new Color(1, 0.42f, 0.06f, 0.5f)); halo.transform.localScale = Vector3.one * (1.6f + Random.value * 0.5f); }
            else if (full) { haloMat.SetColor("_BaseColor", new Color(1, 1, 0.85f, 0.35f + 0.25f * Mathf.Sin(t * 30))); halo.transform.localScale = Vector3.one * (1.7f + 0.2f * Mathf.Sin(t * 30)); }
            // ball punch on impact
            punch = Mathf.Max(0, punch - dt * 4);
            ball.localScale = Vector3.one * BR * 2 * S * (1 + punch * 0.9f);
            for (int i = 0; i < shocks.Length; i++)
            {
                if (shockLife[i] <= 0) continue;
                shockLife[i] -= dt * 2.6f;
                if (shockLife[i] <= 0) { shocks[i].gameObject.SetActive(false); continue; }
                float k = 1 - shockLife[i];
                float ek = 1 - (1 - k) * (1 - k); // ease-out
                shocks[i].localScale = Vector3.one * (0.15f + ek * (0.6f + 1.1f * shockStrength[i]));
                var sc = shockMats[i].GetColor("_BaseColor"); sc.a = shockLife[i] * 0.85f; shockMats[i].SetColor("_BaseColor", sc);
            }
            if (b.Fire > 0)
                for (int i = 0; i < 2; i++)
                    flames.Emit(new ParticleSystem.EmitParams
                    {
                        position = W.Pos(b.X + (Random.value - 0.5f) * 12, b.Y + (Random.value - 0.5f) * 12, BR + b.Z), velocity = Vector3.up * 0.3f,
                        startColor = Color.Lerp(Mat.FireYellow, Mat.Hex("#ff3a10"), Random.value), startSize = 16 * S, startLifetime = 0.32f, applyShapeToPosition = false
                    }, 1);
            float speed = new Vector2(b.Vx, b.Vy).magnitude;
            trail.emitting = speed > 300 && b.Owner == null;
            trail.startColor = b.Spin != 0 ? new Color(0.5f, 0.91f, 0.82f, 0.7f) : new Color(0.9f, 0.97f, 0.94f, 0.35f);
            trail.endColor = new Color(trail.startColor.r, trail.startColor.g, trail.startColor.b, 0);

            ballShadow.position = W.Pos(b.X, b.Y, 0.7f);
            ballShadow.localScale = Vector3.one * (1 + b.Z / 260);
            ownerRing.gameObject.SetActive(b.Owner != null);
            if (b.Owner != null)
            {
                ownerRing.position = W.Pos(b.X, b.Y, 0.8f);
                var c = b.Owner.Team == Team.Home ? Mat.Home : Mat.Away; c.a = b.Lock > 0 ? 0.45f : 0.9f;
                ownerRingMat.SetColor("_BaseColor", c);
            }

            // landing marker: aiming a lob, or a ball already in the air
            var you = m.Local; bool show = false; float lx = 0, ly = 0; Color lc = Color.white;
            if (you.Lobbing)
            {
                var a = m.LobAim(you, localInput.MoveX, localInput.MoveY); float D = m.LobDist(you.LobCharge);
                lx = you.X + a.x * D; ly = you.Y + a.y * D; show = true; lc = m.CanLob(you) ? Mat.Hex("#c8ef7a") : Mat.Hex("#7fa59a");
            }
            else if (b.Z > LOW && b.Hook == null) { var L = m.LandingPoint(); lx = L.x; ly = L.y; show = true; }
            landMark.gameObject.SetActive(show);
            if (show)
            {
                landMark.position = W.Pos(Mathf.Clamp(lx, 0, FW), Mathf.Clamp(ly, -NET, FH + NET), 0.9f);
                landMark.localScale = Vector3.one * (1 + Mathf.Sin(t * 10) * 0.08f);
                lc.a = 0.85f; landMat.SetColor("_BaseColor", lc);
            }

            // curve aim preview
            int n = you.Curving != 0 ? m.CurvePath(you, localInput.MoveX, localInput.MoveY, you.CurveCharge, you.Curving, dotBuf) : 0;
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i].gameObject.SetActive(i < n);
                if (i < n) dots[i].position = W.Pos(dotBuf[i].x, dotBuf[i].y, 0.9f);
            }
        }

        public Vector3 BallPos => ball.position;

        /// <summary>Стереть след мяча (при переходе в повтор и обратно мяч «прыгает»).</summary>
        public void ClearTrails() { if (trail != null) trail.Clear(); }
    }
}
