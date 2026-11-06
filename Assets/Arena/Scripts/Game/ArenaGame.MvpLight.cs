// Экран лучшего игрока (MVP): свой свет на героя, чтобы он не терялся на фоне ярких трибун.
// Ключевой свет — тёплый, спереди-сбоку-сверху (со стороны камеры, слева в кадре); контровой — холодный, сзади-справа
// по краю силуэта; фон вокруг героя чуть притушен (виньетка в OnGUI); свечение лавы/пламени — ярче.
// Работает только на экране MVP, в матче ничего не меняет. Ползунки — Inspector → Arena → «Экран MVP».
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    [System.Serializable]
    public class MvpLightTuning
    {
        [Tooltip("Ключевой свет на герое спереди-сбоку (0 — выкл)")] [Range(0, 15)] public float key = 4f;
        [Tooltip("Цвет ключевого света")] public Color keyColor = new Color(1f, 0.86f, 0.7f, 1);
        [Tooltip("Контровой свет сзади — светлый край силуэта (0 — выкл)")] [Range(0, 20)] public float rim = 7f;
        [Tooltip("Цвет контрового света")] public Color rimColor = new Color(0.55f, 0.8f, 1f, 1);
        [Tooltip("Затемнение фона вокруг героя (0 — без затемнения)")] [Range(0, 0.9f)] public float dim = 0.45f;
        [Tooltip("Свечение пламени и лавы на экране MVP, множитель (1 — как в матче)")] [Range(1, 3)] public float glow = 1.5f;
        [Tooltip("Прятать других героев рядом с MVP, чтобы не загораживали")] public bool hideOthers = true;
        [Tooltip("Радиус, в котором прячутся другие герои, м")] [Range(1, 10)] public float hideRadius = 5.2f;
    }

    public partial class ArenaGame
    {
        [Header("Экран MVP (свет на лучшем игроке)")]
        public MvpLightTuning mvpLight = new MvpLightTuning();

        Light mvpKeyL, mvpRimL;
        static Texture2D mvpVignette;

        /// <summary>Сила «шоу-света» MVP 0..1: плавно включается вместе с облётом камеры, 0 — вне экрана MVP.</summary>
        float MvpLightK()
        {
            if (state != State.Ended || match == null || match.Mvp() == null) return 0;
            return EaseOut((uiTime - endT0 - 0.5f) / 1.5f);
        }

        /// <summary>Каждый кадр (Update): поставить свет на MVP и усилить свечение.</summary>
        void MvpLightTick()
        {
            float k = MvpLightK();
            HeroModel.GlowBoost = Mathf.Lerp(1, mvpLight.glow, k);
            WorldView.MvpSolo = k > 0 && mvpLight.hideOthers ? match.Mvp() : null; // камера уже на MVP (склейка) — соседей прячем
            WorldView.MvpSoloRadius = mvpLight.hideRadius * 100;
            if (k <= 0.001f || cam == null)
            {
                if (mvpKeyL != null) mvpKeyL.enabled = false;
                if (mvpRimL != null) mvpRimL.enabled = false;
                return;
            }
            if (mvpKeyL == null) mvpKeyL = MakeMvpLight("MvpKeyLight");
            if (mvpRimL == null) mvpRimL = MakeMvpLight("MvpRimLight");

            var mvp = match.Mvp();
            Vector3 p = W.Pos(mvp.X, mvp.Y);
            Vector3 chest = p + Vector3.up * 1.1f, head = p + Vector3.up * 1.55f;
            Vector3 toCam = cam.transform.position - p; toCam.y = 0;
            toCam = toCam.sqrMagnitude > 0.0001f ? toCam.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, toCam); // вправо в кадре (если смотреть с камеры) — это -right; влево в кадре — +right

            // ключевой: со стороны камеры, левее в кадре, выше головы
            Vector3 kp = p + toCam * 2.2f + right * 1.4f + Vector3.up * 2.6f;
            SetMvpLight(mvpKeyL, kp, chest, mvpLight.keyColor, mvpLight.key * k, 50);
            // контровой: за героем, правее в кадре, на уровне головы — светит на камеру сквозь силуэт
            Vector3 rp = p - toCam * 1.6f - right * 0.9f + Vector3.up * 2.1f;
            SetMvpLight(mvpRimL, rp, head, mvpLight.rimColor, mvpLight.rim * k, 45);
        }

        Light MakeMvpLight(string name)
        {
            var go = new GameObject(name);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.shadows = LightShadows.None; l.range = 5.5f; l.innerSpotAngle = 15;
            l.renderMode = LightRenderMode.ForcePixel; // важный свет — не отбрасывать в пользу прожекторов
            return l;
        }

        static void SetMvpLight(Light l, Vector3 pos, Vector3 target, Color c, float intensity, float angle)
        {
            l.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up));
            l.color = c; l.intensity = intensity; l.spotAngle = angle;
            l.enabled = intensity > 0.001f;
        }

        /// <summary>OnGUI, до полос и надписей: притушить всё вокруг героя, сам герой остаётся в светлом «окне».</summary>
        void MvpDimGui(float w, float h)
        {
            float a = mvpLight.dim * MvpLightK();
            if (a <= 0.001f || cam == null) return;
            var mvp = match.Mvp();
            Vector3 sp = cam.WorldToScreenPoint(W.Pos(mvp.X, mvp.Y) + Vector3.up * 1.0f);
            if (sp.z <= 0) return;
            if (mvpVignette == null) mvpVignette = MakeVignette();
            float cx = sp.x, cy = h - sp.y;
            float rw = h * 3.2f, rh = h * 4.6f; // окно вытянуто по вертикали — по росту героя
            var old = GUI.color; GUI.color = new Color(0, 0, 0, a);
            var r = new Rect(cx - rw / 2, cy - rh / 2, rw, rh);
            GUI.DrawTexture(r, mvpVignette);
            // если прямоугольник не накрыл край экрана — догнать сплошной заливкой
            if (r.xMin > 0) GUI.DrawTexture(new Rect(0, 0, r.xMin, h), Texture2D.whiteTexture);
            if (r.xMax < w) GUI.DrawTexture(new Rect(r.xMax, 0, w - r.xMax, h), Texture2D.whiteTexture);
            if (r.yMin > 0) GUI.DrawTexture(new Rect(0, 0, w, r.yMin), Texture2D.whiteTexture);
            if (r.yMax < h) GUI.DrawTexture(new Rect(0, r.yMax, w, h - r.yMax), Texture2D.whiteTexture);
            GUI.color = old;
        }

        static Texture2D MakeVignette()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MvpVignette" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.055f, 0.21f, r));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(v * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
