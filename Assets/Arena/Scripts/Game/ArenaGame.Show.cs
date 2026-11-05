// Шоу вокруг матча: выход команд перед стартом, золотой гол, экран лучшего игрока (MVP) после матча.
// Само происходящее на поле считает симуляция (Match: Intro, Golden, статистика) — здесь камера и надписи.
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public partial class ArenaGame
    {
        static readonly Color GoldCol = new Color(1f, 0.78f, 0.25f);
        float endT0 = -10; bool wasEnded;

        /// <summary>Каждый кадр: запомнить момент конца матча (от него идут камера и появление итогов).</summary>
        void TrackShowState()
        {
            bool ended = state == State.Ended;
            if (ended && !wasEnded) endT0 = uiTime;
            wasEnded = ended;
        }

        // ---------- камера ----------
        bool ShowCamOn => ShowCam(out _, out _, out _);

        /// <summary>Камера для выхода команд и MVP: куда смотреть, с какого расстояния и под каким наклоном.</summary>
        bool ShowCam(out Vector3 focus, out float dist, out float tilt)
        {
            focus = Vector3.zero; dist = cameraDistance; tilt = cameraTilt;
            if (CineOn) return false;
            if (state == State.Playing && match.Intro > 0)
            {
                int st = match.IntroStage; float e = match.IntroStageTime;
                var line = W.Pos(Field.FW * 0.42f, Field.FH / 2, 60);
                if (st == 0)
                {   // герои выбегают из туннеля: камера едет от туннеля к линии
                    float k = Mathf.SmoothStep(0, 1, e / Mathf.Max(0.1f, match.T.introRun));
                    focus = Vector3.Lerp(W.Pos(120, Field.FH / 2, 40), line, k);
                    dist = Mathf.Lerp(12, 8, k); tilt = Mathf.Lerp(40, 22, k);
                    return true;
                }
                if (st == 1)
                {   // стоят в линию: камера низко и близко проезжает вдоль строя слева направо
                    float k = Mathf.SmoothStep(0, 1, e / Mathf.Max(0.1f, match.T.introPose));
                    float y = Mathf.Lerp(Field.FH / 2 + 330, Field.FH / 2 - 330, k);
                    focus = W.Pos(Field.FW * 0.42f, y, 95);
                    dist = 6.2f; tilt = 15;
                    return true;
                }
                return false; // разбегаются на разводку — обычная камера плавно возвращается
            }
            if (state == State.Ended)
            {
                var mvp = match.Mvp();
                float t = uiTime - endT0;
                if (mvp == null || t < 0.6f) return false;
                focus = W.Pos(mvp.X, mvp.Y, 85);
                float hl = Field.FH / 2 * W.S, hw = Field.FW / 2 * W.S;
                focus.x = Mathf.Clamp(focus.x, -hl + 1, hl - 1); focus.z = Mathf.Clamp(focus.z, -hw + 0.5f, hw - 0.5f);
                float k = Mathf.SmoothStep(0, 1, (t - 0.6f) / 3f);
                dist = Mathf.Lerp(7.5f, 4.4f, k); tilt = Mathf.Lerp(30, 16, k);
                return true;
            }
            return false;
        }

        // ---------- кто это ----------
        static string HeroName(Hero h) => HeroNames[(int)h];

        /// <summary>Подпись игрока: имя в онлайне, «Ты» офлайн, иначе «бот».</summary>
        string Who(SimPlayer p)
        {
            if (!p.IsHuman) return "бот";
            if (net != null)
            {
                var s = net.Roster.Find(x => x.Slot == p.HumanSlot);
                string n = s != null && !string.IsNullOrWhiteSpace(s.Name) ? s.Name : "игрок";
                return p == match.Local ? n + " (ты)" : n;
            }
            return p == match.Local ? "Ты" : "игрок";
        }

        GUIStyle tagStyle, statStyle, cardTitle;
        void ShowStyles()
        {
            if (tagStyle != null) return;
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            statStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            cardTitle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        }

        // ---------- выход команд ----------
        void IntroGui(float w, float h)
        {
            ShowStyles();
            int st = match.IntroStage; float e = match.IntroStageTime;
            // заголовок: «СИНИЕ vs КРАСНЫЕ» и формат
            float a = st == 2 ? Mathf.Clamp01(1 - e / 0.4f) : Mathf.Clamp01((st == 0 ? e : 1) / 0.5f);
            if (a > 0)
            {
                var old = GUI.color;
                float y = h * 0.06f;
                string mode = match.Mode == GameMode.OneVsOne ? "1 на 1" : match.Mode == GameMode.TwoVsTwo ? "2 на 2" : "4 на 4";
                Shadowed(new Rect(0, y, w / 2 - 60, 60), "СИНИЕ", big.Right(), new Color(Mat.Home.r, Mat.Home.g, Mat.Home.b, a));
                Shadowed(new Rect(w / 2 - 60, y, 120, 60), "vs", mid, new Color(1, 1, 1, a));
                Shadowed(new Rect(w / 2 + 60, y, w / 2 - 60, 60), "КРАСНЫЕ", big.Left(), new Color(Mat.Away.r, Mat.Away.g, Mat.Away.b, a));
                Shadowed(new Rect(0, y + 58, w, 30), mode, mid.Small(), new Color(0.85f, 0.92f, 0.9f, a * 0.9f));
                GUI.color = old;
            }
            // в строю: имя героя и игрока над головой
            if (st == 1 && cam != null)
            {
                float ta = Mathf.Clamp01((e - 0.3f) / 0.4f) * Mathf.Clamp01((match.T.introPose - e) / 0.3f);
                foreach (var p in match.Players)
                {
                    var sp = cam.WorldToScreenPoint(W.Pos(p.X, p.Y, 215));
                    if (sp.z <= 0 || sp.x < -100 || sp.x > w + 100) continue;
                    float x = sp.x, y = h - sp.y;
                    Color tc = p.Team == Team.Home ? Mat.Home : Mat.Away;
                    Shadowed(new Rect(x - 110, y - 46, 220, 26), HeroName(p.Hero), tagStyle, new Color(1, 1, 1, ta));
                    Shadowed(new Rect(x - 110, y - 22, 220, 22), Who(p), small.Center(), new Color(tc.r * 0.6f + 0.4f, tc.g * 0.6f + 0.4f, tc.b * 0.6f + 0.4f, ta));
                }
            }
            if (net == null)
            {
                GUI.color = new Color(1, 1, 1, 0.55f);
                GUI.Label(new Rect(24, h - 34, w * 0.5f, 24), (input.UsingGamepad ? "A" : "Enter") + " — пропустить", small);
                GUI.color = Color.white;
            }
        }

        // ---------- золотой гол ----------
        /// <summary>Перед разводкой золотого гола: крупная надпись.</summary>
        void GoldenBanner(float w, float h)
        {
            float t = match.T.goldenPause - match.Freeze;
            float s = Spring(t);
            var r = new Rect(0, h * 0.3f, w, 100);
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Max(0.01f, s), r.center);
            Shadowed(r, "ЗОЛОТОЙ ГОЛ", banner, Color.Lerp(GoldCol, Color.white, 0.25f + 0.25f * Mathf.Sin(uiTime * 6)));
            GUI.matrix = Matrix4x4.identity;
            float a = Mathf.Clamp01((t - 0.35f) / 0.3f);
            Shadowed(new Rect(0, h * 0.3f + 96, w, 36), "Ничья! Кто забьёт — тот победил", mid, new Color(1, 1, 1, a));
        }

        /// <summary>Пока идёт золотой гол: метка вверху по центру.</summary>
        void GoldenTag(float w, float h)
        {
            float p = 0.75f + 0.25f * Mathf.Sin(uiTime * 4);
            Shadowed(new Rect(0, 12, w, 34), "ЗОЛОТОЙ ГОЛ", mid, new Color(GoldCol.r, GoldCol.g, GoldCol.b, p));
        }

        // ---------- итоги матча и MVP ----------
        string EndedHint() =>
            net == null ? "A / Enter — ещё матч     B / Backspace — в меню"
            : IsHostOnline || net.IsLeader ? "A / Enter — ещё матч (для всех)     B / Backspace — выйти из комнаты"
            : net.ViaServer ? "Ждём, пока главный начнёт новый матч…     B / Backspace — выйти" : "Ждём, пока хост начнёт новый матч…     B / Backspace — выйти";

        static string Stats(SimPlayer p) => "Голы " + p.Goals + "  ·  Передачи " + p.Assists + "  ·  Сейвы " + p.Saves + "  ·  Отборы " + p.Steals;

        void MvpGui(float w, float h, string hint)
        {
            ShowStyles();
            var mvp = match.Mvp();
            float t = uiTime - endT0;
            float a = Mathf.Clamp01((t - 0.9f) / 0.5f);
            bool away = match.Local.Team == Team.Away; int mine = away ? match.ScoreAway : match.ScoreHome, theirs = away ? match.ScoreHome : match.ScoreAway;
            string title = mine > theirs ? "ПОБЕДА" : mine < theirs ? "ПОРАЖЕНИЕ" : "НИЧЬЯ";
            Color titleCol = mine > theirs ? Mat.Accent : mine < theirs ? new Color(0.85f, 0.88f, 0.9f) : Color.white;

            // затемнение снизу и сверху, середина — герой
            GUI.color = new Color(0, 0, 0, 0.45f * Mathf.Clamp01(t / 0.6f));
            GUI.DrawTexture(new Rect(0, 0, w, h * 0.17f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, h * 0.70f, w, h * 0.30f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float ts = Spring(t);
            var tr = new Rect(0, h * 0.025f, w, 64);
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Max(0.01f, ts), tr.center);
            Shadowed(tr, title + "   " + mine + " : " + theirs, big, titleCol);
            GUI.matrix = Matrix4x4.identity;
            if (match.Golden && match.ScoreHome != match.ScoreAway) Shadowed(new Rect(0, h * 0.025f + 60, w, 28), "золотым голом", mid.Small(), new Color(GoldCol.r, GoldCol.g, GoldCol.b, ts));
            if (a <= 0 || mvp == null) { Hint(w, h, hint, 1); return; }

            // карточка лучшего игрока — слева, герой в центре кадра
            float cx = w * 0.06f, cy = h * 0.27f, slide = (1 - Mathf.SmoothStep(0, 1, a)) * -60;
            var oldC = GUI.color;
            Shadowed(new Rect(cx + slide, cy, 520, 30), "ЛУЧШИЙ ИГРОК МАТЧА", mid.Left(), new Color(GoldCol.r, GoldCol.g, GoldCol.b, a));
            Color tc = mvp.Team == Team.Home ? Mat.Home : Mat.Away;
            Shadowed(new Rect(cx + slide, cy + 30, 600, 70), HeroName(mvp.Hero).ToUpperInvariant(), cardTitle, new Color(1, 1, 1, a));
            Shadowed(new Rect(cx + slide, cy + 98, 600, 30), Who(mvp) + "   ·   " + (mvp.Team == Team.Home ? "синие" : "красные"), mid.Left(), new Color(tc.r * 0.6f + 0.4f, tc.g * 0.6f + 0.4f, tc.b * 0.6f + 0.4f, a));
            Shadowed(new Rect(cx + slide, cy + 134, 700, 28), Stats(mvp), statStyle, new Color(0.9f, 0.95f, 0.93f, a));
            GUI.color = oldC;

            // таблица всех игроков: синие слева, красные справа
            float b = Mathf.Clamp01((t - 1.6f) / 0.5f);
            if (b > 0)
            {
                float colW = Mathf.Min(560, w * 0.42f), y0 = h * 0.72f;
                StatsColumn(Team.Home, w / 2 - colW - 20, y0, colW, b, mvp);
                StatsColumn(Team.Away, w / 2 + 20, y0, colW, b, mvp);
            }
            Hint(w, h, hint, a);
        }

        void StatsColumn(Team team, float x, float y, float cw, float a, SimPlayer mvp)
        {
            Color tc = team == Team.Home ? Mat.Home : Mat.Away;
            Shadowed(new Rect(x, y, cw, 26), team == Team.Home ? "СИНИЕ" : "КРАСНЫЕ", statStyle, new Color(tc.r, tc.g, tc.b, a));
            var hs = new GUIStyle(statStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            string[] heads = { "Г", "П", "С", "О" };
            for (int i = 0; i < 4; i++) Shadowed(new Rect(x + cw * 0.6f + i * cw * 0.1f, y, cw * 0.1f, 26), heads[i], hs, new Color(0.75f, 0.82f, 0.8f, a));
            y += 26;
            foreach (var p in match.Players)
            {
                if (p.Team != team || p.Dummy) continue;
                bool star = p == mvp;
                Color c = star ? new Color(GoldCol.r, GoldCol.g, GoldCol.b, a) : new Color(1, 1, 1, a * (p == match.Local ? 1 : 0.85f));
                Shadowed(new Rect(x, y, cw * 0.6f, 24), (star ? "★ " : "") + HeroName(p.Hero) + "  ·  " + Who(p), statStyle, c);
                int[] v = { p.Goals, p.Assists, p.Saves, p.Steals };
                var vs = new GUIStyle(statStyle) { alignment = TextAnchor.MiddleCenter };
                for (int i = 0; i < 4; i++) Shadowed(new Rect(x + cw * 0.6f + i * cw * 0.1f, y, cw * 0.1f, 24), v[i].ToString(), vs, c);
                y += 24;
            }
        }

        void Hint(float w, float h, string hint, float a)
        {
            Shadowed(new Rect(0, h - 40, w, 30), hint, mid.Small(), new Color(0.8f, 0.9f, 0.86f, a));
        }
    }
}
