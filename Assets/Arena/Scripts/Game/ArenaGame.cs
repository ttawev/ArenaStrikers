// Entry point of the grey-box version: menu, fixed-step simulation, camera, HUD.
// Put this component on one empty object in the scene (Arena → Собрать сцену does it for you).
using Arena.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arena.Game
{
    public class ArenaGame : MonoBehaviour
    {
        [Tooltip("Все цифры механики. Можно менять прямо во время игры.")]
        public Tuning tuning = new Tuning();

        [Header("Камера")]
        [Range(30, 80)] public float cameraTilt = 52;
        [Range(8, 30)] public float cameraDistance = 15;
        [Range(20, 60)] public float cameraFov = 38;

        [Header("Отклик удара")]
        [Tooltip("Длительность заморозки кадра при сильном ударе, множитель")] [Range(0, 3)] public float hitstopScale = 1;
        [Tooltip("Тряска камеры, множитель")] [Range(0, 3)] public float shakeScale = 1;
        [Tooltip("Рывок камеры (приближение) при ударе, градусы")] [Range(0, 8)] public float fovKick = 3;

        [Header("Модели героев")]
        public HeroArtTuning heroArt = new HeroArtTuning();

        enum State { Menu, Playing, Paused, Ended }
        State state = State.Menu;
        Match match;
        WorldView view;
        readonly LocalInput input = new LocalInput();
        Camera cam;
        Vector3 camPos;
        float shake, acc, simTime;
        const float DT = 1f / 120;
        readonly InputCommand[] cmds = new InputCommand[1];
        InputCommand lastCmd;

        // menu selections
        static readonly Hero[] HeroList = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };
        static readonly string[] HeroNames = { "Жар", "Багор", "Миг", "Вихрь", "Тень", "Стена" };
        static readonly string[] HeroAbility = { "Огненный суперудар", "Крюк", "Телепорт", "Кручёный удар", "Невидимость", "Ледяной разлом" };
        static readonly GameMode[] ModeList = { GameMode.OneVsOne, GameMode.TwoVsTwo, GameMode.FourVsFour, GameMode.Training };
        static readonly string[] ModeNames = { "1 на 1", "2 на 2", "4 на 4", "Тренировка" };
        static readonly string[] DiffNames = { "Лёгкий", "Средний", "Сложный" };
        int heroSel, modeSel = 2, diffSel = 1;
        float denyFlash, hitstop, fovPunch;
        AudioSource audioSrc;
        AudioClip receiveClip;

        void Awake()
        {
            RuntimeLog.Start();
            Application.targetFrameRate = 144;
            heroSel = PlayerPrefs.GetInt("arena.hero", 0); modeSel = PlayerPrefs.GetInt("arena.mode", 2); diffSel = PlayerPrefs.GetInt("arena.diff", 1);
            cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera") { tag = "MainCamera" }; cam = go.AddComponent<Camera>(); go.AddComponent<AudioListener>(); }
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Mat.Night; cam.nearClipPlane = 0.1f; cam.farClipPlane = 120;
            view = new WorldView(transform);
            audioSrc = gameObject.AddComponent<AudioSource>(); audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0;
            receiveClip = SoundFx.Receive();
            match = new Match(tuning, System.Environment.TickCount);
            match.Setup(ModeList[modeSel], HeroList[heroSel], diffSel); // something to look at behind the menu
            view.Rebuild(match);
            camPos = TargetCamPos(true);
        }

        void StartMatch()
        {
            PlayerPrefs.SetInt("arena.hero", heroSel); PlayerPrefs.SetInt("arena.mode", modeSel); PlayerPrefs.SetInt("arena.diff", diffSel);
            match.T = tuning;
            match.Setup(ModeList[modeSel], HeroList[heroSel], diffSel);
            view.Rebuild(match);
            input.ClearPending(); acc = 0;
            state = State.Playing;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            MenuInput();
            if (state == State.Playing)
            {
                input.Poll(match.Local.Hero);
                if (hitstop > 0) hitstop -= dt; // freeze-frame on strong hits: the sim waits, input is kept
                else acc += dt;
                while (acc >= DT)
                {
                    cmds[0] = input.Take(match.Local.Hero); lastCmd = cmds[0];
                    match.Step(DT, cmds);
                    simTime += DT; acc -= DT;
                    HandleEvents();
                    if (match.Phase == MatchPhase.Ended) { state = State.Ended; input.StopRumble(); break; }
                }
            }
            else match.Events.Clear();
            input.UpdateRumble(dt);
            denyFlash = Mathf.Max(0, denyFlash - dt);
            HeroModel.Tune = heroArt;
            HeroModel.FrameDt = state == State.Paused || hitstop > 0 ? 0 : dt;
            view.Sync(match, Time.time, dt, state == State.Playing ? lastCmd : default);
            UpdateCamera(dt);
        }

        void HandleEvents()
        {
            foreach (var e in match.Events)
            {
                switch (e.Kind)
                {
                    case FxKind.Burst: view.Burst(e.X, e.Y, Mat.Of(e.Color), e.Count, e.Amount); break;
                    case FxKind.Shake: shake = Mathf.Max(shake, e.Amount); break;
                    case FxKind.Goal: view.Burst(e.X, e.Y, e.Team == Team.Home ? Mat.Home : Mat.Away, 60, 1.6f); break;
                    case FxKind.Rumble: if (e.Player == match.Local) input.Rumble(e.Amount); break;
                    case FxKind.Denied: if (e.Player == match.Local) denyFlash = 0.25f; break;
                    case FxKind.Impact:
                        view.Impact(e.X, e.Y, e.Amount);
                        if (e.Amount >= 0.5f) { hitstop = Mathf.Max(hitstop, (0.045f + 0.075f * Mathf.Min(e.Amount, 1.4f)) * hitstopScale); acc = 0; }
                        shake = Mathf.Max(shake, e.Amount * 10 * shakeScale);
                        fovPunch = Mathf.Max(fovPunch, Mathf.Clamp01(e.Amount) * fovKick);
                        view.Burst(e.X, e.Y, Mat.Mist, (int)(6 + 10 * e.Amount), 0.6f + 0.6f * e.Amount);
                        break;
                    case FxKind.Receive:
                        audioSrc.pitch = 0.92f + Random.value * 0.16f;
                        audioSrc.PlayOneShot(receiveClip, e.Player == match.Local ? 0.9f : 0.45f);
                        break;
                }
            }
            match.Events.Clear();
        }

        // ---------- menus: keyboard, gamepad, mouse ----------
        void MenuInput()
        {
            var gp = Gamepad.current; var kb = Keyboard.current;
            bool confirm = (gp != null && gp.buttonSouth.wasPressedThisFrame) || (kb != null && kb.enterKey.wasPressedThisFrame);
            bool back = (gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && kb.backspaceKey.wasPressedThisFrame);
            bool pause = (gp != null && gp.startButton.wasPressedThisFrame) || (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame));
            switch (state)
            {
                case State.Menu:
                    if (gp != null)
                    {
                        if (gp.dpad.left.wasPressedThisFrame) heroSel = (heroSel + HeroList.Length - 1) % HeroList.Length;
                        if (gp.dpad.right.wasPressedThisFrame) heroSel = (heroSel + 1) % HeroList.Length;
                        if (gp.dpad.up.wasPressedThisFrame) modeSel = (modeSel + 3) % 4;
                        if (gp.dpad.down.wasPressedThisFrame) modeSel = (modeSel + 1) % 4;
                        if (gp.leftShoulder.wasPressedThisFrame) diffSel = Mathf.Max(0, diffSel - 1);
                        if (gp.rightShoulder.wasPressedThisFrame) diffSel = Mathf.Min(2, diffSel + 1);
                    }
                    if (confirm || (gp != null && gp.startButton.wasPressedThisFrame)) StartMatch();
                    break;
                case State.Playing:
                    if (pause) { state = State.Paused; input.StopRumble(); }
                    break;
                case State.Paused:
                    if (pause || confirm) { state = State.Playing; input.ClearPending(); acc = 0; }
                    else if (back) state = State.Menu;
                    break;
                case State.Ended:
                    if (confirm) StartMatch();
                    else if (back) state = State.Menu;
                    break;
            }
        }

        // ---------- camera ----------
        Vector3 TargetCamPos(bool snap)
        {
            var you = match.Local; var b = match.Ball;
            float tx = you.X * 0.6f + b.X * 0.4f, ty = you.Y * 0.6f + b.Y * 0.4f;
            Vector3 focus = W.Pos(tx, ty);
            // keep the view over the pitch
            float halfLen = Field.FH / 2 * W.S, halfWid = Field.FW / 2 * W.S;
            focus.x = Mathf.Clamp(focus.x, -halfLen + 5, halfLen - 5);
            focus.z = Mathf.Clamp(focus.z, -halfWid + 3, halfWid - 3);
            var rot = Quaternion.Euler(cameraTilt, 0, 0);
            return focus - rot * Vector3.forward * cameraDistance;
        }

        void UpdateCamera(float dt)
        {
            var target = TargetCamPos(false);
            camPos = Vector3.Lerp(camPos, target, 1 - Mathf.Exp(-5 * dt));
            fovPunch = Mathf.Max(0, fovPunch - dt * 18);
            cam.fieldOfView = cameraFov - fovPunch;
            cam.transform.rotation = Quaternion.Euler(cameraTilt, 0, 0);
            Vector3 jitter = shake > 0 ? new Vector3(Random.value - 0.5f, 0, Random.value - 0.5f) * shake * 4f * W.S : Vector3.zero;
            cam.transform.position = camPos + jitter;
            shake = Mathf.Max(0, shake - dt * 30);
        }

        // ---------- HUD (temporary, IMGUI) ----------
        GUIStyle big, mid, small, banner;
        void Styles()
        {
            if (big != null) return;
            big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = true };
            banner = new GUIStyle(GUI.skin.label) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        void Shadowed(Rect r, string text, GUIStyle st, Color c)
        {
            var old = GUI.color; GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.Label(new Rect(r.x + 2, r.y + 3, r.width, r.height), text, st);
            GUI.color = c; GUI.Label(r, text, st); GUI.color = old;
        }

        void OnGUI()
        {
            Styles();
            float w = Screen.width, h = Screen.height;
            if (state == State.Playing || state == State.Paused || state == State.Ended) Hud(w, h);
            if (state == State.Menu) MenuGui(w, h);
            if (state == State.Paused) Panel(w, h, "Пауза", "A / Enter — продолжить     B / Backspace — в меню");
            if (state == State.Ended)
            {
                string title = match.ScoreHome > match.ScoreAway ? "Победа" : match.ScoreHome < match.ScoreAway ? "Поражение" : "Ничья";
                Panel(w, h, title + "   " + match.ScoreHome + " : " + match.ScoreAway, "A / Enter — ещё матч     B / Backspace — в меню");
            }
        }

        void Hud(float w, float h)
        {
            bool training = match.Mode == GameMode.Training;
            string score = training ? "Голы: " + match.ScoreHome : (match.Mode == GameMode.OneVsOne ? "Ты " : "Мы ") + match.ScoreHome + " : " + match.ScoreAway + (match.Mode == GameMode.OneVsOne ? " Бот" : " Они");
            Shadowed(new Rect(0, 12, w, 40), score, mid, Color.white);
            if (!training)
            {
                int c = Mathf.CeilToInt(match.Clock);
                Shadowed(new Rect(0, 46, w, 30), (c / 60) + ":" + (c % 60).ToString("00"), small.Center(), new Color(0.75f, 0.85f, 0.82f));
            }
            if (match.Freeze > 0 && state == State.Playing)
            {
                string text = match.Banner != "" ? match.Banner : "ВПЕРЁД";
                Color col = match.Banner == "" ? Mat.Chalk : match.BannerTeam == Team.Home ? Mat.Home : Mat.Away;
                Shadowed(new Rect(0, h * 0.35f, w, 100), text, match.Banner != "" ? banner : big, col);
            }
            ButtonsHud(w, h, training);
        }

        // ---------- action buttons, bottom right: laid out like the gamepad's face buttons ----------
        static Texture2D disc, ringTex;
        static Texture2D MakeCircle(bool ring)
        {
            const int N = 128; var t = new Texture2D(N, N, TextureFormat.RGBA32, false); var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f));
                    float a = Mathf.Clamp01(N / 2f - d);
                    if (ring) a *= Mathf.Clamp01(d - (N / 2f - 7));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(); t.hideFlags = HideFlags.HideAndDontSave; return t;
        }

        void ActionButton(float cx, float cy, float size, string key, string label, float fill, Color col, bool active)
        {
            if (disc == null) disc = MakeCircle(false);
            if (ringTex == null) ringTex = MakeCircle(true);
            var r = new Rect(cx - size / 2, cy - size / 2, size, size);
            fill = Mathf.Clamp01(fill);
            bool ready = fill >= 1;
            GUI.color = new Color(0.03f, 0.08f, 0.1f, 0.78f); GUI.DrawTexture(r, disc);
            // fill rises from the bottom as the cooldown (or charge) completes
            var fc = ready ? col : new Color(col.r * 0.55f, col.g * 0.55f, col.b * 0.55f, 0.9f);
            GUI.color = fc;
            float fh = size * fill;
            GUI.BeginGroup(new Rect(r.x, r.y + size - fh, size, fh));
            GUI.DrawTexture(new Rect(0, fh - size, size, size), disc);
            GUI.EndGroup();
            GUI.color = active ? Color.white : ready ? col : new Color(1, 1, 1, 0.25f); GUI.DrawTexture(r, ringTex);
            GUI.color = Color.white;
            var ls = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
            ls.normal.textColor = ready ? Mat.Night : Color.white;
            GUI.Label(r, label, ls);
            var ks = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            GUI.color = new Color(0, 0, 0, 0.85f); GUI.DrawTexture(new Rect(cx - 12, r.y - 10, 24, 20), disc);
            GUI.color = Color.white; GUI.Label(new Rect(cx - 12, r.y - 10, 24, 20), key, ks);
        }

        void ButtonsHud(float w, float h, bool training)
        {
            var you = match.Local; var T = match.T;
            bool pad = input.UsingGamepad;
            float size = Mathf.Clamp(h * 0.075f, 56, 84), gap = size * 1.05f;
            float cx = w - gap * 1.7f - 20, cy = h - gap * 1.6f - 20;
            Color heroCol = you.Hero == Hero.Fire ? Mat.Fire : you.Hero == Hero.Hook ? Mat.Hex("#e2c48a") : you.Hero == Hero.Teleport ? Mat.Blink : you.Hero == Hero.Shade ? Mat.Hex("#b48cff") : you.Hero == Hero.Wall ? Mat.Hex("#9fe8ff") : Mat.Spin;
            if (denyFlash > 0) heroCol = Mat.Away;

            // A — kick (fill = charge while held), X — lob, B — dash, Y — hero ability
            ActionButton(cx, cy + gap, size, pad ? "A" : "J", "Удар", you.Charging ? you.Charge : (you.KickCd > 0 ? 0.6f : 1), Mat.Home, you.Charging);
            ActionButton(cx - gap, cy, size, pad ? "X" : "L", "Навес", you.Lobbing ? you.LobCharge : 1, Mat.Hex("#c8ef7a"), you.Lobbing);
            ActionButton(cx + gap, cy, size, pad ? "B" : "K", "Рывок", 1 - you.DashCd / T.dashCd, Mat.Chalk, you.DashT > 0);
            switch (you.Hero)
            {
                case Hero.Fire: ActionButton(cx, cy - gap, size, pad ? "Y" : "U", you.FireT > 0 ? "Замах!" : "Огонь", you.FireT > 0 ? 1 : 1 - you.FireCd / T.fireCd, heroCol, you.FireT > 0); break;
                case Hero.Hook: ActionButton(cx, cy - gap, size, pad ? "Y" : "U", "Крюк", 1 - you.HookCd / T.hookCd, heroCol, match.Hooks.Exists(k => k.Owner == you)); break;
                case Hero.Teleport:
                    ActionButton(cx, cy - gap, size, pad ? "Y" : "U", you.TpBackT > 0 ? "Назад!" : "Телепорт", you.TpBackT > 0 ? 1 : 1 - you.TpCd / T.tpCd, you.TpBackT > 0 ? Mat.Hex("#c9f3ff") : heroCol, you.TpBackT > 0); break;
                case Hero.Wall:
                    ActionButton(cx, cy - gap, size, pad ? "Y" : "U", "Разлом", 1 - you.WallCd / T.wallCd, heroCol, match.Walls.Exists(k => k.Owner == you)); break;
                case Hero.Shade:
                    ActionButton(cx, cy - gap, size, pad ? "Y" : "U", you.Invisible ? "Невидим" : "Тень", you.Invisible ? you.InvisT / T.invisDur : 1 - you.InvisCd / T.invisCd, heroCol, you.Invisible); break;
                case Hero.Curve:
                    float cf = 1 - you.CurveCd / T.curveCd;
                    ActionButton(cx - gap * 0.55f, cy - gap * 1.05f, size * 0.85f, pad ? "LB" : "U", "↶ Влево", you.Curving < 0 ? you.CurveCharge : cf, heroCol, you.Curving < 0);
                    ActionButton(cx + gap * 0.55f, cy - gap * 1.05f, size * 0.85f, pad ? "RB" : "I", "Вправо ↷", you.Curving > 0 ? you.CurveCharge : cf, heroCol, you.Curving > 0);
                    break;
            }
            // small hint line, bottom left
            string hint = (pad ? "Start — пауза" : "WASD — бег · Esc — пауза") + (training ? (pad ? " · Back — мяч ко мне" : " · R — мяч ко мне") : "");
            GUI.color = new Color(1, 1, 1, 0.55f); GUI.Label(new Rect(24, h - 34, w * 0.5f, 24), hint, small); GUI.color = Color.white;
        }

        void Bar(ref float y, float x, string label, float k)
        {
            k = Mathf.Clamp01(k);
            Color full = denyFlash > 0 ? Mat.Away : Mat.Home;
            GUI.Label(new Rect(x, y, 200, 22), label, small);
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(new Rect(x + 150, y + 6, 160, 10), Texture2D.whiteTexture);
            GUI.color = k >= 1 ? full : new Color(0.6f, 0.7f, 0.68f); GUI.DrawTexture(new Rect(x + 150, y + 6, 160 * k, 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += 26;
        }

        void Panel(float w, float h, string title, string hint)
        {
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture); GUI.color = Color.white;
            Shadowed(new Rect(0, h * 0.38f, w, 80), title, big, Color.white);
            Shadowed(new Rect(0, h * 0.38f + 80, w, 40), hint, mid.Small(), new Color(0.8f, 0.9f, 0.86f));
        }

        void MenuGui(float w, float h)
        {
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture); GUI.color = Color.white;
            float cw = Mathf.Min(720, w - 40), x = (w - cw) / 2, y = Mathf.Max(30, h * 0.12f);
            Shadowed(new Rect(0, y, w, 60), "АРЕНА СТРАЙКЕРОВ", big, Mat.Home); y += 70;
            Shadowed(new Rect(0, y, w, 24), "серая коробка · этап 1", small.Center(), new Color(0.6f, 0.7f, 0.68f)); y += 44;

            GUI.Label(new Rect(x, y, cw, 24), "Герой  (крестовина ← →)", small); y += 26;
            int hn = HeroList.Length;
            for (int i = 0; i < hn; i++)
                if (Toggle(new Rect(x + i * (cw / hn), y, cw / hn - 8, 54), heroSel == i, HeroNames[i] + "\n" + HeroAbility[i])) heroSel = i;
            y += 70;
            GUI.Label(new Rect(x, y, cw, 24), "Формат  (крестовина ↑ ↓)", small); y += 26;
            for (int i = 0; i < 4; i++)
                if (Toggle(new Rect(x + i * (cw / 4), y, cw / 4 - 8, 36), modeSel == i, ModeNames[i])) modeSel = i;
            y += 52;
            GUI.Label(new Rect(x, y, cw, 24), "Сложность соперников  (LB / RB)", small); y += 26;
            for (int i = 0; i < 3; i++)
                if (Toggle(new Rect(x + i * (cw / 3), y, cw / 3 - 8, 36), diffSel == i, DiffNames[i])) diffSel = i;
            y += 60;
            if (GUI.Button(new Rect(x, y, cw, 50), "Играть  (A / Enter)")) StartMatch();
            y += 70;
            GUI.color = new Color(1, 1, 1, 0.65f);
            GUI.Label(new Rect(x, y, cw, 80), "Геймпад: стик — бег, A — удар (держи), X — навес (держи), B — рывок, Y — способность, у Вихря LB/RB — кручёный, Start — пауза.\nКлавиатура: WASD, J удар, L навес, K рывок, U способность (у Вихря U/I), Esc пауза.", small);
            GUI.color = Color.white;
        }

        static bool Toggle(Rect r, bool on, string text)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = on ? Mat.Home : new Color(0.3f, 0.4f, 0.4f);
            bool clicked = GUI.Button(r, text, new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = 13 });
            GUI.backgroundColor = old;
            return clicked;
        }

        void OnDisable() => input.StopRumble();
    }

    /// <summary>Editor play sessions: errors and warnings go to Logs/arena-runtime.log so they can be read without the Console.</summary>
    static class RuntimeLog
    {
        static string path;
        public static void Start()
        {
#if UNITY_EDITOR
            path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "arena-runtime.log");
            try { System.IO.File.WriteAllText(path, "play started " + System.DateTime.Now.ToString("HH:mm:ss") + "\n"); } catch { }
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
#endif
        }
        static void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log || path == null) return;
            try { System.IO.File.AppendAllText(path, type + ": " + msg + "\n" + (type == LogType.Exception || type == LogType.Error ? stack + "\n" : "")); } catch { }
        }
    }

    static class GuiStyleExt
    {
        public static GUIStyle Center(this GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.MiddleCenter };
        public static GUIStyle Small(this GUIStyle s) => new GUIStyle(s) { fontSize = 16, fontStyle = FontStyle.Normal };
    }
}
