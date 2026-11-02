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
        int ModeIndex { get; set; }   // 0 — 1 на 1, 1 — 2 на 2, 2 — 4 на 4, 3 — тренировка
        int DiffIndex { get; set; }
        string PlayerName { get; set; }
        string JoinCode { get; set; }
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
        public enum Screen { None, Main, Heroes, Online, Room }
        public Screen Current { get; private set; } = Screen.None;
        /// <summary>На этом экране камера смотрит на героя на подиуме.</summary>
        public bool Showcase => Current == Screen.Heroes || (Current == Screen.Room && host.Net != null && host.Net.State != Online.Phase.Connecting);

        // порядок как в ArenaGame.HeroList
        public static readonly Hero[] HeroOrder = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };
        static readonly string[] HName = { "Жар", "Багор", "Миг", "Вихрь", "Тень", "Стена" };
        static readonly string[] HRole = { "Удар", "Контроль мяча", "Перемещение", "Пас", "Скрытность", "Защита ворот" };
        static readonly string[] HAbility = { "Огненный суперудар", "Крюк", "Телепорт", "Кручёный удар", "Невидимость", "Ледяной разлом" };
        static readonly string[] HDesc =
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
        static readonly string[] ModeName = { "1 на 1", "2 на 2", "4 на 4", "Тренировка" };
        static readonly string[] DiffName = { "Лёгкий", "Средний", "Сложный" };
        static readonly GameMode[] RoomModes = { GameMode.OneVsOne, GameMode.TwoVsTwo, GameMode.FourVsFour };

        class Item { public VisualElement El; public Action Submit, Left, Right, Focus; public TextField Text; }
        class Page { public VisualElement Root; public readonly List<Item> Items = new List<Item>(); public int Index; public Action Back; }

        readonly IMenuHost host;
        readonly UIDocument doc;
        readonly VisualElement root;
        readonly Font fBody, fBold, fHead;
        readonly List<Action> refreshers = new List<Action>();
        Page main, heroes, online, room, wait;
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

            BuildMain(); BuildHeroes(); BuildOnline(); BuildRoom(); BuildWait();
            Show(Screen.None);
        }

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
            p.Root.Add(s);
            return s;
        }

        Item AddItem(Page p, VisualElement parent, VisualElement el)
        {
            var it = new Item { El = el };
            int idx = p.Items.Count; p.Items.Add(it);
            el.RegisterCallback<PointerEnterEvent>(e => SetIndex(p, p.Items.IndexOf(it)));
            el.RegisterCallback<ClickEvent>(e => { SetIndex(p, p.Items.IndexOf(it)); it.Submit?.Invoke(); });
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
            la.RegisterCallback<ClickEvent>(e => { left(); e.StopPropagation(); });
            ra.RegisterCallback<ClickEvent>(e => { right(); e.StopPropagation(); });
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
                ic.Role.text = "Роль: " + HRole[h]; ic.Ability.text = HAbility[h]; ic.Desc.text = HDesc[h];
            });
            return ic;
        }

        // ---------- экраны ----------
        void BuildMain()
        {
            main = NewPage(); var s = Side(main);
            s.Add(L("АРЕНА", "title-big", fHead));
            s.Add(L("СТРАЙКЕРОВ", "title-accent", fHead));
            s.Add(L("футбол со способностями · до 8 игроков", "subtitle"));
            Button(main, s, "Играть", () => Show(Screen.Heroes), "item-primary");
            Button(main, s, "Онлайн с друзьями", () => Show(Screen.Online));
            Button(main, s, "Выход", () => host.Quit());
            var sp = new VisualElement(); sp.AddToClassList("spacer"); sp.pickingMode = PickingMode.Ignore; s.Add(sp);
            s.Add(L("↑↓ — выбор   ←→ — значение   A / Enter — ОК   B / Esc — назад", "hints"));
        }

        void BuildHeroes()
        {
            heroes = NewPage(); var s = Side(heroes);
            s.Add(L("ВЫБОР ГЕРОЯ", "header", fHead));
            s.Add(L("Пустые места в командах займут боты", "note"));
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
                it.Submit = () => SetIndex(heroes, 8); // к кнопке «В бой»
                refreshers.Add(() => el.EnableInClassList("selected", host.HeroIndex == h));
            }
            Selector(heroes, s, "Формат", () => ModeName[Mathf.Clamp(host.ModeIndex, 0, 3)], () => host.ModeIndex = (host.ModeIndex + 3) % 4, () => host.ModeIndex = (host.ModeIndex + 1) % 4);
            Selector(heroes, s, "Соперники", () => DiffName[Mathf.Clamp(host.DiffIndex, 0, 2)], () => host.DiffIndex = Mathf.Max(0, host.DiffIndex - 1), () => host.DiffIndex = Mathf.Min(2, host.DiffIndex + 1));
            Button(heroes, s, "В БОЙ!", () => host.StartOffline(), "item-primary");
            Button(heroes, s, "Назад", () => Show(Screen.Main), "item-small");
            heroes.Back = () => Show(Screen.Main);
            Info(heroes);
        }

        void BuildOnline()
        {
            online = NewPage(); var s = Side(online);
            s.Add(L("ОНЛАЙН", "header", fHead));
            s.Add(L("Создай комнату и отправь друзьям код. Герой — тот, что выбран на экране «Играть», в комнате его можно поменять.", "note"));
            Field(online, s, "Имя", () => host.PlayerName, v => host.PlayerName = v, 16, () => SetIndex(online, 1), false);
            Button(online, s, "Создать комнату", () => { host.CreateRoom(); }, "item-primary");
            s.Add(L("— или войди в комнату друга —", "divider"));
            Field(online, s, "Код", () => host.JoinCode, v => host.JoinCode = v, 12, () => TryJoin(), true);
            Button(online, s, "Войти по коду", () => TryJoin());
            errorLabel = L("", "error"); s.Add(errorLabel);
            refreshers.Add(() => { string e = host.NetError ?? ""; if (errorLabel.text != e) errorLabel.text = e; errorLabel.EnableInClassList("hidden", e == ""); });
            var sp = new VisualElement(); sp.AddToClassList("spacer"); sp.pickingMode = PickingMode.Ignore; s.Add(sp);
            Button(online, s, "Назад", () => Show(Screen.Main), "item-small");
            online.Back = () => Show(Screen.Main);
        }

        void TryJoin() { if (!string.IsNullOrWhiteSpace(host.JoinCode)) host.JoinRoom(); }

        void BuildWait()
        {
            wait = NewPage();
            var box = new VisualElement(); box.AddToClassList("center-box"); wait.Root.Add(box);
            waitStatus = L("", "status", fHead); box.Add(waitStatus);
            Button(wait, box, "Отмена", () => host.LeaveRoom(), "item-small");
            wait.Back = () => host.LeaveRoom();
        }

        void BuildRoom()
        {
            room = NewPage(); var s = Side(room, "side side-wide");
            s.Add(L("КОМНАТА", "header", fHead));
            codeLabel = L("", "code", fHead); s.Add(codeLabel);
            refreshers.Add(() => { string c = host.Net != null ? host.Net.Code : ""; if (codeLabel.text != c) codeLabel.text = c; });
            rosterBox = new VisualElement(); rosterBox.AddToClassList("roster"); rosterBox.pickingMode = PickingMode.Ignore; s.Add(rosterBox);
            roomItemsBox = new VisualElement(); roomItemsBox.pickingMode = PickingMode.Ignore; s.Add(roomItemsBox);
            room.Back = () => host.LeaveRoom();
            Info(room);
        }

        /// <summary>Пункты комнаты зависят от того, хост ли ты, — пересобираются при смене.</summary>
        void RebuildRoomItems()
        {
            var net = host.Net;
            roomItemsBox.Clear(); room.Items.Clear();
            Button(room, roomItemsBox, "Скопировать код", () => { GUIUtility.systemCopyBuffer = net.Code; }, "item-small");
            Selector(room, roomItemsBox, "Герой", () => HName[Mathf.Clamp(host.HeroIndex, 0, 5)], () => host.SetRoomHero((host.HeroIndex + 5) % 6), () => host.SetRoomHero((host.HeroIndex + 1) % 6));
            Selector(room, roomItemsBox, "Команда", () => host.RoomTeam == Team.Home ? "Синие" : "Красные",
                     () => host.SetRoomTeam(host.RoomTeam == Team.Home ? Team.Away : Team.Home), () => host.SetRoomTeam(host.RoomTeam == Team.Home ? Team.Away : Team.Home));
            if (net.IsHost)
            {
                Selector(room, roomItemsBox, "Формат", () => ModeName[Math.Max(0, Array.IndexOf(RoomModes, net.Mode))],
                         () => net.SetMode(RoomModes[(Math.Max(0, Array.IndexOf(RoomModes, net.Mode)) + 2) % 3], net.Difficulty),
                         () => net.SetMode(RoomModes[(Math.Max(0, Array.IndexOf(RoomModes, net.Mode)) + 1) % 3], net.Difficulty));
                Selector(room, roomItemsBox, "Боты", () => DiffName[Mathf.Clamp(net.Difficulty, 0, 2)],
                         () => net.SetMode(net.Mode, Math.Max(0, net.Difficulty - 1)), () => net.SetMode(net.Mode, Math.Min(2, net.Difficulty + 1)));
                Button(room, roomItemsBox, "НАЧАТЬ МАТЧ", () => host.StartRoomMatch(), "item-primary");
            }
            else roomItemsBox.Add(L("Ждём, пока хост начнёт матч…", "note"));
            Button(room, roomItemsBox, "Выйти из комнаты", () => host.LeaveRoom(), "item-small");
            room.Index = Mathf.Clamp(room.Index, 0, room.Items.Count - 1);
            SetIndex(room, room.Index);
        }

        void RefreshRoster()
        {
            var net = host.Net; if (net == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var st in net.Roster) sb.Append(st.Slot).Append(st.Name).Append(st.Hero).Append(st.Team).Append(st.Ping).Append('|');
            string sig = sb.ToString(); if (sig == rosterSig) return; rosterSig = sig;
            rosterBox.Clear();
            foreach (var st in net.Roster)
            {
                var row = new VisualElement(); row.AddToClassList("roster-row"); row.pickingMode = PickingMode.Ignore;
                var t = new VisualElement(); t.AddToClassList("roster-team"); t.AddToClassList(st.Team == Team.Home ? "team-home" : "team-away"); t.pickingMode = PickingMode.Ignore;
                row.Add(t);
                row.Add(L((st.Slot == 0 ? "★ " : "") + st.Name + (st.Slot == net.MySlot ? "  (ты)" : ""), "roster-name", fBold));
                int hi = Array.IndexOf(HeroOrder, st.Hero);
                row.Add(L(hi >= 0 ? HName[hi] : "?", "roster-hero"));
                row.Add(L(st.Slot == 0 ? "хост" : st.Ping < 0 ? "пинг —" : "пинг " + st.Ping + " мс", "roster-ping"));
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
                case Screen.Room: return host.Net != null && host.Net.State == Online.Phase.Connecting ? wait : room;
                default: return null;
            }
        }

        public void Show(Screen s)
        {
            Current = s;
            root.style.display = s == Screen.None ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var p in new[] { main, heroes, online, room, wait }) p.Root.EnableInClassList("hidden", true);
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
                    string sig = net.IsHost + "/" + net.State;
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
                if (dir == 1) SetIndex(p, p.Index - 1);
                else if (dir == 2) SetIndex(p, p.Index + 1);
                else if (dir == 3) it.Left?.Invoke();
                else if (dir == 4) it.Right?.Invoke();
            }

            bool submit = (gp != null && gp.buttonSouth.wasPressedThisFrame) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || (!typing && kb.spaceKey.wasPressedThisFrame)));
            bool back = (gp != null && gp.buttonEast.wasPressedThisFrame) || (kb != null && (kb.escapeKey.wasPressedThisFrame || (!typing && kb.backspaceKey.wasPressedThisFrame)));
            if (submit) it.Submit?.Invoke();
            else if (back) p.Back?.Invoke();
        }
    }
}
