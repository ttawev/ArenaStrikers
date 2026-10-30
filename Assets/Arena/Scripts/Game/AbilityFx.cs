// Эффекты способностей и движения, которые не влияют на игру — только на картинку.
// Всё определяется по изменению состояния симуляции между кадрами (как дым Тени), сама симуляция не трогается.
//   Миг    — портал на месте ухода и появления, молния между ними.
//   Вихрь  — воронка ветра и перья при закрученном ударе, вихрь вокруг летящего мяча, перья слетают на бегу.
//   Багор  — капли с крюка, всплеск, когда крюк возвращается или цепляет, мокрые следы и капли с тела.
//   Все    — пыль из-под ног на рывке, трава и воздушная волна при сильном ударе.
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using static Arena.Sim.Field;

namespace Arena.Game
{
    public class AbilityFx
    {
        const float S = W.S;
        readonly Transform root;
        readonly ParticleSystem glow, dust, feathers, water;

        // по игроку: что было в прошлом кадре
        float[] px = new float[0], py = new float[0], pTpCd = new float[0], pTpBack = new float[0], pDash = new float[0], stepAcc = new float[0], featherAcc = new float[0], dripAcc = new float[0];
        bool[] stepLeft = new bool[0];
        float prevSpin, prevBallSpeed, ballBx, ballBy, swirlAng, swirlFeatherAcc;
        bool first = true;
        readonly HashSet<Hook> prevHooks = new HashSet<Hook>(), curHooks = new HashSet<Hook>();
        readonly Dictionary<Hook, Vector2> hookLast = new Dictionary<Hook, Vector2>();
        readonly HashSet<Hook> hookCaught = new HashSet<Hook>();

        public AbilityFx(Transform parent)
        {
            root = new GameObject("AbilityFx").transform; root.SetParent(parent, false);
            glow = MakePs("FxGlow", true, DotTexture(), 0, 1200);
            dust = MakePs("FxDust", false, WorldView.CloudTexture(), -0.02f, 600);
            var lim = dust.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.2f; lim.dampen = 0.15f;
            feathers = MakePs("FxFeathers", false, FeatherTexture(), 0.05f, 300);
            var fl = feathers.limitVelocityOverLifetime; fl.enabled = true; fl.limit = 0.35f; fl.dampen = 0.08f; // перо парит, а не падает камнем
            var fr = feathers.rotationOverLifetime; fr.enabled = true; fr.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var fn = feathers.noise; fn.enabled = true; fn.strength = 0.25f; fn.frequency = 0.8f;
            water = MakePs("FxWater", false, DotTexture(), 0.9f, 800);
            BuildRings(); BuildBeams(); BuildPrints();
        }

        // ---------- частицы ----------
        ParticleSystem MakePs(string name, bool additive, Texture2D tex, float gravity, int max)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.loop = true;
            main.maxParticles = max; main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var m = Mat.ParticleMat(additive); m.SetTexture("_BaseMap", tex); r.sharedMaterial = m;
            ps.Play();
            return ps;
        }

        static Texture2D dotTex, featherTex;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { dotTex = null; featherTex = null; }
        static Texture2D DotTexture()
        {
            if (dotTex != null) return dotTex;
            const int N = 32; dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "fxDot", wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
                    p[y * N + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
            dotTex.SetPixels32(p); dotTex.Apply();
            return dotTex;
        }

        static Texture2D FeatherTexture()
        {   // перо: вытянутое опахало с белым стержнем; цвет задаётся цветом частицы
            if (featherTex != null) return featherTex;
            const int Wt = 32, Ht = 64; featherTex = new Texture2D(Wt, Ht, TextureFormat.RGBA32, true) { name = "fxFeather", wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[Wt * Ht];
            for (int y = 0; y < Ht; y++)
                for (int x = 0; x < Wt; x++)
                {
                    float v = y / (float)(Ht - 1), u = (x - Wt / 2f + 0.5f) / (Wt / 2f);
                    float half = Mathf.Sin(Mathf.Clamp01(v * 1.05f) * Mathf.PI) * (0.55f + 0.45f * v); // шире к верху, острый кончик
                    float a = Mathf.Clamp01((half - Mathf.Abs(u)) * 6) * (v > 0.04f ? 1 : 0);
                    float barbs = 0.8f + 0.2f * Mathf.Sin((v * 40 + Mathf.Abs(u) * 6)); // прожилки
                    bool shaft = Mathf.Abs(u) < 0.07f && v < 0.97f;
                    byte c = (byte)(255 * (shaft ? 1 : barbs));
                    p[y * Wt + x] = new Color32(c, c, c, (byte)(255 * (shaft ? 1 : a)));
                }
            featherTex.SetPixels32(p); featherTex.Apply(true);
            return featherTex;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life, float rot = 0)
        {
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = pos, velocity = vel, startColor = c, startSize = size, startLifetime = life, rotation = rot };
            ps.Emit(e, 1);
        }

        // ---------- кольца (порталы, воронки, волны) ----------
        class Ring { public Transform T; public Material M; public float Life, Dur, From, To; public Color C; public bool On; }
        readonly List<Ring> rings = new List<Ring>();
        Mesh ringMesh;
        void BuildRings()
        {
            ringMesh = Shapes.Ring(0.78f, 1f, 48);
            for (int i = 0; i < 16; i++)
            {
                var m = Mat.Clear(new Color(1, 1, 1, 0), true);
                var t = Shapes.MeshObj(ringMesh, root, m, "FxRing").transform; t.gameObject.SetActive(false);
                rings.Add(new Ring { T = t, M = m });
            }
        }
        int ringNext;
        /// <summary>Кольцо радиуса from -> to (метры) за dur секунд; normal — куда смотрит плоскость кольца.</summary>
        void SpawnRing(Vector3 pos, Vector3 normal, float from, float to, float dur, Color c)
        {
            var r = rings[ringNext++ % rings.Count];
            r.On = true; r.Life = 0; r.Dur = dur; r.From = from; r.To = to; r.C = c;
            r.T.position = pos; r.T.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 0 ? normal.normalized : Vector3.up);
            r.T.gameObject.SetActive(true);
            TickRing(r, 0);
        }
        void TickRing(Ring r, float dt)
        {
            r.Life += dt;
            float k = Mathf.Clamp01(r.Life / r.Dur);
            if (k >= 1) { r.On = false; r.T.gameObject.SetActive(false); return; }
            float e = 1 - (1 - k) * (1 - k);
            r.T.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Lerp(r.From, r.To, e));
            var c = r.C; c.a *= 1 - k; r.M.SetColor("_BaseColor", c);
        }

        // ---------- молния телепорта ----------
        class Beam { public LineRenderer L; public Vector3 A, B; public float Life; public Color C; public bool On; }
        readonly List<Beam> beams = new List<Beam>();
        const int BEAM_PTS = 12;
        void BuildBeams()
        {
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("FxBeam"); go.transform.SetParent(root, false);
                var l = go.AddComponent<LineRenderer>();
                l.positionCount = BEAM_PTS; l.useWorldSpace = true; l.material = Mat.ParticleMat(true);
                l.widthCurve = new AnimationCurve(new Keyframe(0, 0.02f), new Keyframe(0.5f, 0.07f), new Keyframe(1, 0.02f));
                l.numCapVertices = 2; l.enabled = false;
                beams.Add(new Beam { L = l });
            }
        }
        int beamNext;
        void SpawnBeam(Vector3 a, Vector3 b, Color c)
        {
            var bm = beams[beamNext++ % beams.Count];
            bm.A = a; bm.B = b; bm.Life = 0.32f; bm.C = c; bm.On = true; bm.L.enabled = true;
        }
        void TickBeam(Beam bm, float dt)
        {
            bm.Life -= dt;
            if (bm.Life <= 0) { bm.On = false; bm.L.enabled = false; return; }
            var side = Vector3.Cross(bm.B - bm.A, Vector3.up).normalized;
            for (int i = 0; i < BEAM_PTS; i++)
            {
                float t = i / (float)(BEAM_PTS - 1), j = (i == 0 || i == BEAM_PTS - 1) ? 0 : 1;
                var p = Vector3.Lerp(bm.A, bm.B, t) + (side * (Random.value - 0.5f) * 0.25f + Vector3.up * (Random.value - 0.5f) * 0.2f) * j;
                bm.L.SetPosition(i, p);
            }
            var c = bm.C; c.a = Mathf.Clamp01(bm.Life / 0.32f);
            bm.L.startColor = c; bm.L.endColor = c;
        }

        // ---------- мокрые следы ----------
        class Print { public Transform T; public Material M; public float Life; public bool On; }
        readonly List<Print> prints = new List<Print>();
        const float PRINT_LIFE = 3.5f;
        void BuildPrints()
        {
            var mesh = Shapes.Disc(1, 14);
            for (int i = 0; i < 48; i++)
            {
                var m = Mat.Clear(new Color(0.05f, 0.16f, 0.22f, 0));
                var t = Shapes.MeshObj(mesh, root, m, "WetPrint").transform; t.gameObject.SetActive(false);
                prints.Add(new Print { T = t, M = m });
            }
        }
        int printNext;
        void SpawnPrint(float x, float y, float fx, float fy)
        {
            var pr = prints[printNext++ % prints.Count];
            pr.On = true; pr.Life = PRINT_LIFE; pr.T.gameObject.SetActive(true);
            pr.T.position = W.Pos(x, y, 0.6f);
            pr.T.rotation = W.Yaw(fx, fy);
            pr.T.localScale = new Vector3(6 * S, 1, 10 * S); // вытянут вдоль шага
        }

        // ---------- каждый кадр ----------
        void Resize(int n)
        {
            if (px.Length == n) return;
            px = new float[n]; py = new float[n]; pTpCd = new float[n]; pTpBack = new float[n]; pDash = new float[n];
            stepAcc = new float[n]; featherAcc = new float[n]; dripAcc = new float[n]; stepLeft = new bool[n];
            first = true;
        }

        static readonly Color[] FeatherCols = { Mat.Hex("#2f9e4a"), Mat.Hex("#1f7a3c"), Mat.Hex("#2b6fd6"), Mat.Hex("#f2c230"), Mat.Hex("#3fb8c9") };
        static readonly Color WaterCol = new Color(0.62f, 0.85f, 1f, 0.9f), DustCol = new Color(0.62f, 0.66f, 0.55f, 0.75f);

        public void Sync(Match m, float t, float dt)
        {
            int n = m.Players.Count;
            Resize(n);
            if (dt <= 0) return;
            foreach (var r in rings) if (r.On) TickRing(r, dt);
            foreach (var bm in beams) if (bm.On) TickBeam(bm, dt);
            foreach (var pr in prints)
            {
                if (!pr.On) continue;
                pr.Life -= dt;
                if (pr.Life <= 0) { pr.On = false; pr.T.gameObject.SetActive(false); continue; }
                pr.M.SetColor("_BaseColor", new Color(0.05f, 0.16f, 0.22f, 0.5f * Mathf.Clamp01(pr.Life / PRINT_LIFE * 1.5f)));
            }

            for (int i = 0; i < n; i++)
            {
                var p = m.Players[i];
                if (!first) PlayerFx(m, p, i, dt);
                px[i] = p.X; py[i] = p.Y; pTpCd[i] = p.TpCd; pTpBack[i] = p.TpBackT; pDash[i] = p.DashT;
            }
            BallFx(m, dt);
            HookFx(m, dt);
            first = false;
        }

        void PlayerFx(Match m, SimPlayer p, int i, float dt)
        {
            float jump = new Vector2(p.X - px[i], p.Y - py[i]).magnitude;
            bool hidden = p.Invisible;
            Color team = p.Team == Team.Home ? Mat.Home : Mat.Away;

            // Миг: телепорт — кулдаун только что взведён, или окно возврата закрылось прыжком назад
            if (p.Hero == Hero.Teleport && jump > 60 && jump < m.T.tpDist + 200 && (p.TpCd > pTpCd[i] + 0.5f || (pTpBack[i] > 0 && p.TpBackT <= 0)))
                Teleport(px[i], py[i], p.X, p.Y, team);

            // рывок: облако пыли на старте и шлейф, пока длится
            if (!hidden && p.DashT > 0)
            {
                bool start = pDash[i] <= 0;
                Vector3 bw = W.Dir(-p.Vx, -p.Vy).normalized;
                if (start)
                {
                    for (int k = 0; k < 10; k++)
                    {
                        float a = Random.value * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        Emit(dust, W.Pos(p.X, p.Y, 6) + d * 0.1f, d * Random.Range(0.4f, 1f) + bw * 0.8f + Vector3.up * Random.Range(0.1f, 0.4f),
                             DustCol, Random.Range(0.25f, 0.45f), Random.Range(0.5f, 0.8f), Random.value * 360);
                    }
                    SpawnRing(W.Pos(p.X, p.Y, 1.2f), Vector3.up, 0.15f, 0.7f, 0.35f, new Color(1, 1, 1, 0.35f));
                }
                for (int k = 0; k < 2; k++)
                    Emit(dust, W.Pos(p.X + (Random.value - 0.5f) * 16, p.Y + (Random.value - 0.5f) * 16, 4), bw * Random.Range(0.3f, 0.7f) + Vector3.up * Random.Range(0.1f, 0.3f),
                         DustCol, Random.Range(0.18f, 0.32f), Random.Range(0.35f, 0.6f), Random.value * 360);
            }

            float spd = new Vector2(p.Vx, p.Vy).magnitude;
            // Вихрь: на бегу с крыльев слетают перья
            if (p.Hero == Hero.Curve && !hidden && spd > 120)
            {
                featherAcc[i] += dt * 2.2f * Mathf.Clamp01(spd / 320);
                while (featherAcc[i] >= 1)
                {
                    featherAcc[i] -= 1;
                    Vector3 bw = W.Dir(-p.Fx, -p.Fy);
                    var pos = W.Pos(p.X - p.Fx * 14 + (Random.value - 0.5f) * 30, p.Y - p.Fy * 14 + (Random.value - 0.5f) * 30, Random.Range(60, 130));
                    Emit(feathers, pos, bw * Random.Range(0.2f, 0.6f) + Vector3.up * Random.Range(0, 0.2f), FeatherCols[Random.Range(0, FeatherCols.Length)],
                         Random.Range(0.07f, 0.12f), Random.Range(1.4f, 2.2f), Random.value * 360);
                }
            }

            // Багор: мокрые следы и капли с одежды
            if (p.Hero == Hero.Hook && !hidden)
            {
                if (jump < 60) stepAcc[i] += jump;
                if (stepAcc[i] >= 34)
                {
                    stepAcc[i] = 0; stepLeft[i] = !stepLeft[i];
                    float fx = p.Vx, fy = p.Vy; float l = Mathf.Max(1, Mathf.Sqrt(fx * fx + fy * fy)); fx /= l; fy /= l;
                    float side = stepLeft[i] ? 7 : -7;
                    SpawnPrint(p.X - fy * side, p.Y + fx * side, fx, fy);
                }
                dripAcc[i] += dt * 4;
                while (dripAcc[i] >= 1)
                {
                    dripAcc[i] -= 1;
                    Emit(water, W.Pos(p.X + (Random.value - 0.5f) * 30, p.Y + (Random.value - 0.5f) * 30, Random.Range(40, 120)), Vector3.zero, WaterCol, Random.Range(0.025f, 0.04f), 0.6f);
                }
            }
        }

        void Teleport(float x0, float y0, float x1, float y1, Color team)
        {
            Color blink = Mat.Blink;
            // уход: кольцо схлопывается, столб искр уходит вверх
            SpawnRing(W.Pos(x0, y0, 1.5f), Vector3.up, 0.9f, 0.05f, 0.35f, new Color(blink.r, blink.g, blink.b, 0.9f));
            SpawnRing(W.Pos(x0, y0, 60), Vector3.up, 0.5f, 0.02f, 0.3f, new Color(blink.r, blink.g, blink.b, 0.6f));
            for (int k = 0; k < 26; k++)
            {
                float a = Random.value * Mathf.PI * 2, r = Random.Range(0, 0.3f);
                var off = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                Emit(glow, W.Pos(x0, y0, Random.Range(0, 140)) + off, Vector3.up * Random.Range(1.5f, 3.5f) - off * 0.5f, blink, Random.Range(0.06f, 0.12f), Random.Range(0.35f, 0.6f));
            }
            // появление: кольцо распахивается, искры разлетаются
            SpawnRing(W.Pos(x1, y1, 1.5f), Vector3.up, 0.05f, 1.1f, 0.45f, new Color(team.r, team.g, team.b, 0.9f));
            SpawnRing(W.Pos(x1, y1, 1.6f), Vector3.up, 0.05f, 0.75f, 0.3f, new Color(blink.r, blink.g, blink.b, 0.9f));
            for (int k = 0; k < 30; k++)
            {
                float a = Random.value * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Emit(glow, W.Pos(x1, y1, Random.Range(10, 120)), d * Random.Range(1f, 2.8f) + Vector3.up * Random.Range(0, 1.2f), k % 2 == 0 ? blink : team, Random.Range(0.05f, 0.1f), Random.Range(0.3f, 0.55f));
            }
            SpawnBeam(W.Pos(x0, y0, 90), W.Pos(x1, y1, 90), new Color(blink.r, blink.g, blink.b, 1));
        }

        void BallFx(Match m, float dt)
        {
            var b = m.Ball;
            float speed = new Vector2(b.Vx, b.Vy).magnitude;
            bool reset = !first && new Vector2(b.X - ballBx, b.Y - ballBy).magnitude > 300; // разыгрывание с центра — не удар
            Vector3 bp = W.Pos(b.X, b.Y, BR + b.Z);
            Vector3 dir = W.Dir(b.Vx, b.Vy); dir = dir.sqrMagnitude > 0 ? dir.normalized : Vector3.forward;

            if (!first && !reset)
            {
                // сильный удар: трава и пыль из-под мяча, вертикальная воздушная волна
                float strong = m.T.kick * 1.3f; // сильный удар: заметно сильнее обычного паса
                if (b.Owner == null && speed - prevBallSpeed > 900 && speed > strong && b.Fire <= 0)
                {
                    float k = Mathf.Clamp01((speed - strong) / (m.T.kick * m.T.charge - strong + 1));
                    SpawnRing(bp, dir, 0.12f, 0.55f + 0.4f * k, 0.25f, new Color(1, 1, 1, 0.55f));
                    for (int i = 0; i < 12 + (int)(10 * k); i++)
                    {
                        var side = Vector3.Cross(dir, Vector3.up) * Random.Range(-1f, 1f);
                        Emit(dust, W.Pos(b.X, b.Y, 3), (-dir * Random.Range(0.2f, 0.8f) + side * 0.6f + Vector3.up * Random.Range(0.4f, 1.2f)), new Color(0.35f, 0.55f, 0.36f, 0.8f),
                             Random.Range(0.06f, 0.14f), Random.Range(0.4f, 0.7f), Random.value * 360);
                    }
                }
                // закрученный удар Вихря: воронка ветра и сноп перьев
                if (prevSpin == 0 && b.Spin != 0)
                {
                    bool vihr = b.Last != null && b.Last.Hero == Hero.Curve;
                    Color wind = Mat.Spin;
                    SpawnRing(bp, dir, 0.1f, 0.7f, 0.3f, new Color(wind.r, wind.g, wind.b, 0.8f));
                    SpawnRing(bp - dir * 0.25f, dir, 0.1f, 1.0f, 0.4f, new Color(wind.r, wind.g, wind.b, 0.45f));
                    for (int i = 0; i < 28; i++)
                    {   // спираль: частицы уходят по касательной вокруг направления удара
                        float a = i / 28f * Mathf.PI * 4;
                        var up = Vector3.up; var side = Vector3.Cross(dir, up).normalized;
                        var radial = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        var tang = Vector3.Cross(dir, radial) * Mathf.Sign(b.Spin);
                        Emit(glow, bp + radial * 0.15f - dir * (i / 28f) * 0.6f, radial * 0.8f + tang * 2.2f - dir * 0.4f, wind, Random.Range(0.05f, 0.09f), Random.Range(0.3f, 0.5f));
                    }
                    if (vihr)
                        for (int i = 0; i < 10; i++)
                            Emit(feathers, bp + Random.insideUnitSphere * 0.2f, Random.insideUnitSphere * 1.6f + Vector3.up * 0.6f, FeatherCols[Random.Range(0, FeatherCols.Length)],
                                 Random.Range(0.08f, 0.13f), Random.Range(1.2f, 2f), Random.value * 360);
                }
            }
            // закрученный мяч в полёте: вокруг вьётся вихрь, у мяча Вихря изредка срываются перья
            if (b.Spin != 0 && b.Owner == null && speed > 250)
            {
                swirlAng += dt * 28 * Mathf.Sign(b.Spin);
                var side = Vector3.Cross(dir, Vector3.up).normalized;
                for (int k = 0; k < 2; k++)
                {
                    float a = swirlAng + k * Mathf.PI;
                    var radial = side * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                    Emit(glow, bp + radial * (BR * 1.6f * S), radial * 0.15f, new Color(Mat.Spin.r, Mat.Spin.g, Mat.Spin.b, 0.8f), 0.05f, 0.28f);
                }
                if (b.Last != null && b.Last.Hero == Hero.Curve)
                {
                    swirlFeatherAcc += dt * 6;
                    while (swirlFeatherAcc >= 1)
                    {
                        swirlFeatherAcc -= 1;
                        Emit(feathers, bp, Random.insideUnitSphere * 0.5f, FeatherCols[Random.Range(0, FeatherCols.Length)], Random.Range(0.06f, 0.1f), Random.Range(1f, 1.6f), Random.value * 360);
                    }
                }
            }
            prevSpin = b.Spin; prevBallSpeed = b.Owner != null ? 0 : speed; ballBx = b.X; ballBy = b.Y;
        }

        void HookFx(Match m, float dt)
        {
            curHooks.Clear();
            foreach (var h in m.Hooks)
            {
                curHooks.Add(h);
                var pos = W.Pos(h.X, h.Y, 30);
                // с наконечника капает вода
                if (Random.value < dt * 30)
                    Emit(water, pos + Random.insideUnitSphere * 0.04f, new Vector3(0, -0.2f, 0), WaterCol, Random.Range(0.025f, 0.045f), 0.5f);
                // зацепил мяч или игрока — всплеск
                if ((h.HasBall || h.Victim != null) && hookCaught.Add(h)) Splash(h.X, h.Y, 30, 1f);
                hookLast[h] = new Vector2(h.X, h.Y);
            }
            foreach (var h in prevHooks)
                if (!curHooks.Contains(h))
                {   // крюк вернулся к хозяину — небольшой всплеск у рук
                    if (h.Owner != null) Splash(h.Owner.X + h.Owner.Fx * R, h.Owner.Y + h.Owner.Fy * R, 30, 0.6f);
                    hookLast.Remove(h); hookCaught.Remove(h);
                }
            prevHooks.Clear(); foreach (var h in curHooks) prevHooks.Add(h);
        }

        void Splash(float x, float y, float h, float k)
        {
            var c = W.Pos(x, y, h);
            for (int i = 0; i < (int)(22 * k); i++)
            {
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) + 0.4f;
                Emit(water, c, d * Random.Range(1.2f, 2.6f) * k, WaterCol, Random.Range(0.03f, 0.06f), Random.Range(0.4f, 0.7f));
            }
            SpawnRing(W.Pos(x, y, 1.2f), Vector3.up, 0.05f, 0.5f * k + 0.2f, 0.4f, new Color(0.7f, 0.9f, 1f, 0.6f));
        }
    }
}
