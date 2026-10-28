// Rigged hero models (stage 3 art). Each hero lives in Resources/Heroes/<Folder>/:
//   model.fbx (Mixamo, humanoid), tail.fbx (optional, Blender), albedo.png,
//   idle / run / kick / kick_strong / dash / stun .fbx (Mixamo, without skin).
// Animations are blended with a small PlayableGraph driven from the sim state — no Animator Controller asset.
using System;
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Arena.Game
{
    public enum HeroAnim { Idle, Run, Kick, KickStrong, Dash, Stun }

    [Serializable]
    public class HeroArtTuning
    {
        [Tooltip("Рост модели на поле, м (с причёской)")] [Range(0.5f, 2)] public float height = 1.35f;
        [Tooltip("Скорость героя, при которой бег проигрывается с обычной скоростью")] [Range(100, 600)] public float runRef = 300;
        [Tooltip("Плавность переходов между анимациями, с")] [Range(0.02f, 0.4f)] public float fade = 0.12f;
        [Tooltip("Сколько держать анимацию удара, прежде чем вернуться к бегу, с")] [Range(0.1f, 1.5f)] public float kickHold = 0.45f;
        [Tooltip("Обычный удар: с какой доли анимации начинать (пропустить замах)")] [Range(0, 0.9f)] public float kickStart = 0.3f;
        [Range(0.3f, 3)] public float kickSpeed = 1.4f;
        [Tooltip("Сильный удар/навес: с какой доли анимации начинать")] [Range(0, 0.9f)] public float strongStart = 0.4f;
        [Range(0.3f, 3)] public float strongSpeed = 1.3f;
        [Tooltip("Огненный суперудар: замах начинается с этой доли анимации")] [Range(0, 0.9f)] public float fireStart = 0.15f;
        [Range(0.3f, 3)] public float fireSpeed = 1.2f;
        [Tooltip("Рывок: с какой доли анимации подката начинать")] [Range(0, 0.9f)] public float dashStart = 0.15f;
        [Range(0.3f, 3)] public float dashSpeed = 1.6f;
        [Header("Хвост")]
        [Tooltip("Насколько хвост отклоняется от движения")] [Range(0, 3)] public float tailAmount = 1;
        [Range(5, 200)] public float tailStiff = 70;
        [Range(0, 30)] public float tailDamp = 9;
        [Tooltip("Помахивание хвостом, градусы")] [Range(0, 30)] public float tailWag = 8;
        [Tooltip("Развернуть хвост на 180° (если модель в Mixamo поворачивали). Применяется к новому матчу.")] public bool tailFlip = true;
    }

    public class HeroModel
    {
        static readonly string[] Folder = { "Jar", "Bagor", "Mig", "Vihr", "Ten", "Stena" };
        static readonly string[] ClipFile = { "idle", "run", "kick", "kick_strong", "dash", "stun" };

        public static HeroArtTuning Tune = new HeroArtTuning();
        public static float FrameDt; // set by ArenaGame each frame; 0 while paused or in hitstop

        class Art
        {
            public GameObject Model, Tail; public Texture2D Albedo;
            public AnimationClip[] Clips; public readonly Material[] TeamMat = new Material[2];
        }
        static readonly Dictionary<Hero, Art> cache = new Dictionary<Hero, Art>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); Tune = new HeroArtTuning(); FrameDt = 0; }

        static Art Load(Hero h)
        {
            if (cache.TryGetValue(h, out var a)) return a; // null = no art for this hero yet
            int i = (int)h;
            if (i < Folder.Length)
            {
                string dir = "Heroes/" + Folder[i] + "/";
                var model = Resources.Load<GameObject>(dir + "model");
                if (model != null)
                {
                    a = new Art { Model = model, Tail = Resources.Load<GameObject>(dir + "tail"), Albedo = Resources.Load<Texture2D>(dir + "albedo"), Clips = new AnimationClip[ClipFile.Length] };
                    for (int c = 0; c < ClipFile.Length; c++)
                        foreach (var clip in Resources.LoadAll<AnimationClip>(dir + ClipFile[c]))
                            if (!clip.name.StartsWith("__preview__")) { a.Clips[c] = clip; break; }
                    if (a.Clips[0] == null) Debug.LogWarning("[Arena] " + dir + "idle: нет анимации, модель будет стоять в Т-позе");
                }
            }
            cache[h] = a;
            return a;
        }

        /// <summary>White/grey kit pixels (low saturation, bright) are recoloured to the team colour.</summary>
        static Material MakeMat(Texture2D src, Team team)
        {
            if (src == null) return Mat.Solid(Color.white);
            Texture2D tex = src;
            if (src.isReadable)
            {
                Color tc = team == Team.Home ? Mat.Home : Mat.Away;
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    int mx = Math.Max(c.r, Math.Max(c.g, c.b)), mn = Math.Min(c.r, Math.Min(c.g, c.b));
                    float v = mx / 255f, s = mx == 0 ? 0 : (mx - mn) / (float)mx;
                    float k = Mathf.Clamp01((0.2f - s) / 0.08f) * Mathf.Clamp01((v - 0.4f) / 0.2f);
                    if (k <= 0) continue;
                    float l = Mathf.Min(1, v * 1.08f);
                    px[i] = new Color32((byte)Mathf.Lerp(c.r, 255 * tc.r * l, k), (byte)Mathf.Lerp(c.g, 255 * tc.g * l, k), (byte)Mathf.Lerp(c.b, 255 * tc.b * l, k), c.a);
                }
                tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = src.name + "_" + team };
                tex.SetPixels32(px); tex.Apply(true, true);
            }
            return Mat.Textured(tex);
        }

        public static HeroModel Create(Hero h, Team team, Transform parent)
        {
            var a = Load(h);
            return a == null ? null : new HeroModel(a, team, parent);
        }

        public readonly GameObject Go;
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] plays;
        readonly float[] w, len; readonly bool[] loops;
        readonly bool animated;
        readonly float unitScale; // scale for a 1 m tall model
        HeroAnim cur = HeroAnim.Idle; float fadeRate = 10;
        readonly Transform tail; readonly Quaternion tailRest; Vector3 tailPrev; Vector2 sway, swayV; float wagT;

        HeroModel(Art a, Team team, Transform parent)
        {
            Go = Object.Instantiate(a.Model, parent, false); Go.name = "model";
            var mat = a.TeamMat[(int)team];
            if (mat == null) mat = a.TeamMat[(int)team] = MakeMat(a.Albedo, team);
            foreach (var r in Go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            var anim = Go.GetComponent<Animator>();

            // tail: hang it on the hips so it follows the run
            if (a.Tail != null)
            {
                var inst = Object.Instantiate(a.Tail, Go.transform, false);
                if (Tune.tailFlip) inst.transform.localRotation = Quaternion.Euler(0, 180, 0) * inst.transform.localRotation;
                var tr = inst.GetComponentInChildren<Renderer>();
                if (tr != null)
                {
                    tr.sharedMaterial = mat; tail = tr.transform;
                    var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                    if (hips != null) { tail.SetParent(hips, true); if (inst.transform != tail) Object.Destroy(inst); }
                    tailRest = tail.localRotation; tailPrev = tail.position;
                }
            }

            // fit the height
            var skin = Go.GetComponentInChildren<SkinnedMeshRenderer>();
            float hgt = skin != null ? skin.bounds.size.y : 1.8f;
            unitScale = 1 / Mathf.Max(0.01f, hgt);
            Go.transform.localScale = Vector3.one * (Tune.height * unitScale);

            int n = ClipFile.Length;
            plays = new AnimationClipPlayable[n]; w = new float[n]; len = new float[n]; loops = new bool[n];
            if (anim != null && a.Clips[0] != null)
            {
                anim.applyRootMotion = false; anim.runtimeAnimatorController = null;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("hero");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "anim", anim);
                mixer = AnimationMixerPlayable.Create(graph, n);
                output.SetSourcePlayable(mixer);
                for (int i = 0; i < n; i++)
                {
                    var clip = a.Clips[i] != null ? a.Clips[i] : a.Clips[0];
                    plays[i] = AnimationClipPlayable.Create(graph, clip);
                    len[i] = clip.length; loops[i] = clip.isLooping;
                    graph.Connect(plays[i], 0, mixer, i);
                }
                w[0] = 1; mixer.SetInputWeight(0, 1);
                graph.Play(); graph.Evaluate(0);
                Go.AddComponent<HeroGraphOwner>().Graph = graph;
                animated = true;
            }
        }

        public void Play(HeroAnim s, float fade, bool restart = false, float speed = 1, float startAt = 0)
        {
            if (!animated) return;
            int i = (int)s;
            if (s != cur || restart)
            {
                if (restart || !loops[i]) plays[i].SetTime(startAt * len[i]);
                cur = s; fadeRate = 1f / Mathf.Max(0.01f, fade);
            }
            plays[i].SetSpeed(speed);
        }

        public void Tick(float dt)
        {
            if (dt <= 0 || !Go.activeInHierarchy) return;
            Go.transform.localScale = Vector3.one * (Tune.height * unitScale); // live from the Inspector
            if (animated)
            {
                float sum = 0;
                for (int i = 0; i < w.Length; i++) { w[i] = Mathf.MoveTowards(w[i], i == (int)cur ? 1 : 0, dt * fadeRate); sum += w[i]; }
                for (int i = 0; i < w.Length; i++) mixer.SetInputWeight(i, sum > 0 ? w[i] / sum : 0);
                graph.Evaluate(dt);
            }
            if (tail != null)
            {   // a damped spring: the tail lags behind the body's motion and wags a little
                var T = Tune;
                Vector3 vel = (tail.position - tailPrev) / dt; tailPrev = tail.position;
                Vector3 lv = Go.transform.InverseTransformDirection(vel); // m/s in the hero's own axes
                Vector2 target = new Vector2(-lv.z, lv.x) * 12 * T.tailAmount;
                target = Vector2.ClampMagnitude(target, 40);
                swayV += ((target - sway) * T.tailStiff - swayV * T.tailDamp) * dt;
                sway += swayV * dt;
                wagT += dt * (5 + vel.magnitude * 2);
                tail.localRotation = tailRest * Quaternion.Euler(sway.x, Mathf.Sin(wagT) * T.tailWag, sway.y);
            }
        }
    }
}
