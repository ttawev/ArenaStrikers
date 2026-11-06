// Фон главного меню: ночной стадион без матча, как в тизере. Герои спрятаны, мяч парит и светится в центре поля,
// под ним — светящийся круг; в воздухе медленно летают искры и ледяные блёстки; камера неспешно облетает стадион.
// Включает ArenaGame на главном экране и в настройках (MenuCineOn). Ползунки — Inspector → Arena → «Фон меню».
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    [System.Serializable]
    public class MenuStageTuning
    {
        [Tooltip("Скорость облёта, градусов в секунду")] [Range(0, 20)] public float orbitSpeed = 5f;
        [Tooltip("Расстояние камеры до мяча, м (меняется плавно от меньшего к большему)")] public Vector2 distance = new Vector2(7f, 13f);
        [Tooltip("Высота камеры, м (от и до)")] public Vector2 height = new Vector2(1.4f, 5f);
        [Tooltip("Угол обзора")] [Range(20, 60)] public float fov = 34f;
        [Tooltip("Сдвиг мяча вправо в кадре (чтобы не прятался за меню), градусы")] [Range(0, 25)] public float frameShift = 11f;
        [Tooltip("Мяч парит: высота, см")] [Range(0, 40)] public float ballHover = 12f;
        [Tooltip("Свет от мяча")] [Range(0, 8)] public float ballLight = 2.5f;
        [Tooltip("Искр в секунду")] [Range(0, 80)] public float embers = 18f;
        [Tooltip("Ледяных блёсток в секунду")] [Range(0, 80)] public float sparkles = 14f;
    }

    public class MenuStage
    {
        public static MenuStageTuning Cfg = new MenuStageTuning();

        readonly GameObject root;
        readonly Light ballLight;
        readonly Transform glowRing, glowWave;
        readonly Material ringMat, waveMat;
        readonly ParticleSystem embers, sparkles;
        float t;

        public MenuStage(Transform parent)
        {
            root = new GameObject("menuStage"); root.transform.SetParent(parent, false);
            var c = W.Pos(Field.FW / 2, Field.FH / 2);
            var lgo = new GameObject("menuBallLight"); lgo.transform.SetParent(root.transform, false); lgo.transform.position = c + Vector3.up * 0.6f;
            ballLight = lgo.AddComponent<Light>();
            ballLight.type = LightType.Point; ballLight.range = 4.5f; ballLight.color = new Color(0.6f, 0.85f, 1f); ballLight.shadows = LightShadows.None;
            ballLight.renderMode = LightRenderMode.ForcePixel;

            ringMat = Mat.Clear(new Color(0.55f, 0.85f, 1f, 0.8f), true);
            glowRing = Shapes.MeshObj(Shapes.Ring(0.42f, 0.5f, 64), root.transform, ringMat, "menuRing").transform;
            glowRing.position = c + Vector3.up * 0.02f;
            waveMat = Mat.Clear(new Color(1f, 0.6f, 0.2f, 0.5f), true);
            glowWave = Shapes.MeshObj(Shapes.Ring(0.5f, 0.56f, 64), root.transform, waveMat, "menuWave").transform;
            glowWave.position = c + Vector3.up * 0.015f;

            var dot = SoftDot();
            embers = Air("menuEmbers", c, dot, new Color(2.4f, 1.3f, 0.5f, 1), new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.08f), 0.04f, 0.09f, -0.04f);
            sparkles = Air("menuSparkles", c, dot, new Color(1.2f, 1.8f, 2.4f, 1), new Color(0.75f, 0.95f, 1f), new Color(0.6f, 0.5f, 1f), 0.025f, 0.06f, 0.01f);
            root.SetActive(false);
        }

        ParticleSystem Air(string name, Vector3 center, Texture2D dot, Color tint, Color c0, Color c1, float s0, float s1, float gravity)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.position = center + Vector3.up * 2.5f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.playOnAwake = false; main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
            main.gravityModifier = gravity;
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.prewarm = true;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, 5f, 16f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.4f; noise.scrollSpeed = 0.15f;
            var mat = Mat.ParticleMat(true); mat.SetTexture("_BaseMap", dot); mat.SetColor("_BaseColor", tint);
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        static Texture2D SoftDot()
        {
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "menuDot" };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2;
                float a = Mathf.Clamp01(1 - r); a *= a;
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Каждый кадр: показать/спрятать сцену, оживить свет, круг и частицы.</summary>
        public void Tick(bool on, float dt)
        {
            if (root.activeSelf != on)
            {
                root.SetActive(on);
                if (on) { embers.Play(); sparkles.Play(); }
            }
            if (!on) return;
            t += dt;
            var c = Cfg;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.1f);
            ballLight.intensity = c.ballLight * (0.75f + 0.25f * pulse);
            ballLight.color = Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(1f, 0.6f, 0.25f), 0.5f + 0.5f * Mathf.Sin(t * 0.37f)); // магия ↔ огонь
            glowRing.localScale = Vector3.one * (1 + 0.05f * pulse);
            ringMat.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, 0.55f + 0.35f * pulse));
            float wv = (t * 0.45f) % 1f; // расходящаяся огненная волна
            glowWave.localScale = Vector3.one * (1 + wv * 3.5f);
            waveMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.2f, 0.5f * (1 - wv) * (1 - wv)));
            var e1 = embers.emission; e1.rateOverTime = c.embers;
            var e2 = sparkles.emission; e2.rateOverTime = c.sparkles;
        }

        /// <summary>Мяч стоит в центре поля и парит (вместо матча ботов). Матч в меню — фоновый, его не жалко.</summary>
        public void HoldBall(Match m)
        {
            var b = m.Ball;
            b.Owner = null; b.X = Field.FW / 2; b.Y = Field.FH / 2; b.Vx = b.Vy = b.Vz = 0;
            b.Z = Cfg.ballHover * (0.7f + 0.3f * Mathf.Sin(t * 1.6f));
        }

        /// <summary>Камера: медленный облёт вокруг мяча, то ниже и ближе, то выше и дальше.</summary>
        public void CameraPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            var c = Cfg;
            Vector3 ball = W.Pos(Field.FW / 2, Field.FH / 2) + Vector3.up * 0.3f;
            float ang = (200 + t * c.orbitSpeed) * Mathf.Deg2Rad;
            float k1 = 0.5f + 0.5f * Mathf.Sin(t * 0.11f), k2 = 0.5f + 0.5f * Mathf.Sin(t * 0.077f + 1.3f);
            float dist = Mathf.Lerp(c.distance.x, c.distance.y, k1), h = Mathf.Lerp(c.height.x, c.height.y, k2);
            pos = ball + new Vector3(Mathf.Cos(ang) * dist, h, Mathf.Sin(ang) * dist);
            Vector3 look = ball + Vector3.up * 0.15f;
            rot = Quaternion.LookRotation(look - pos, Vector3.up) * Quaternion.Euler(0, -c.frameShift, 0);
            fov = c.fov;
        }

        public float FocusDistance(Vector3 camPos) => Vector3.Distance(camPos, W.Pos(Field.FW / 2, Field.FH / 2));
    }
}
