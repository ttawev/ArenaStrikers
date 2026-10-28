// Small helpers for the grey-box visuals: coordinate mapping, materials and a few procedural meshes.
// Stage 3 replaces these with real models and a toon shader.
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.Game
{
    /// <summary>Field units (1500 x 2400, y toward the Home attack goal = up in the prototype) → Unity metres.
    /// The field lies sideways on screen: Home attacks to the right (+x), the camera looks along +z.</summary>
    public static class W
    {
        public const float S = 0.01f; // 1 field unit = 1 cm
        public static Vector3 Pos(float x, float y, float h = 0) => new Vector3((Field.FH / 2 - y) * S, h * S, (Field.FW / 2 - x) * S);
        public static Vector3 Dir(float fx, float fy) => new Vector3(-fy, 0, -fx);
        public static Quaternion Yaw(float fx, float fy) => Quaternion.LookRotation(Dir(fx, fy).sqrMagnitude > 0 ? Dir(fx, fy) : Vector3.forward, Vector3.up);
        /// <summary>Screen-space stick (right, up) → field direction.</summary>
        public static Vector2 StickToField(Vector2 s) => new Vector2(-s.y, -s.x);
    }

    public static class Mat
    {
        static Shader lit, unlit, particles;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        // Unity 6 skips the domain reload when entering Play, so statics survive between sessions
        // while runtime materials are destroyed on exit. Start every session with an empty cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); lit = unlit = particles = null; }

        static Shader Lit => lit ? lit : (lit = Shader.Find("Universal Render Pipeline/Lit"));
        static Shader Unlit => unlit ? unlit : (unlit = Shader.Find("Universal Render Pipeline/Unlit"));
        public static Shader ParticleShader => particles ? particles : (particles = Shader.Find("Universal Render Pipeline/Particles/Unlit"));

        public static Material Solid(Color c, float smooth = 0.15f)
        {
            string key = "s" + ColorUtility.ToHtmlStringRGBA(c) + smooth;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        public static Material Glow(Color c) // unlit, opaque: gems, markers that should not be shaded
        {
            string key = "g" + ColorUtility.ToHtmlStringRGBA(c);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Unlit);
            m.SetColor("_BaseColor", c);
            cache[key] = m;
            return m;
        }

        /// <summary>A new lit material with a colour texture (hero models).</summary>
        public static Material Textured(Texture tex, float smooth = 0.1f)
        {
            var m = new Material(Lit);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        /// <summary>A new (not cached) transparent unlit material; additive for glows.</summary>
        public static Material Clear(Color c, bool additive = false)
        {
            var m = new Material(Unlit);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material ParticleMat(bool additive)
        {
            var m = new Material(ParticleShader);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static readonly Color Home = Hex("#ffb21e"), Away = Hex("#ff4f72"), Chalk = Hex("#d9efe6"), Night = Hex("#08151a");
        public static readonly Color Fire = Hex("#ff8a1e"), FireYellow = Hex("#ffd23e"), Blink = Hex("#8fd8ff"), Spin = Hex("#7fe8d0"), Mist = Hex("#e6f8f0"), Dust = Hex("#cfe8d8");

        public static Color Of(FxColor c)
        {
            switch (c)
            {
                case FxColor.Fire: return Fire;
                case FxColor.FireYellow: return FireYellow;
                case FxColor.Blink: return Blink;
                case FxColor.Home: return Home;
                case FxColor.Away: return Away;
                case FxColor.Spin: return Spin;
                case FxColor.Dust: return Dust;
                case FxColor.Mist: return Mist;
                case FxColor.Ice: return Hex("#bff3ff");
                default: return Color.white;
            }
        }
    }

    public static class Shapes
    {
        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos, Vector3 scale, Material m, string name = null)
        {
            var g = GameObject.CreatePrimitive(t);
            if (name != null) g.name = name;
            Object.Destroy(g.GetComponent<Collider>()); // our own physics, no Unity colliders needed
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            return g;
        }

        public static GameObject MeshObj(Mesh mesh, Transform parent, Material m, string name = "mesh")
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            return g;
        }

        /// <summary>Cone along +Y, base at y=0.</summary>
        public static Mesh Cone(float radius, float height, int seg = 14)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            v.Add(new Vector3(0, height, 0)); v.Add(Vector3.zero);
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius)); }
            for (int i = 0; i < seg; i++) { t.Add(0); t.Add(i + 3); t.Add(i + 2); t.Add(1); t.Add(i + 2); t.Add(i + 3); }
            var m = new Mesh { name = "cone" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat ring in the XZ plane facing up; sweep in radians starting at 'start' (0 = +Z, clockwise seen from above).</summary>
        public static Mesh Ring(float inner, float outer, int seg = 40, float start = 0, float sweep = Mathf.PI * 2, Mesh reuse = null)
        {
            var v = new Vector3[(seg + 1) * 2]; var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = start + sweep * i / seg, s = Mathf.Sin(a), c = Mathf.Cos(a);
                v[i * 2] = new Vector3(s * inner, 0, c * inner); v[i * 2 + 1] = new Vector3(s * outer, 0, c * outer);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 2;
                t[i * 6] = k; t[i * 6 + 1] = k + 1; t[i * 6 + 2] = k + 2; t[i * 6 + 3] = k + 1; t[i * 6 + 4] = k + 3; t[i * 6 + 5] = k + 2;
            }
            var m = reuse ? reuse : new Mesh { name = "ring" };
            m.Clear(); m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static Mesh Disc(float radius, int seg = 24) => Ring(0, radius, seg);
    }
}
