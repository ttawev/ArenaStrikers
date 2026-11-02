// Эффекты гола на поле: задняя сетка ворот прогибается «мешком», мяч влетает в сетку и падает,
// из штанг бьют конфетти в цвет забившей команды. Всё только визуально — симуляция не меняется
// (в ней мяч после гола замирает у линии ворот, см. Match.Goal).
using Arena.Sim;
using UnityEngine;
using static Arena.Sim.Field;

namespace Arena.Game
{
    /// <summary>Задняя сетка ворот: сетка вершин, прогиб — затухающая пружина вокруг места удара.</summary>
    public class GoalNet
    {
        const int NX = 22, NZ = 9;
        readonly float y0, dir;
        readonly Mesh mesh; readonly Vector3[] verts;
        float amp, vel, hx = FW / 2, hz = GOALH / 2;
        bool flat = true;

        public GoalNet(Transform root, float y0, float dir, Material m)
        {
            this.y0 = y0; this.dir = dir;
            mesh = new Mesh { name = "goalNetBack" };
            int n = (NX + 1) * (NZ + 1);
            verts = new Vector3[n * 2]; var uv = new Vector2[n * 2];
            for (int k = 0; k < 2; k++)
                for (int j = 0; j <= NZ; j++)
                    for (int i = 0; i <= NX; i++) uv[k * n + j * (NX + 1) + i] = new Vector2(i / (float)NX, j / (float)NZ);
            var tris = new int[NX * NZ * 12]; int t = 0;
            for (int j = 0; j < NZ; j++)
                for (int i = 0; i < NX; i++)
                {
                    int a = j * (NX + 1) + i, b = a + 1, c = a + NX + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d;           // одна сторона
                    tris[t++] = n + a; tris[t++] = n + b; tris[t++] = n + c; tris[t++] = n + b; tris[t++] = n + d; tris[t++] = n + c; // и обратная
                }
            Rebuild();
            mesh.vertices = verts; // сначала вершины, потом остальное (иначе сетка пустая до первого гола)
            mesh.uv = uv; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = Shapes.MeshObj(mesh, root, m, "NetBack");
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Насколько сетка прогнулась в этой точке (единицы поля, наружу от поля).</summary>
        public float DispAt(float x, float z) => Disp(x, z);

        float Disp(float x, float z)
        {
            float u = Mathf.Clamp01((x - GX1) / GOAL), v = Mathf.Clamp01(z / GOALH);
            float dx = x - hx, dz = z - hz;
            float g = Mathf.Exp(-(dx * dx) / (2 * 75f * 75f) - (dz * dz) / (2 * 32f * 32f));
            return amp * g * Mathf.Sin(Mathf.PI * u) * Mathf.Cos(Mathf.PI * v * 0.5f); // края и перекладина держат сетку
        }

        public void Hit(float x, float z, float speed)
        {
            hx = Mathf.Clamp(x, GX1 + 40, GX2 - 40); hz = Mathf.Clamp(z, 8, GOALH - 12);
            vel += Mathf.Min(speed, 2600) * GoalFx.NetPunch;
        }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            float k = GoalFx.NetStiff, c = 2 * Mathf.Sqrt(k) * GoalFx.NetDamp;
            vel += (-k * amp - c * vel) * dt; amp += vel * dt;
            amp = Mathf.Clamp(amp, -8, 34); // за сеткой — пустота, но не бесконечная
            if (Mathf.Abs(amp) < 0.02f && Mathf.Abs(vel) < 0.05f) { amp = vel = 0; if (flat) return; flat = true; }
            else flat = false;
            Rebuild();
            mesh.vertices = verts; mesh.RecalculateBounds();
        }

        void Rebuild()
        {
            int n = (NX + 1) * (NZ + 1);
            float yb = y0 + dir * NET;
            for (int j = 0; j <= NZ; j++)
                for (int i = 0; i <= NX; i++)
                {
                    float x = GX1 + GOAL * i / NX, z = GOALH * j / NZ;
                    var p = W.Pos(x, yb + dir * Disp(x, z), z);
                    verts[j * (NX + 1) + i] = p; verts[n + j * (NX + 1) + i] = p;
                }
        }
    }

    public class GoalFx
    {
        // настраиваются из Inspector (ArenaGame → «Гол»)
        public static float NetPunch = 0.16f, NetStiff = 110, NetDamp = 0.32f;
        public static int ConfettiCount = 140;

        readonly Transform root;
        public readonly GoalNet[] Nets = new GoalNet[2]; // [0] — ворота у y = 0, [1] — у y = FH
        ParticleSystem confetti;

        // мяч, летящий в сетку после гола
        bool ballOn, stuck; int ballNet; float bx, by, bz, bvx, bvy, bvz, life, stuckT, simX, simY;

        public GoalFx(Transform root) { this.root = root; }

        public void Hit(Match m, Team team, float x, float y, bool confettiToo)
        {
            var b = m.Ball;
            ballNet = y < FH / 2 ? 0 : 1;
            bx = b.X; by = b.Y; bz = b.Z;
            // в симуляции мяч после гола гасится в 10 раз (Match.Goal) — возвращаем ему скорость для полёта в сетку
            bvx = b.Vx * 10; bvy = b.Vy * 10; bvz = b.Vz;
            float dir = ballNet == 0 ? -1 : 1;
            if (bvy * dir < 250) bvy = dir * 250; // даже закатившийся мяч доходит до сетки
            float sp = new Vector2(bvx, bvy).magnitude;
            if (sp > 2600) { bvx *= 2600 / sp; bvy *= 2600 / sp; }
            simX = b.X; simY = b.Y; life = 5; stuck = false; ballOn = true;
            if (confettiToo) Confetti(team, ballNet == 0 ? 0 : FH);
        }

        /// <summary>Каждый кадр: сетки и мяч в сетке. true — мяч рисовать здесь (pos), а не по симуляции.</summary>
        public bool Tick(Match m, float dt, out Vector3 pos, out Vector3 shadow)
        {
            foreach (var n in Nets) n?.Tick(dt);
            pos = shadow = Vector3.zero;
            if (!ballOn) return false;
            var sb = m.Ball;
            life -= dt;
            // мяч в симуляции переставили (разводка, повтор) — показываем снова настоящий
            if (life <= 0 || Mathf.Abs(sb.X - simX) + Mathf.Abs(sb.Y - simY) > 250) { ballOn = false; return false; }
            float y0 = ballNet == 0 ? 0 : FH, dir = ballNet == 0 ? -1 : 1, lim = NET - BR;
            var net = Nets[ballNet];
            if (stuck)
            {   // мяч в «мешке»: едет вместе с прогибом, потом выпадает
                stuckT -= dt;
                by = y0 + dir * (lim + (net != null ? net.DispAt(bx, bz) : 0));
                bvz -= GRAV * dt * 0.5f; bz = Mathf.Max(0, bz + bvz * dt);
                if (stuckT <= 0) { stuck = false; bvy = -dir * 90; bvx *= 0.3f; }
            }
            else
            {
                bx += bvx * dt; by += bvy * dt; bz += bvz * dt; bvz -= GRAV * dt;
                if (bz < 0) { bz = 0; bvz = -bvz * 0.4f; bvx *= 0.8f; bvy *= 0.8f; if (bvz < 40) bvz = 0; }
                if (bz <= 0.5f) { float f = Mathf.Exp(-2.5f * dt); bvx *= f; bvy *= f; }
                if (bz > GOALH - BR * 2) { bz = GOALH - BR * 2; if (bvz > 0) bvz = -bvz * 0.3f; }
                if (bx < GX1 + BR) { bx = GX1 + BR; bvx = Mathf.Abs(bvx) * 0.4f; }
                if (bx > GX2 - BR) { bx = GX2 - BR; bvx = -Mathf.Abs(bvx) * 0.4f; }
                float depth = (by - y0) * dir;
                if (depth > lim)
                {
                    float hit = Mathf.Abs(bvy);
                    if (hit > 120 && net != null) { net.Hit(bx, bz, hit); stuck = true; stuckT = 0.28f; bvz *= 0.3f; }
                    else bvy = -dir * hit * 0.2f;
                    by = y0 + dir * lim;
                }
                if (depth < BR) { by = y0 + dir * BR; bvy = dir * Mathf.Abs(bvy) * 0.3f; }
            }
            pos = W.Pos(bx, by, BR + bz);
            shadow = W.Pos(bx, by, 0.7f);
            return true;
        }

        // ---------- конфетти ----------
        static Texture2D paper;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { paper = null; NetPunch = 0.16f; NetStiff = 110; NetDamp = 0.32f; ConfettiCount = 140; }

        void BuildConfetti()
        {
            var go = new GameObject("Confetti"); go.transform.SetParent(root, false);
            confetti = go.AddComponent<ParticleSystem>();
            confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = confetti.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.playOnAwake = false; main.maxParticles = 1500;
            main.gravityModifier = 0.32f;
            main.startRotation3D = true;
            var em = confetti.emission; em.enabled = false;
            var sh = confetti.shape; sh.enabled = false;
            var lim = confetti.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.2f; lim.dampen = 0.06f; // вылетели — и плавно планируют вниз
            var rot = confetti.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-9, 9); rot.y = new ParticleSystem.MinMaxCurve(-6, 6); rot.z = new ParticleSystem.MinMaxCurve(-4, 4);
            var noise = confetti.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.8f;
            var col = confetti.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = confetti.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            r.alignment = ParticleSystemRenderSpace.World;
            if (paper == null) { paper = new Texture2D(2, 2) { name = "confetti" }; paper.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) }); paper.Apply(); }
            var mat = Mat.ParticleMat(false); mat.SetTexture("_BaseMap", paper); mat.SetFloat("_Cull", 0); // бумажки видны с обеих сторон
            r.sharedMaterial = mat;
            confetti.Play();
        }

        void Confetti(Team team, float y0)
        {
            if (ConfettiCount <= 0) return;
            if (confetti == null) BuildConfetti();
            float dir = y0 < FH / 2 ? -1 : 1; // наружу от поля; конфетти летят в поле (-dir)
            Color tc = team == Team.Home ? Mat.Home : Mat.Away;
            Color[] cols = { tc, tc, Color.Lerp(tc, Color.white, 0.45f), Color.white, Mat.FireYellow };
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            foreach (float px in new[] { GX1, GX2 })
                for (int i = 0; i < ConfettiCount; i++)
                {
                    float side = px < FW / 2 ? -1 : 1;
                    e.position = W.Pos(px, y0, GOALH + 4);
                    // вверх и в поле, веером от штанги
                    float up = Random.Range(650, 1150), fwd = Random.Range(80, 420), out_ = Random.Range(-120, 360) * side;
                    e.velocity = new Vector3(0, up * W.S, 0) + W.Dir(out_, -dir * fwd) * W.S;
                    float s = Random.Range(0.07f, 0.12f);
                    e.startSize3D = new Vector3(s, s * Random.Range(0.45f, 0.7f), 1);
                    e.rotation3D = new Vector3(Random.value * 360, Random.value * 360, Random.value * 360);
                    e.startLifetime = Random.Range(3.5f, 5.5f);
                    e.startColor = cols[Random.Range(0, cols.Length)];
                    confetti.Emit(e, 1);
                }
        }
    }
}
