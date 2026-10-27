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
            sun.type = LightType.Directional; sun.color = Mat.Hex("#fff1d8"); sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(58, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Mat.Hex("#4a6a78"); RenderSettings.ambientEquatorColor = Mat.Hex("#24443f"); RenderSettings.ambientGroundColor = Mat.Hex("#0b2a24");
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Mat.Night;
            RenderSettings.fogStartDistance = 30; RenderSettings.fogEndDistance = 60;
        }

        void BuildPitch()
        {
            // the pitch is painted into a texture, exactly like the prototype's canvas
            const int TW = 1200, TH = 750; // texture width runs along the field length
            var px = new Color32[TW * TH];
            Color32 a = Mat.Hex("#1c5d4a"), b = Mat.Hex("#174f40");
            for (int i = 0; i < TW; i++)
            {
                int stripe = (int)(i / (TW / 12f));
                var c = stripe % 2 == 0 ? a : b;
                for (int j = 0; j < TH; j++) px[j * TW + i] = c;
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
            var ground = Shapes.Prim(PrimitiveType.Quad, root, new Vector3(0, -0.005f, 0), new Vector3(70, 60, 1), Mat.Solid(Mat.Hex("#0c2422")), "Ground");
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
            Box(ew / 2, FH + BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#8a6316")), "BoardBotA"); Box(FW - ew / 2, FH + BT / 2, ew, BT, BH, Mat.Solid(Mat.Hex("#8a6316")), "BoardBotB");
            Goal(0, -1, Mat.Away); Goal(FH, 1, Mat.Home);
        }

        void Goal(float y0, float dir, Color col)
        {
            var white = Mat.Solid(Mat.Hex("#f1f7f4"), 0.4f);
            foreach (float x in new[] { GX1, GX2 })
                Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(x, y0, GOALH / 2), new Vector3(POST * 2 * S, GOALH / 2 * S, POST * 2 * S), white, "Post");
            var bar = Shapes.Prim(PrimitiveType.Cylinder, root, W.Pos(FW / 2, y0, GOALH), new Vector3(POST * 2 * S, (GOAL + POST * 2) / 2 * S, POST * 2 * S), white, "Crossbar");
            bar.transform.rotation = Quaternion.Euler(90, 0, 0); // field x runs along world z
            var net = Mat.Clear(new Color(col.r, col.g, col.b, 0.28f));
            float yb = y0 + dir * NET;
            Box(FW / 2, yb, GOAL, 2, GOALH, net, "NetBack");
            Box(GX1, y0 + dir * NET / 2, 2, NET, GOALH, net, "NetSideA"); Box(GX2, y0 + dir * NET / 2, 2, NET, GOALH, net, "NetSideB");
            Box(FW / 2, y0 + dir * NET / 2, GOAL, NET, 2, net, "NetTop").transform.position += new Vector3(0, (GOALH - 1) * S, 0);
        }

        void BuildStands()
        {
            string[] cols = { "#12302f", "#153836", "#19403d", "#1c4744" };
            for (int i = 0; i < 4; i++)
            {
                float gap = 40 + i * 46, h = 22 + i * 26, dep = 46; var m = Mat.Solid(Mat.Hex(cols[i]));
                Box(-gap - dep / 2, FH / 2, dep, FH + 2 * (gap + dep), h, m, "Stand");
                Box(FW + gap + dep / 2, FH / 2, dep, FH + 2 * (gap + dep), h, m, "Stand");
                Box(FW / 2, -NET - gap - dep / 2, FW + 2 * gap, dep, h, m, "Stand");
                Box(FW / 2, FH + NET + gap + dep / 2, FW + 2 * gap, dep, h, m, "Stand");
            }
        }

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
            for (int i = 0; i < m.Players.Count; i++) heroes[i].Sync(m, m.Players[i], t, m.Mode != GameMode.OneVsOne && m.Mode != GameMode.Training && i == 0);

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
    }
}
