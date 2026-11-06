// Метка своего героя на поле:
//  • над головой — золотой «шеврон» с тёмной обводкой, всегда повёрнут к камере, покачивается и сияет (bloom);
//    размер на экране почти не зависит от приближения камеры;
//  • под ногами — золотое кольцо с разрывом и стрелкой: куда смотрит герой (туда пойдёт пас и удар).
// Настройки — Inspector → Arena → «Метка своего героя» (YouMarker.Cfg задаёт ArenaGame каждый кадр).
using UnityEngine;

namespace Arena.Game
{
    [System.Serializable]
    public class YouMarkerTuning
    {
        [Tooltip("Шеврон над головой")] public bool head = true;
        [Tooltip("Кольцо со стрелкой под ногами (куда смотрит герой)")] public bool ground = true;
        [Tooltip("Цвет метки")] public Color color = new Color(1f, 0.82f, 0.25f, 1);
        [Tooltip("Яркость сияния (больше 1 — светится, bloom)")] [Range(0.5f, 3)] public float glow = 1.6f;
        [Tooltip("Размер шеврона над головой")] [Range(0.4f, 2.5f)] public float headSize = 1f;
        [Tooltip("Высота шеврона над землёй, м")] [Range(1.2f, 3f)] public float headHeight = 1.95f;
        [Tooltip("Держать размер шеврона на экране постоянным при приближении/отдалении камеры")] public bool constantSize = true;
        [Tooltip("Прозрачность кольца под ногами")] [Range(0, 1)] public float groundAlpha = 0.85f;
    }

    public class YouMarker
    {
        public static YouMarkerTuning Cfg = new YouMarkerTuning();
        const float S = W.S;

        readonly GameObject headRoot, ground;
        readonly Transform chevron, outline;
        readonly Material headMat, groundMat;
        float popT; bool wasOn;

        public YouMarker(Transform heroRoot)
        {
            headRoot = new GameObject("youMark"); headRoot.transform.SetParent(heroRoot, false);
            headMat = Mat.Clear(Color.white, false); headMat.renderQueue = 3100; // поверх полупрозрачных эффектов
            var dark = Mat.Clear(new Color(0.05f, 0.04f, 0.02f, 0.85f), false); dark.renderQueue = 3099;
            outline = Shapes.MeshObj(Chevron(1.32f), headRoot.transform, dark, "outline").transform;
            chevron = Shapes.MeshObj(Chevron(1f), headRoot.transform, headMat, "chevron").transform;
            Shadowless(headRoot);

            groundMat = Mat.Clear(Color.white, true);
            ground = Shapes.MeshObj(GroundMesh(), heroRoot, groundMat, "youRing");
            ground.transform.localPosition = new Vector3(0, 1.6f * S, 0);
            Shadowless(ground);
            headRoot.SetActive(false); ground.SetActive(false);
        }

        static void Shadowless(GameObject g)
        {
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        public void Sync(bool show, float t, float dt)
        {
            var c = Cfg;
            bool head = show && c.head, gr = show && c.ground;
            if (show && !wasOn) popT = 0;
            wasOn = show;
            headRoot.SetActive(head); ground.SetActive(gr);
            if (!show) return;
            popT = Mathf.Min(1, popT + dt * 3.5f);

            Color hc = c.color * c.glow; hc.a = 1;
            headMat.SetColor("_BaseColor", hc);
            Color gc = c.color * (0.6f + 0.4f * c.glow); gc.a = c.groundAlpha * (0.8f + 0.2f * Mathf.Sin(t * 4));
            groundMat.SetColor("_BaseColor", gc);

            if (head)
            {   // появление «пружинкой», покачивание, к камере лицом
                float pop = popT >= 1 ? 1 : 1 + Mathf.Sin(popT * Mathf.PI) * 0.45f * (1 - popT * 0.4f) - (1 - popT) * 0.6f;
                pop = Mathf.Max(0.05f, pop);
                var hr = headRoot.transform;
                hr.localPosition = new Vector3(0, c.headHeight + Mathf.Sin(t * 4.2f) * 0.05f, 0);
                var cam = Camera.main;
                float k = 1;
                if (cam != null)
                {
                    hr.rotation = cam.transform.rotation;
                    if (c.constantSize) k = Mathf.Clamp(Vector3.Distance(cam.transform.position, hr.position) / 15f, 0.35f, 2.2f);
                    outline.localPosition = new Vector3(0, 0, 0.02f); // чуть дальше от камеры
                }
                hr.localScale = Vector3.one * (c.headSize * k * pop * (1 + 0.05f * Mathf.Sin(t * 8.4f)));
            }
            if (gr)
            {
                float s = 1 + 0.03f * Mathf.Sin(t * 4);
                ground.transform.localScale = new Vector3(s, 1, s) * Mathf.Lerp(1.4f, 1f, Mathf.SmoothStep(0, 1, popT));
            }
        }

        public void Destroy() { Object.Destroy(headRoot); Object.Destroy(ground); }

        // ---------- сетки ----------
        /// <summary>Шеврон «вниз» в плоскости XY (м), двусторонний; scale — для обводки (вокруг центра).</summary>
        static Mesh Chevron(float scale)
        {
            // контур в сантиметрах: верхние концы «крыльев», внутренний вырез, нижний острый кончик
            Vector2[] p = { new(-21, 14), new(-9, 14), new(0, 3), new(9, 14), new(21, 14), new(0, -12) };
            Vector2 c = new Vector2(0, 5);
            var v = new Vector3[p.Length];
            for (int i = 0; i < p.Length; i++) { var q = c + (p[i] - c) * scale; v[i] = new Vector3(q.x * 0.01f, q.y * 0.01f, 0); }
            int[] tri = { 0, 1, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5 };
            return TwoSided(v, tri, "chevron");
        }

        /// <summary>Кольцо под ногами с разрывом впереди и стрелкой в разрыве (локальный +Z — куда смотрит герой).</summary>
        static Mesh GroundMesh()
        {
            float r0 = 44 * S, r1 = 50 * S, gap = 0.42f; int seg = 48;
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = gap + (Mathf.PI * 2 - gap * 2) * i / seg, s = Mathf.Sin(a), co = Mathf.Cos(a);
                v.Add(new Vector3(s * r0, 0, co * r0)); v.Add(new Vector3(s * r1, 0, co * r1));
            }
            for (int i = 0; i < seg; i++) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            // стрелка: основание у кольца, кончик впереди
            int b = v.Count;
            v.Add(new Vector3(-13 * S, 0, 42 * S)); v.Add(new Vector3(0, 0, 70 * S)); v.Add(new Vector3(13 * S, 0, 42 * S)); v.Add(new Vector3(0, 0, 50 * S));
            t.AddRange(new[] { b, b + 1, b + 3, b + 3, b + 1, b + 2 }); // с вырезом сзади — тоже шеврон
            return TwoSided(v.ToArray(), t.ToArray(), "youRing");
        }

        static Mesh TwoSided(Vector3[] v, int[] tri, string name)
        {
            var t2 = new int[tri.Length * 2];
            for (int i = 0; i < tri.Length; i += 3)
            {
                t2[i] = tri[i]; t2[i + 1] = tri[i + 1]; t2[i + 2] = tri[i + 2];
                t2[tri.Length + i] = tri[i]; t2[tri.Length + i + 1] = tri[i + 2]; t2[tri.Length + i + 2] = tri[i + 1];
            }
            var m = new Mesh { name = name }; m.vertices = v; m.triangles = t2; m.RecalculateBounds();
            return m;
        }
    }
}
