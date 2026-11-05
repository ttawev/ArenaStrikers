// Плашка гола в стиле телетрансляции: тёмная лента выезжает из центра, полосы в цвет команды,
// «ГОЛ!» с ореолом и пружинкой, блик по ленте, ниже — табло со счётом и кто забил.
// Рисуется IMGUI, текстуры создаются кодом, шрифт — Resources/Fonts/Lato-Heavy (лицензия OFL).
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public partial class ArenaGame
    {
        static Texture2D texBand, texGlow, texShine, texRound;
        static Font fontHeavy;
        GUIStyle bTitle, bDigit, bSmall, bRound;

        static readonly string[] HeroNamesUp = { "ЖАР", "БАГОР", "МИГ", "ВИХРЬ", "ТЕНЬ", "СТЕНА" };

        void BannerAssets()
        {
            if (texBand == null)
            {   // лента: края растворяются
                texBand = new Texture2D(256, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 256; x++)
                {
                    float u = x / 255f, a = Mathf.SmoothStep(0, 1, Mathf.Min(u, 1 - u) / 0.18f);
                    for (int y = 0; y < 4; y++) texBand.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                texBand.Apply();
                texGlow = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 128; x++) for (int y = 0; y < 128; y++)
                {
                    float dx = (x - 63.5f) / 63.5f, dy = (y - 63.5f) / 63.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    texGlow.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - r) * Mathf.Clamp01(1 - r)));
                }
                texGlow.Apply();
                texShine = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 128; x++) for (int y = 0; y < 128; y++)
                {
                    float d = (x / 127f - y / 127f * 0.45f - 0.3f) / 0.09f;
                    texShine.SetPixel(x, y, new Color(1, 1, 1, Mathf.Exp(-d * d) * 0.9f));
                }
                texShine.Apply();
                texRound = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 48; x++) for (int y = 0; y < 48; y++)
                {   // скруглённый прямоугольник, радиус 14
                    float cx = Mathf.Clamp(x + 0.5f, 14, 34), cy = Mathf.Clamp(y + 0.5f, 14, 34);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    texRound.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(14.5f - d)));
                }
                texRound.Apply();
                fontHeavy = Resources.Load<Font>("Fonts/Lato-Heavy");
            }
            if (bTitle == null)
            {
                bTitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = fontHeavy != null ? FontStyle.Normal : FontStyle.Bold, clipping = TextClipping.Overflow };
                if (fontHeavy != null) bTitle.font = fontHeavy;
                bDigit = new GUIStyle(bTitle); bSmall = new GUIStyle(bTitle);
                bRound = new GUIStyle { normal = { background = texRound }, border = new RectOffset(16, 16, 16, 16) };
            }
        }

        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }

        /// <summary>Плашка гола. Время — от goalT0 (uiTime), счёт — goalScoreH/A, кто забил — goalScorer.</summary>
        void GoalBanner(float w, float h)
        {
            BannerAssets();
            float t = uiTime - goalT0, k = h / 1080f;
            bool reel = state == State.Reel;
            Team team = reel ? goalTeam : match.BannerTeam;
            Color tc = team == Team.Home ? Mat.Home : Mat.Away;
            string title = reel ? "ГОЛ!" : BannerText();
            float fade = t > 2.7f ? Mathf.Clamp01(1 - (t - 2.7f) / 0.35f) : 1; // уход
            if (fade <= 0) return;
            float cy = h * 0.27f, bandH = 150 * k;
            var old = GUI.color;

            // ореол в цвет команды за лентой
            float pulse = 0.55f + 0.2f * Mathf.Sin(t * 6);
            GUI.color = new Color(tc.r, tc.g, tc.b, 0.45f * pulse * EaseOut(t / 0.3f) * fade);
            GUI.DrawTexture(new Rect(w / 2 - 520 * k, cy - 260 * k, 1040 * k, 520 * k), texGlow);

            // лента выезжает из центра
            float bw = w * 1.1f * EaseOut(t / 0.25f);
            var band = new Rect(w / 2 - bw / 2, cy - bandH / 2, bw, bandH);
            GUI.color = new Color(0.03f, 0.05f, 0.11f, 0.86f * fade); GUI.DrawTexture(band, texBand);
            // полосы в цвет команды сверху и снизу (чуть позже)
            float sw = w * 1.1f * EaseOut((t - 0.06f) / 0.3f);
            GUI.color = new Color(tc.r, tc.g, tc.b, fade);
            GUI.DrawTexture(new Rect(w / 2 - sw / 2, band.y - 7 * k, sw, 7 * k), texBand);
            GUI.DrawTexture(new Rect(w / 2 - sw / 2, band.yMax, sw, 7 * k), texBand);

            // «ГОЛ!» — ореол и пружинка
            float s = t > 6 ? 1 : Spring(t - 0.08f);
            if (s > 0.01f)
            {
                bTitle.fontSize = Mathf.RoundToInt(118 * k);
                var tr = new Rect(0, cy - bandH / 2, w, bandH);
                var m = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * s, tr.center);
                float gr = 5 * k;
                bTitle.normal.textColor = new Color(tc.r, tc.g, tc.b, 0.28f * fade);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    GUI.Label(new Rect(tr.x + Mathf.Cos(a) * gr, tr.y + Mathf.Sin(a) * gr, tr.width, tr.height), title, bTitle);
                }
                bTitle.normal.textColor = new Color(0, 0, 0, 0.6f * fade);
                GUI.Label(new Rect(tr.x + 3 * k, tr.y + 5 * k, tr.width, tr.height), title, bTitle);
                bTitle.normal.textColor = new Color(1, 1, 1, fade);
                GUI.Label(tr, title, bTitle);
                GUI.matrix = m;
            }

            // блик по ленте
            float st = (t - 0.35f) / 0.55f;
            if (st > 0 && st < 1)
            {
                GUI.BeginGroup(band);
                GUI.color = new Color(1, 1, 1, 0.55f * fade);
                float sx = Mathf.Lerp(-300 * k, band.width + 100 * k, st);
                GUI.DrawTexture(new Rect(sx, -20 * k, 260 * k, bandH + 40 * k), texShine);
                GUI.EndGroup();
            }

            // табло со счётом
            if (match.Mode != GameMode.Training)
            {
                float s2 = EaseOut((t - 0.25f) / 0.3f);
                if (s2 > 0.01f)
                {
                    float pw = 420 * k, ph = 96 * k, py = band.yMax + 26 * k + (1 - s2) * 30 * k;
                    var plate = new Rect(w / 2 - pw / 2, py, pw, ph);
                    GUI.color = new Color(0.02f, 0.04f, 0.09f, 0.88f * s2 * fade); GUI.Box(plate, GUIContent.none, bRound);
                    var left = new Rect(plate.x + 10 * k, plate.y + 10 * k, 150 * k, ph - 20 * k);
                    var right = new Rect(plate.xMax - 160 * k, plate.y + 10 * k, 150 * k, ph - 20 * k);
                    GUI.color = new Color(Mat.Home.r, Mat.Home.g, Mat.Home.b, 0.95f * s2 * fade); GUI.Box(left, GUIContent.none, bRound);
                    GUI.color = new Color(Mat.Away.r, Mat.Away.g, Mat.Away.b, 0.95f * s2 * fade); GUI.Box(right, GUIContent.none, bRound);
                    GUI.color = new Color(1, 1, 1, s2 * fade);
                    bool flip = t > 0.75f;
                    int hs = goalScoreH - (goalTeam == Team.Home && !flip ? 1 : 0), aw = goalScoreA - (goalTeam == Team.Away && !flip ? 1 : 0);
                    bDigit.fontSize = Mathf.RoundToInt(70 * k);
                    BannerDigit(hs, left, goalTeam == Team.Home && flip ? t - 0.75f : -1, s2 * fade);
                    BannerDigit(aw, right, goalTeam == Team.Away && flip ? t - 0.75f : -1, s2 * fade);
                    bDigit.normal.textColor = new Color(1, 1, 1, 0.8f * s2 * fade);
                    GUI.Label(new Rect(plate.x, plate.y - 4 * k, pw, ph), ":", bDigit);
                    // кто забил
                    if (goalScorer >= 0 && goalScorer < match.Players.Count)
                    {
                        var sp = match.Players[goalScorer];
                        int hi = System.Array.IndexOf(HeroList, sp.Hero);
                        if (hi >= 0)
                        {
                            float s3 = EaseOut((t - 0.5f) / 0.3f);
                            bSmall.fontSize = Mathf.RoundToInt(30 * k);
                            bSmall.normal.textColor = new Color(1, 1, 1, 0.85f * s3 * fade);
                            GUI.Label(new Rect(0, plate.yMax + 8 * k, w, 40 * k), "ЗАБИЛ  " + HeroNamesUp[hi], bSmall);
                        }
                    }
                }
            }
            GUI.color = old;
        }

        void BannerDigit(int v, Rect r, float popT, float alpha)
        {
            var old = GUI.matrix;
            if (popT >= 0) { float kk = 1 + 0.6f * Mathf.Exp(-6 * popT) * Mathf.Cos(11 * popT); GUIUtility.ScaleAroundPivot(Vector2.one * kk, r.center); }
            bDigit.normal.textColor = new Color(0, 0, 0, 0.5f * alpha);
            GUI.Label(new Rect(r.x + 2, r.y + 4, r.width, r.height), v.ToString(), bDigit);
            bDigit.normal.textColor = new Color(1, 1, 1, alpha);
            GUI.Label(r, v.ToString(), bDigit);
            GUI.matrix = old;
        }
    }
}
