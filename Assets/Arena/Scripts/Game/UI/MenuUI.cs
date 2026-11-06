// Меню игры на UI Toolkit, собирается кодом: главный экран, выбор героя (модель героя стоит на подиуме на поле),
// онлайн (имя, создать комнату, войти по коду) и комната. Стили — Resources/UI/menu.uss, шрифты — Resources/UI/Fonts.
// Управление одинаковое для геймпада, клавиатуры и мыши: ↑↓ — пункт, ←→ — значение, A/Enter — выбрать, B/Esc — назад.
using System;
using System.Collections.Generic;
using Arena.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Arena.Game
{
    /// <summary>Что меню просит у игры. Реализует ArenaGame.</summary>
    public interface IMenuHost
    {
        int HeroIndex { get; set; }
        int ModeIndex { get; set; }   // 0 — 1 на 1, 1 — 2 на 2, 2 — 3 на 3, 3 — 4 на 4, 4 — тренировка
        int DiffIndex { get; set; }
        int TimeIndex { get; set; }   // длительность матча: Match.TimeOptions
        int GoalsIndex { get; set; }  // голов до победы: Match.GoalOptions
        string PlayerName { get; set; }
        string JoinCode { get; set; }
        string ServerAddress { get; set; }   // адрес своего выделенного сервера (IP или IP:порт)
        void JoinServer();
        string NetError { get; }
        Online Net { get; }
        Team RoomTeam { get; }
        void StartOffline();
        void CreateRoom();
        void JoinRoom();
        void LeaveRoom();
        void SetRoomHero(int heroIndex);
        void SetRoomTeam(Team team);
        void SetRoomRules(GameMode mode, int difficulty);
        void StartRoomMatch();
        void Quit();
    }

    public class MenuUI
    {
        public enum Screen { None, Main, Heroes, Online, Room, Settings }
        public Screen Current { get; private set; } = Screen.None;
        /// <summary>На этом экране камера смотрит на героя на подиуме.</summary>
        public bool Showcase => Current == Screen.Heroes || (Current == Screen.Room && host.Net != null && host.Net.State != Online.Phase.Connecting);

        // порядок как в ArenaGame.HeroList
        public static readonly Hero[] HeroOrder = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };
        static readonly string[] HNameRu = { "Жар", "Багор", "Миг", "Вихрь", "Тень", "Стена" };
        static readonly string[] HNameEn = { "Blaze", "Gaff", "Blink", "Whirl", "Shade", "Wall" };
        static readonly string[] HRoleRu = { "Удар", "Контроль мяча", "Перемещение", "Пас", "Скрытность", "Защита ворот" };
        static readonly string[] HRoleEn = { "Shooting", "Ball control", "Mobility", "Passing", "Stealth", "Goal defense" };
        static readonly string[] HAbilityRu = { "Огненный суперудар", "Крюк", "Телепорт", "Кручёный удар", "Невидимость", "Ледяной разлом" };
        static readonly string[] HAbilityEn = { "Fire Strike", "Hook", "Teleport", "Curve Shot", "Invisibility", "Ice Fissure" };
        static readonly string[] HDescEn =
        {
            "Wind up and the ball flies on fire. Knocks opponents down and shatters ice. Only with the ball at your feet.",
            "Throws a hook on a rope: pulls the ball right to your feet or drags an opponent in and stuns them.",
            "Instant jump forward where you face. Press again within a second to return to the mark.",
            "A kick with curl to the left (LB) or right (RB): the ball bends around a defender.",
            "Vanishes from opponents for 3 seconds: no silhouette, no shadow. Kicking gives you away.",
            "Raises an ice wall: it pushes everyone aside and stops the ball. Lobs fly over it.",
        };
        static string[] HName => Loc.En ? HNameEn : HNameRu;
        static string[] HRole => Loc.En ? HRoleEn : HRoleRu;
        static string[] HAbility => Loc.En ? HAbilityEn : HAbilityRu;
        static string[] HDesc => Loc.En ? HDescEn : HDescRu;
        static readonly string[] HDescRu =
        {
            "Замах — и мяч летит с огнём. Сбивает соперника с ног и разбивает лёд. Только когда мяч у ног.",
            "Бросает крюк на верёвке: притягивает мяч прямо к ногам или подтаскивает соперника и оглушает.",
            "Мгновенный прыжок вперёд по взгляду. Нажми ещё раз в течение секунды — вернёшься на метку.",
            "Удар с закруткой влево (LB) или вправо (RB): мяч уходит дугой в обход защитника.",
            "На 3 секунды исчезает для соперников: ни силуэта, ни тени. Удар выдаёт тебя.",
            "Выстреливает ледяную стену: она отталкивает всех на пути и останавливает мяч. Навес перелетает.",
        };
        public static readonly Color[] HColor =
        {
            new Color(1f, 0.54f, 0.12f), new Color(0.89f, 0.77f, 0.54f), new Color(0.56f, 0.85f, 1f),
            new Color(0.5f, 0.91f, 0.82f), new Color(0.71f, 0.55f, 1f), new Color(0.62f, 0.91f, 1f),
        };
        static string[] ModeName => Loc.En ? new[] { "1 vs 1", "2 vs 2", "3 vs 3", "4 vs 4", "Training" } : new[] { "1 на 1", "2 на 2", "3 на 3", "4 на 4", "Тренировка" };
        static string[] DiffName => Loc.En ? new[] { "Easy", "Medium", "Hard" } : new[] { "Лёгкий", "Средний", "Сложный" };
        static string TimeName(float sec) => Mathf.Round(sec / 60f) + Loc.T(" мин", " min");
        static string GoalsName(int g) => g <= 0 ? Loc.T("без лимита", "no limit") : Loc.T("до ", "first to ") + g;
        static string ModeNameOf(GameMode m) => ModeName[m == GameMode.OneVsOne ? 0 : m == GameMode.TwoVsTwo ? 1 : m == GameMode.ThreeVsThree ? 2 : m == GameMode.FourVsFour ? 3 : 4];
        static readonly GameMode[] RoomModes = { GameMode.OneVsOne, GameMode.TwoVsTwo, GameMode.ThreeVsThree, GameMode.FourVsFour };
        static int Step(int[] opts, int cur, int dir) { int i = Array.IndexOf(opts, cur); if (i < 0) i = 0; return opts[Mathf.Clamp(i + dir, 0, opts.Length - 1)]; }
        static string RulesText(Online n) => ModeNameOf(n.Mode) + "  ·  " + TimeName(n.MatchTime) + "  ·  " + GoalsName(n.WinGoals);

        class Item { public VisualElement El; public Action Submit, Left, Right, Focus; public TextField Text; }
        class Page { public VisualElement Root; public readonly List<Item> Items = new List<Item>(); public int Index; public Action Back; }

        readonly IMenuHost host;
        readonly UIDocument doc;
        readonly VisualElement root;
        readonly Font fBody, fBold, fHead;
        readonly List<Action> refreshers = new List<Action>();
        Page main, heroes, online, room, wait, settings;
        readonly List<Label> logoLetters = new List<Label>(); float logoT, shineT = -1;
        Label errorLabel, waitStatus, codeLabel;
        VisualElement rosterBox, roomItemsBox;
        string rosterSig = "", roomSig = "";

        public MenuUI(IMenuHost host, Transform parent)
        {
            this.host = host;
            fBody = Resources.Load<Font>("UI/Fonts/PTSans-Regular");
            fBold = Resources.Load<Font>("UI/Fonts/PTSans-Bold");
            fHead = Resources.Load<Font>("UI/Fonts/RussoOne-Regular");
            EnsureEventSystem();

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "ArenaMenuPanel";
            ps.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;

            var go = new GameObject("MenuUI");
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            go.SetActive(true);

            root = doc.rootVisualElement;
            var ss = Resources.Load<StyleSheet>("UI/menu");
            if (ss != null) root.styleSheets.Add(ss); else Debug.LogWarning("[Arena] нет Resources/UI/menu.uss");
            root.AddToClassList("menu-root");
            if (fBody != null) root.style.unityFontDefinition = FontDefinition.FromFont(fBody);
            root.pickingMode = PickingMode.Ignore;

            BuildAll();
            Show(Screen.None);
        }

        /// <summary>Собрать все экраны заново (при смене языка — все надписи берутся заново).</summary>
        void BuildAll()
        {
            root.Clear(); refreshers.Clear(); logoLetters.Clear(); rosterSig = ""; roomSig = "";
            BuildMain(); BuildHeroes(); BuildOnline(); BuildRoom(); BuildWait(); BuildSettings();
        }
        bool rebuildPending;

        static void EnsureEventSystem()
        {   // мышь для UI Toolkit приходит через EventSystem; стрелки/кнопки геймпада меню читает само (ниже), поэтому навигацию модуля отключаем
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem");
            Object.DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            var m = es.AddComponent<InputSystemUIInputModule>();
            m.AssignDefaultActions();
            m.move = null; m.submit = null; m.cancel = null;
        }

        // ---------- строительные блоки ----------
        Label L(string text, string cls, Font font = null)
        {
            var l = new Label(text);
            if (cls != null) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        Page NewPage(string sideCls = "side")
        {
            var p = new Page { Root = new VisualElement() };
            p.Root.AddToClassList("screen"); p.Root.pickingMode = PickingMode.Ignore;
            root.Add(p.Root);
            return p;
        }

        VisualElement Side(Page p, string cls = "side")
        {
            var s = new VisualElement(); foreach (var c in cls.Split(' ')) s.AddToClassList(c);
            var bg = new VisualElement(); bg.AddToClassList("side-bg"); bg.pickingMode = PickingMode.Ignore;
            bg.style.backgroundImage = new StyleBackground(SideGradient());
            s.Add(bg); // мягкий градиент: тёмный у края экрана, к правому краю панели растворяется в картинке
            p.Root.Add(s);
            return s;
        }

        Item AddItem(Page p, VisualElement parent, VisualElement el)
        {
            var it = new Item { El = el };
            int idx = p.Items.Count; p.Items.Add(it);
            el.RegisterCallback<PointerEnterEvent>(e => SetIndex(p, p.Items.IndexOf(it)));
            el.RegisterCallback<ClickEvent>(e => { SetIndex(p, p.Items.IndexOf(it)); MatchAudio.Ui(UiSound.Confirm); it.Submit?.Invoke(); });
            parent.Add(el);
            return it;
        }

        Item Button(Page p, VisualElement parent, string text, Action click, string extra = null)
        {
            var el = new VisualElement(); el.AddToClassList("item"); if (extra != null) foreach (var c in extra.Split(' ')) el.AddToClassList(c);
            el.Add(L(text, "item-label", fHead));
            var it = AddItem(p, parent, el); it.Submit = click;
            return it;
        }

        Item Selector(Page p, VisualElement parent, string title, Func<string> value, Action left, Action right)
        {
            var el = new VisualElement(); el.AddToClassList("item");
            el.Add(L(title, "selector-title"));
            var la = L("‹", "selector-arrow"); la.pickingMode = PickingMode.Position;
            var v = L("", "selector-value", fHead);
            var ra = L("›", "selector-arrow"); ra.pickingMode = PickingMode.Position;
            el.Add(la); el.Add(v); el.Add(ra);
            la.RegisterCallback<ClickEvent>(e => { MatchAudio.Ui(UiSound.Move); left(); e.StopPropagation(); });
            ra.RegisterCallback<ClickEvent>(e => { MatchAudio.Ui(UiSound.Move); right(); e.StopPropagation(); });
            refreshers.Add(() => { var s = value(); if (v.text != s) v.text = s; });
            var it = AddItem(p, parent, el); it.Left = left; it.Right = right; it.Submit = right;
            return it;
        }

        Item Field(Page p, VisualElement parent, string title, Func<string> get, Action<string> set, int max, Action enter, bool upper)
        {
            var el = new VisualElement(); el.AddToClassList("field-row");
            el.Add(L(title, "selector-title"));
            var tf = new TextField { maxLength = max, value = get() ?? "" };
            tf.AddToClassList("text-field");
            tf.RegisterValueChangedCallback(e =>
            {
                string s = upper ? (e.newValue ?? "").ToUpperInvariant() : e.newValue;
                if (upper && s != e.newValue) tf.SetValueWithoutNotify(s);
                set(s);
            });
            el.Add(tf);
            var it = AddItem(p, parent, el); it.Text = tf; it.Submit = enter;
            return it;
        }

        class InfoCard { public Label Name, Role, Ability, Desc; }
        InfoCard Info(Page p)
        {
            var card = new VisualElement(); card.AddToClassList("info-card"); card.pickingMode = PickingMode.Ignore;
            var ic = new InfoCard { Name = L("", "info-name", fHead), Role = L("", "info-role"), Ability = L("", "info-ability", fHead), Desc = L("", "info-desc") };
            card.Add(ic.Name); card.Add(ic.Role); card.Add(ic.Ability); card.Add(ic.Desc);
            p.Root.Add(card);
            refreshers.Add(() =>
            {
                int h = Mathf.Clamp(host.HeroIndex, 0, 5);
                if (ic.Name.text == HName[h]) return;
                ic.Name.text = HName[h]; ic.Name.style.color = HColor[h];
                ic.Role.text = Loc.T("Роль: ", "Role: ") + HRole[h]; ic.Ability.text = HAbility[h]; ic.Desc.text = HDesc[h];
            });
            return ic;
        }

        static Texture2D sideGrad;
        static Texture2D SideGradient()
        {   // слева направо: почти чёрная синева → прозрачно (плавно, по кривой)
            if (sideGrad != null) return sideGrad;
            const int N = 256;
            sideGrad = new Texture2D(N, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuSideGradient" };
            var px = new Color32[N];
            for (int i = 0; i < N; i++)
            {
                float x = i / (N - 1f), a = x < 0.45f ? 0.9f : 0.9f * (1 - Mathf.SmoothStep(0, 1, (x - 0.45f) / 0.55f));
                px[i] = new Color(0.027f, 0.047f, 0.12f, a);
            }
            sideGrad.SetPixels32(px); sideGrad.Apply();
            return sideGrad;
        }

        // ---------- экраны ----------
        void BuildMain()
        {
            main = NewPage(); var s = Side(main);
            // название игры SpellStrike: две строки, каждая буква — отдельная надпись (по буквам пробегает блик, Tick)
            var logo = new VisualElement(); logo.AddToClassList("logo"); logo.pickingMode = PickingMode.Ignore;
            logo.Add(LogoRow("SPELL", "title-spell", "logo-row")); logo.Add(LogoRow("STRIKE", "title-strike", "logo-row logo-row2"));
            s.Add(logo);
            s.Add(L(Loc.T("футбол со способностями · до 8 игроков", "football with superpowers · up to 8 players"), "subtitle"));
            Button(main, s, Loc.T("Играть", "Play"), () => Show(Screen.Heroes), "item-primary");
            Button(main, s, Loc.T("Найти матч", "Find a match"), () => host.JoinServer()); // онлайн на нашем сервере: до 8 игроков, пустые места — боты
            Button(main, s, Loc.T("Онлайн с друзьями", "Play with friends"), () => Show(Screen.Online));
            Button(main, s, Loc.T("Настройки", "Settings"), () => Show(Screen.Settings));
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            Button(main, s, Loc.T("Выход", "Quit"), () => host.Quit(), "item-small"); // в браузере выйти — значит закрыть вкладку
#endif
            var sp = new VisualElement(); sp.AddToClassList("spacer"); sp.pickingMode = PickingMode.Ignore; s.Add(sp);
            s.Add(L(Loc.T("↑↓ — выбор   A / Enter — ОК   B / Esc — назад", "↑↓ — select   A / Enter — OK   B / Esc — back"), "hints"));
            s.Add(L(Loc.T("версия ", "version ") + Online.NetVersion, "hints")); // у друзей и на сервере должна быть та же

        }

        VisualElement LogoRow(string word, string cls, string rowCls)
        {
            var row = new VisualElement(); foreach (var c in rowCls.Split(' ')) row.AddToClassList(c); row.pickingMode = PickingMode.Ignore;
            foreach (char ch in word) { var l = L(ch.ToString(), cls + " logo-letter", fHead); logoLetters.Add(l); row.Add(l); }
            return row;
        }

        void BuildSettings()
        {
            settings = NewPage(); var s = Side(settings);
            s.Add(L(Loc.T("НАСТРОЙКИ", "SETTINGS"), "header", fHead));
            s.Add(L(Loc.T("Всё запоминается между запусками", "Everything is saved between launches"), "note"));
            Selector(settings, s, "Язык / Language", () => Loc.En ? "ENGLISH" : "РУССКИЙ",
                () => { Loc.En = !Loc.En; rebuildPending = true; }, () => { Loc.En = !Loc.En; rebuildPending = true; });
            Selector(settings, s, Loc.T("Звук", "Sound"), () => MatchAudio.SoundOn ? Loc.T("ВКЛ", "ON") : Loc.T("ВЫКЛ", "OFF"),
                () => MatchAudio.SoundOn = !MatchAudio.SoundOn, () => MatchAudio.SoundOn = !MatchAudio.SoundOn);
            Selector(settings, s, Loc.T("Громкость", "Volume"), () => Mathf.RoundToInt(MatchAudio.Volume * 100) + "%",
                () => MatchAudio.Volume = Mathf.Round(MatchAudio.Volume * 10 - 1) / 10f, () => MatchAudio.Volume = Mathf.Round(MatchAudio.Volume * 10 + 1) / 10f);
            Selector(settings, s, Loc.T("Трибуны", "Crowd"), () => MatchAudio.CrowdOn ? Loc.T("ВКЛ", "ON") : Loc.T("ВЫКЛ", "OFF"),
                () => MatchAudio.CrowdOn = !MatchAudio.CrowdOn, () => MatchAudio.CrowdOn = !MatchAudio.CrowdOn);
            Selector(settings, s, Loc.T("Музыка", "Music"), () => MatchAudio.MusicOn ? Loc.T("ВКЛ", "ON") : Loc.T("ВЫКЛ", "OFF"),
                () => MatchAudio.MusicOn = !MatchAudio.MusicOn, () => MatchAudio.MusicOn = !MatchAudio.MusicOn);
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            Selector(settings, s, Loc.T("Экран", "Screen"), () => UnityEngine.Screen.fullScreen ? Loc.T("ПОЛНЫЙ", "FULLSCREEN") : Loc.T("ОКНО", "WINDOW"),
                () => UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen, () => UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen);
#endif
            Button(settings, s, Loc.T("Назад", "Back"), () => Show(Screen.Main), "item-small");
            settings.Back = () => Show(Screen.Main);
            var sp = new VisualElement(); sp.AddToClassList("spacer"); sp.pickingMode = PickingMode.Ignore; s.Add(sp);
            s.Add(L(Loc.T("↑↓ — выбор   ←→ — значение   A / Enter — ОК   B / Esc — назад", "↑↓ — select   ←→ — change   A / Enter — OK   B / Esc — back"), "hints"));
        }

        /// <summary>Каждый кадр на главном экране: живой логотип.</summary>
        void TickMain(float dt)
        {
            if (Current != Screen.Main) return;
            logoT += dt;
            // появление: буквы падают сверху по очереди; потом свечение «дышит», раз в 4 с пробегает блик
            if (shineT < 0 && logoT > 1.2f) shineT = 0;
            if (shineT >= 0) { shineT += dt; if (shineT > 4f) shineT = 0; }
            float breathe = 0.5f + 0.5f * Mathf.Sin(logoT * 2.2f);
            for (int i = 0; i < logoLetters.Count; i++)
            {
                var l = logoLetters[i]; bool strike = i >= 5;
                float d = Mathf.Clamp01((logoT - 0.08f * i) / 0.35f), e = 1 - (1 - d) * (1 - d) * (1 - d);
                float bounce = d < 1 ? Mathf.Sin(d * Mathf.PI) * 0.12f : 0;
                l.style.opacity = e;
                l.style.translate = new Translate(0, (1 - e) * -60, 0);
                l.style.scale = new Scale(Vector2.one * (1 + bounce));
                float sh = shineT >= 0 ? Mathf.Clamp01(1 - Mathf.Abs(shineT * 9f - i) / 1.6f) : 0; // блик идёт слева направо по всем 11 буквам
                Color baseC = strike ? new Color(1f, 0.70f, 0.12f) : new Color(0.55f, 0.89f, 1f);
                Color glowC = strike ? new Color(1f, 0.35f, 0.1f) : new Color(0.51f, 0.43f, 1f);
                l.style.color = Color.Lerp(baseC, Color.white, sh * 0.85f);
                var ts = new TextShadow { offset = Vector2.zero, blurRadius = 10 + 10 * breathe + 14 * sh, color = new Color(glowC.r, glowC.g, glowC.b, 0.6f + 0.35f * breathe) };
                l.style.textShadow = new StyleTextShadow(ts);
            }
        }

        void BuildHeroes()
        {
            heroes = NewPage(); var s = Side(heroes);
            s.Add(L(Loc.T("ВЫБОР ГЕРОЯ", "CHOOSE YOUR HERO"), "header", fHead));
            s.Add(L(Loc.T("Пустые места в командах займут боты", "Empty spots will be filled by bots"), "note"));
            for (int i = 0; i < 6; i++)
            {
                int h = i;
                var el = new VisualElement(); el.AddToClassList("item"); el.AddToClassList("hero-row");
                var chip = new VisualElement(); chip.AddToClassList("hero-chip"); chip.style.backgroundColor = HColor[h]; chip.pickingMode = PickingMode.Ignore;
                var texts = new VisualElement(); texts.AddToClassList("hero-texts"); texts.pickingMode = PickingMode.Ignore;
                texts.Add(L(HName[h], "hero-name item-label", fHead));
                texts.Add(L(HRole[h] + " · " + HAbility[h], "hero-role"));
                el.Add(chip); el.Add(texts);
                var it = AddItem(heroes, s, el);
                it.Focus = () => host.HeroIndex = h;
                it.Submit = () => SetIndex(heroes, 10); // к кнопке «В бой»
                refreshers.Add(() => el.EnableInClassList("selected", host.HeroIndex == h));
            }
            int nm = ModeName.Length;
            Selector(heroes, s, Loc.T("Формат", "Mode"), () => ModeName[Mathf.Clamp(host.ModeIndex, 0, nm - 1)], () => host.ModeIndex = (host.ModeIndex + nm - 1) % nm, () => host.ModeIndex = (host.ModeIndex + 1) % nm);
            Selector(heroes, s, Loc.T("Время", "Time"), () => host.ModeIndex == nm - 1 ? "—" : TimeName(Match.TimeOptions[host.TimeIndex]), () => host.TimeIndex--, () => host.TimeIndex++);
            Selector(heroes, s, Loc.T("Голы", "Goals"), () => host.ModeIndex == nm - 1 ? "—" : GoalsName(Match.GoalOptions[host.GoalsIndex]), () => host.GoalsIndex--, () => host.GoalsIndex++);
            Selector(heroes, s, Loc.T("Соперники", "Opponents"), () => DiffName[Mathf.Clamp(host.DiffIndex, 0, 2)], () => host.DiffIndex = Mathf.Max(0, host.DiffIndex - 1), () => host.DiffIndex = Mathf.Min(2, host.DiffIndex + 1));
            Button(heroes, s, Loc.T("В БОЙ!", "PLAY!"), () => host.StartOffline(), "item-primary");
            Button(heroes, s, Loc.T("Назад", "Back"), () => Show(Screen.Main), "item-small");
            heroes.Back = () => Show(Screen.Main);
            Info(heroes);
        }

        void BuildOnline()
        {
            online = NewPage(); var s = Side(online);
            s.Add(L(Loc.T("ОНЛАЙН", "ONLINE"), "header", fHead));
            s.Add(L(Loc.T("Создай комнату и отправь друзьям код. Герой — тот, что выбран на экране «Играть», в комнате его можно поменять.", "Create a room and send the code to your friends. Your hero is the one chosen on the Play screen; you can change it in the room."), "note"));
            Item create = null;
            Field(online, s, Loc.T("Имя", "Name"), () => host.PlayerName, v => host.PlayerName = v, 16, () => SetIndex(online, online.Items.IndexOf(create)), false);
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            Selector(online, s, Loc.T("Связь", "Connection"), Online.LinkText, () => Online.CycleLink(-1), () => Online.CycleLink(1)); // через Unity или напрямую (Radmin VPN, домашняя сеть)
#endif
            create = Button(online, s, Loc.T("Создать комнату", "Create room"), () => { host.CreateRoom(); }, "item-primary");
            s.Add(L(Loc.T("— или войди в комнату друга —", "— or join a friend's room —"), "divider"));
            Field(online, s, Loc.T("Код", "Code"), () => host.JoinCode, v => host.JoinCode = v, 12, () => TryJoin(), true);
            Button(online, s, Loc.T("Войти по коду", "Join by code"), () => TryJoin());
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            s.Add(L(Loc.T("— или игра с незнакомцами —", "— or play with strangers —"), "divider"));
            Button(online, s, Loc.T("Найти матч", "Find a match"), () => TryServer());
#endif
            errorLabel = L("", "error"); s.Add(errorLabel);
            refreshers.Add(() => { string e = host.NetError ?? ""; if (errorLabel.text != e) errorLabel.text = e; errorLabel.EnableInClassList("hidden", e == ""); });
            var sp = new VisualElement(); sp.AddToClassList("spacer"); sp.pickingMode = PickingMode.Ignore; s.Add(sp);
            Button(online, s, Loc.T("Назад", "Back"), () => Show(Screen.Main), "item-small");
            online.Back = () => Show(Screen.Main);
        }

        void TryJoin() { if (!string.IsNullOrWhiteSpace(host.JoinCode)) host.JoinRoom(); }
        void TryServer() => host.JoinServer(); // пустой адрес — наш сервер по умолчанию

        void BuildWait()
        {
            wait = NewPage();
            var box = new VisualElement(); box.AddToClassList("center-box"); wait.Root.Add(box);
            waitStatus = L("", "status", fHead); box.Add(waitStatus);
            Button(wait, box, Loc.T("Отмена", "Cancel"), () => host.LeaveRoom(), "item-small");
            wait.Back = () => host.LeaveRoom();
        }

        void BuildRoom()
        {
            room = NewPage(); var s = Side(room, "side side-wide");
            s.Add(L(Loc.T("КОМНАТА", "ROOM"), "header", fHead));
            codeLabel = L("", "code", fHead); s.Add(codeLabel);
            refreshers.Add(() => { string c = host.Net != null ? host.Net.Code : ""; if (codeLabel.text != c) codeLabel.text = c; });
            var rulesLabel = L("", "note"); s.Add(rulesLabel); // формат, время, голы — видят все
            refreshers.Add(() => { var n = host.Net; string t = n == null ? "" : RulesText(n); if (rulesLabel.text != t) rulesLabel.text = t; });
            var autoLabel = L("", "note"); s.Add(autoLabel); // «Найти матч»: сколько ждать до автостарта
            refreshers.Add(() =>
            {
                var n = host.Net;
                string t = n == null || !n.ViaServer ? "" : n.AutoStart > 0 ? Loc.T("Матч начнётся через ", "The match starts in ") + Mathf.CeilToInt(n.AutoStart) + Loc.T(" с. Пустые места займут боты.", " s. Empty spots will be filled by bots.")
                         : n.AutoStart == 0 ? Loc.T("Матч начинается…", "The match is starting…") : "";
                if (autoLabel.text != t) autoLabel.text = t;
                autoLabel.EnableInClassList("hidden", t == "");
            });
            rosterBox = new VisualElement(); rosterBox.AddToClassList("roster"); rosterBox.pickingMode = PickingMode.Ignore; s.Add(rosterBox);
            roomItemsBox = new VisualElement(); roomItemsBox.pickingMode = PickingMode.Ignore; s.Add(roomItemsBox);
            room.Back = () => host.LeaveRoom();
            Info(room);
        }

        /// <summary>Формат, длительность и голы до победы (хост комнаты или главный на сервере).</summary>
        void RulesSelectors(Online net)
        {
            int n = RoomModes.Length;
            int Mi() => Math.Max(0, Array.IndexOf(RoomModes, net.Mode));
            Selector(room, roomItemsBox, Loc.T("Формат", "Mode"), () => ModeNameOf(net.Mode),
                     () => net.SetRules(RoomModes[(Mi() + n - 1) % n], net.Difficulty, net.MatchTime, net.WinGoals),
                     () => net.SetRules(RoomModes[(Mi() + 1) % n], net.Difficulty, net.MatchTime, net.WinGoals));
            Selector(room, roomItemsBox, Loc.T("Время", "Time"), () => TimeName(net.MatchTime),
                     () => net.SetRules(net.Mode, net.Difficulty, Step(Match.TimeOptions, net.MatchTime, -1), net.WinGoals),
                     () => net.SetRules(net.Mode, net.Difficulty, Step(Match.TimeOptions, net.MatchTime, 1), net.WinGoals));
            Selector(room, roomItemsBox, Loc.T("Голы", "Goals"), () => GoalsName(net.WinGoals),
                     () => net.SetRules(net.Mode, net.Difficulty, net.MatchTime, Step(Match.GoalOptions, net.WinGoals, -1)),
                     () => net.SetRules(net.Mode, net.Difficulty, net.MatchTime, Step(Match.GoalOptions, net.WinGoals, 1)));
        }

        /// <summary>Пункты комнаты зависят от того, хост ли ты, — пересобираются при смене.</summary>
        void RebuildRoomItems()
        {
            var net = host.Net;
            roomItemsBox.Clear(); room.Items.Clear();
            if (!net.ViaServer) Button(room, roomItemsBox, Loc.T("Скопировать код", "Copy code"), () => { GUIUtility.systemCopyBuffer = net.Code; }, "item-small");
            Selector(room, roomItemsBox, Loc.T("Герой", "Hero"), () => HName[Mathf.Clamp(host.HeroIndex, 0, 5)], () => host.SetRoomHero((host.HeroIndex + 5) % 6), () => host.SetRoomHero((host.HeroIndex + 1) % 6));
            Selector(room, roomItemsBox, Loc.T("Команда", "Team"), () => host.RoomTeam == Team.Home ? Loc.T("Синие", "Blue") : Loc.T("Красные", "Red"),
                     () => host.SetRoomTeam(host.RoomTeam == Team.Home ? Team.Away : Team.Home), () => host.SetRoomTeam(host.RoomTeam == Team.Home ? Team.Away : Team.Home));
            if (net.ViaServer && net.IsLeader) // «Найти матч»: главный (вошёл первым) начинает сразу или ждёт игроков
            {
                RulesSelectors(net);
                Selector(room, roomItemsBox, Loc.T("Боты", "Bots"), () => DiffName[Mathf.Clamp(net.Difficulty, 0, 2)],
                         () => net.SetMode(net.Mode, Math.Max(0, net.Difficulty - 1)), () => net.SetMode(net.Mode, Math.Min(2, net.Difficulty + 1)));
                Button(room, roomItemsBox, Loc.T("НАЧАТЬ СЕЙЧАС", "START NOW"), () => host.StartRoomMatch(), "item-primary");
                roomItemsBox.Add(L(Loc.T("Ты главный: начни сразу (свободные места займут боты) или подожди игроков.", "You are the leader: start now (bots fill empty spots) or wait for players."), "note"));
            }
            else if (net.ViaServer) roomItemsBox.Add(L(Loc.T("Ждём игроков. Начнёт главный (★) или автостарт.", "Waiting for players. The leader (★) starts, or auto-start."), "note"));
            else if (net.IsHost) // комната с другом: хост выбирает формат и сложность
            {
                RulesSelectors(net);
                Selector(room, roomItemsBox, Loc.T("Боты", "Bots"), () => DiffName[Mathf.Clamp(net.Difficulty, 0, 2)],
                         () => net.SetMode(net.Mode, Math.Max(0, net.Difficulty - 1)), () => net.SetMode(net.Mode, Math.Min(2, net.Difficulty + 1)));
                Button(room, roomItemsBox, Loc.T("НАЧАТЬ МАТЧ", "START MATCH"), () => host.StartRoomMatch(), "item-primary");
            }
            else if (!net.ViaServer) roomItemsBox.Add(L(Loc.T("Ждём, пока хост начнёт матч…", "Waiting for the host to start…"), "note"));
            Button(room, roomItemsBox, Loc.T("Выйти из комнаты", "Leave room"), () => host.LeaveRoom(), "item-small");
            room.Index = Mathf.Clamp(room.Index, 0, room.Items.Count - 1);
            SetIndex(room, room.Index);
        }

        void RefreshRoster()
        {
            var net = host.Net; if (net == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(net.LeaderSlot).Append('|');
            foreach (var st in net.Roster) sb.Append(st.Slot).Append(st.Name).Append(st.Hero).Append(st.Team).Append(st.Ping).Append('|');
            string sig = sb.ToString(); if (sig == rosterSig) return; rosterSig = sig;
            rosterBox.Clear();
            foreach (var st in net.Roster)
            {
                var row = new VisualElement(); row.AddToClassList("roster-row"); row.pickingMode = PickingMode.Ignore;
                var t = new VisualElement(); t.AddToClassList("roster-team"); t.AddToClassList(st.Team == Team.Home ? "team-home" : "team-away"); t.pickingMode = PickingMode.Ignore;
                row.Add(t);
                bool lead = net.ViaServer ? st.Slot == net.LeaderSlot : st.Slot == 0;
                row.Add(L((lead ? "★ " : "") + st.Name + (st.Slot == net.MySlot ? Loc.T("  (ты)", "  (you)") : ""), "roster-name", fBold));
                int hi = Array.IndexOf(HeroOrder, st.Hero);
                row.Add(L(hi >= 0 ? HName[hi] : "?", "roster-hero"));
                row.Add(L(st.Slot == 0 && !net.ViaServer ? Loc.T("хост", "host") : st.Ping < 0 ? Loc.T("пинг —", "ping —") : Loc.T("пинг ", "ping ") + st.Ping + Loc.T(" мс", " ms"), "roster-ping"));
                rosterBox.Add(row);
            }
        }

        // ---------- показ ----------
        Page PageOf(Screen s)
        {
            switch (s)
            {
                case Screen.Main: return main;
                case Screen.Heroes: return heroes;
                case Screen.Online: return online;
                case Screen.Settings: return settings;
                case Screen.Room: return host.Net != null && host.Net.State == Online.Phase.Connecting ? wait : room;
                default: return null;
            }
        }

        public void Show(Screen s)
        {
            Current = s;
            root.style.display = s == Screen.None ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var p in new[] { main, heroes, online, room, wait, settings }) p.Root.EnableInClassList("hidden", true);
            root.focusController?.focusedElement?.Blur();
            if (s == Screen.Heroes) heroes.Index = Mathf.Clamp(host.HeroIndex, 0, 5);
            if (s == Screen.Room) { roomSig = ""; rosterSig = ""; }
            var pg = PageOf(s);
            if (pg != null) { pg.Root.EnableInClassList("hidden", false); SetIndex(pg, pg.Index); }
        }

        void SetIndex(Page p, int i)
        {
            if (p.Items.Count == 0) return;
            i = Mathf.Clamp(i, 0, p.Items.Count - 1);
            var prev = p.Index < p.Items.Count ? p.Items[p.Index] : null;
            p.Index = i;
            for (int k = 0; k < p.Items.Count; k++) p.Items[k].El.EnableInClassList("focused", k == i);
            var it = p.Items[i];
            if (it.Text != null) it.Text.Focus();
            else if (prev != null && prev.Text != null) prev.Text.Blur();
            it.Focus?.Invoke();
        }

        // ---------- каждый кадр ----------
        float repeatT; int heldDir;

        public void Tick(float dt)
        {
            if (rebuildPending)
            {   // язык сменился: пересобрать меню и остаться на том же пункте настроек
                rebuildPending = false;
                int idx = settings.Index; var cur = Current;
                BuildAll(); Show(cur); SetIndex(settings, idx);
            }
            TickMain(dt);
            if (Current == Screen.None) return;
            if (Current == Screen.Room)
            {
                var net = host.Net;
                if (net == null) return;
                bool connecting = net.State == Online.Phase.Connecting;
                room.Root.EnableInClassList("hidden", connecting); wait.Root.EnableInClassList("hidden", !connecting);
                if (connecting) { if (waitStatus.text != net.Status) waitStatus.text = net.Status; }
                else
                {
                    string sig = net.IsHost + "/" + net.IsLeader + "/" + net.State;
                    if (sig != roomSig) { roomSig = sig; RebuildRoomItems(); }
                    RefreshRoster();
                }
            }
            foreach (var r in refreshers) r();
            Navigate(dt);
        }

        void Navigate(float dt)
        {
            var p = PageOf(Current); if (p == null || p.Items.Count == 0) return;
            var it = p.Items[Mathf.Clamp(p.Index, 0, p.Items.Count - 1)];
            bool typing = it.Text != null;
            var gp = Gamepad.current; var kb = Keyboard.current;

            int dir = 0; // 1 вверх, 2 вниз, 3 влево, 4 вправо
            if (gp != null)
            {
                Vector2 v = gp.leftStick.ReadValue(); var d = gp.dpad.ReadValue(); if (d.sqrMagnitude > 0.1f) v = d;
                if (v.y > 0.6f) dir = 1; else if (v.y < -0.6f) dir = 2; else if (v.x < -0.6f) dir = 3; else if (v.x > 0.6f) dir = 4;
            }
            if (kb != null && dir == 0)
            {
                if (kb.upArrowKey.isPressed || (!typing && kb.wKey.isPressed)) dir = 1;
                else if (kb.downArrowKey.isPressed || (!typing && kb.sKey.isPressed)) dir = 2;
                else if (!typing && (kb.leftArrowKey.isPressed || kb.aKey.isPressed)) dir = 3;
                else if (!typing && (kb.rightArrowKey.isPressed || kb.dKey.isPressed)) dir = 4;
            }
            bool fire = false;
            if (dir != heldDir) { heldDir = dir; fire = dir != 0; repeatT = 0.38f; }
            else if (dir != 0) { repeatT -= dt; if (repeatT <= 0) { fire = true; repeatT = 0.12f; } }
            if (fire)
            {
                int was = p.Index;
                if (dir == 1) SetIndex(p, p.Index - 1);
                else if (dir == 2) SetIndex(p, p.Index + 1);
                else if (dir == 3) it.Left?.Invoke();
                else if (dir == 4) it.Right?.Invoke();
                if (p.Index != was || (dir >= 3 && (it.Left != null || it.Right != null))) MatchAudio.Ui(UiSound.Move);
            }

            bool submit = (gp != null && gp.buttonSouth.wasPressedThisFrame) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || (!typing && kb.spaceKey.wasPressedThisFrame)));
            bool back = (gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && (kb.escapeKey.wasPressedThisFrame || (!typing && kb.backspaceKey.wasPressedThisFrame)));
            if (submit) { MatchAudio.Ui(UiSound.Confirm); it.Submit?.Invoke(); }
            else if (back && p.Back != null) { MatchAudio.Ui(UiSound.Back); p.Back.Invoke(); }
        }
    }
}
