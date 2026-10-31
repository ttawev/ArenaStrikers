// Онлайн с друзьями: комната по коду через сервисы Unity (свой сервер не нужен, хост — один из игроков).
// Хост считает матч; друзья шлют ему нажатия кнопок, он рассылает им снимки матча (NetSnapshot).
// Сообщения: hello (друг → хост: имя, герой, команда), roster (хост → все: состав комнаты),
// start (хост → все: состав матча), in (друг → хост: ввод), snap (хост → все: снимок).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arena.Sim;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Game
{
    public class LobbySeat { public ulong ClientId; public int Slot; public string Name = ""; public Hero Hero; public Team Team; }

    public class Online
    {
        public enum Phase { Connecting, Lobby, InGame, Failed }
        public Phase State = Phase.Connecting;
        public string Status = "", Code = "";
        public bool IsHost;
        public int MySlot;
        public GameMode Mode = GameMode.TwoVsTwo;
        public int Difficulty = 1;
        public readonly List<LobbySeat> Roster = new List<LobbySeat>();

        /// <summary>Друг: пришёл состав матча (режим, сложность, места).</summary>
        public event Action<GameMode, int, List<Match.Seat>> MatchStarted;
        /// <summary>Хост: игрок вышел — его героем дальше играет бот.</summary>
        public readonly Queue<int> LeftSlots = new Queue<int>();
        /// <summary>Друг: полученные снимки (по времени хоста).</summary>
        public readonly List<Snap> Snaps = new List<Snap>();

        ISession session;
        NetworkManager nm;
        string myName; Hero myHero; Team myTeam;

        // ---------- вход в сервисы ----------
        static async Task EnsureSignedIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {   // каждый запуск — новый анонимный игрок: две копии игры на одном ПК не мешают друг другу
                var opts = new InitializationOptions().SetProfile("p" + UnityEngine.Random.Range(100000, 999999));
                await UnityServices.InitializeAsync(opts);
            }
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void EnsureNetworkManager()
        {
            nm = NetworkManager.Singleton;
            if (nm != null) return;
            var go = new GameObject("NetworkManager");
            go.SetActive(false); // настройки до Awake
            var ut = go.AddComponent<UnityTransport>();
            nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = ut, EnableSceneManagement = false, ConnectionApproval = false };
            nm.RunInBackground = true;
            go.SetActive(true);
            Object.DontDestroyOnLoad(go);
        }

        // ---------- создать / войти / выйти ----------
        public async void Host(string name, Hero hero)
        {
            IsHost = true; myName = name; myHero = hero; myTeam = Team.Home;
            try
            {
                Status = "Подключаюсь к сервисам Unity…";
                await EnsureSignedIn(); EnsureNetworkManager();
                Status = "Создаю комнату…";
                var opts = new SessionOptions { MaxPlayers = 8, IsPrivate = true }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(opts);
                Code = session.Code; MySlot = 0;
                Listen();
                Roster.Clear();
                Roster.Add(new LobbySeat { ClientId = nm.LocalClientId, Slot = 0, Name = name, Hero = hero, Team = Team.Home });
                State = Phase.Lobby; Status = "";
            }
            catch (Exception e) { Fail("Не удалось создать комнату: " + e.Message); }
        }

        public async void Join(string code, string name, Hero hero)
        {
            IsHost = false; myName = name; myHero = hero; myTeam = Team.Away;
            try
            {
                Status = "Подключаюсь к сервисам Unity…";
                await EnsureSignedIn(); EnsureNetworkManager();
                Status = "Вхожу в комнату " + code + "…";
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                Code = session.Code;
                Listen();
                State = Phase.Lobby; Status = "";
                SendHello();
            }
            catch (Exception e) { Fail("Не удалось войти: " + e.Message); }
        }

        public async void Leave()
        {
            Unlisten();
            var s = session; session = null;
            try { if (s != null) await s.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Arena] выход из комнаты: " + e.Message); }
            try { if (nm != null && nm.IsListening) nm.Shutdown(); } catch { }
        }

        void Fail(string msg)
        {
            Debug.LogWarning("[Arena] " + msg);
            Status = msg; State = Phase.Failed;
            Leave();
        }

        // ---------- обработчики ----------
        bool listening;
        void Listen()
        {
            var cm = nm.CustomMessagingManager;
            if (IsHost) { cm.RegisterNamedMessageHandler("hello", OnHello); cm.RegisterNamedMessageHandler("in", OnInput); }
            else { cm.RegisterNamedMessageHandler("roster", OnRoster); cm.RegisterNamedMessageHandler("start", OnStart); cm.RegisterNamedMessageHandler("snap", OnSnap); }
            nm.OnClientDisconnectCallback += OnDisconnect;
            session.RemovedFromSession += OnRemoved;
            session.Deleted += OnRemoved;
            listening = true;
        }

        void Unlisten()
        {
            if (!listening) return; listening = false;
            try
            {
                if (nm != null)
                {
                    nm.OnClientDisconnectCallback -= OnDisconnect;
                    var cm = nm.CustomMessagingManager;
                    if (cm != null) foreach (var n in new[] { "hello", "in", "roster", "start", "snap" }) cm.UnregisterNamedMessageHandler(n);
                }
                if (session != null) { session.RemovedFromSession -= OnRemoved; session.Deleted -= OnRemoved; }
            }
            catch { }
        }

        void OnRemoved() { if (!IsHost) Fail("Хост закрыл комнату"); }

        void OnDisconnect(ulong id)
        {
            if (!IsHost) { if (State != Phase.Failed) Fail("Связь с хостом потеряна"); return; }
            int i = Roster.FindIndex(s => s.ClientId == id);
            if (i < 0) return;
            LeftSlots.Enqueue(Roster[i].Slot);
            Roster.RemoveAt(i);
            SendRoster();
        }

        // ---------- комната ----------
        /// <summary>Свой герой/команда изменились.</summary>
        public void SetMine(Hero hero, Team team)
        {
            myHero = hero; myTeam = team;
            if (IsHost) { var me = Roster.Find(s => s.Slot == 0); if (me != null) { me.Hero = hero; me.Team = team; } SendRoster(); }
            else SendHello();
        }

        public void SetMode(GameMode mode, int difficulty) { Mode = mode; Difficulty = difficulty; SendRoster(); }

        void SendHello()
        {
            using var w = new FastBufferWriter(128, Allocator.Temp, 1024);
            w.WriteValueSafe(myName ?? ""); w.WriteValueSafe((byte)myHero); w.WriteValueSafe((byte)myTeam);
            nm.CustomMessagingManager.SendNamedMessage("hello", NetworkManager.ServerClientId, w, NetworkDelivery.ReliableSequenced);
        }

        void OnHello(ulong from, FastBufferReader r)
        {
            r.ReadValueSafe(out string name); r.ReadValueSafe(out byte hero); r.ReadValueSafe(out byte team);
            var s = Roster.Find(x => x.ClientId == from);
            if (s == null)
            {
                int slot = 1; while (Roster.Exists(x => x.Slot == slot)) slot++;
                if (slot > 7) return;
                s = new LobbySeat { ClientId = from, Slot = slot };
                Roster.Add(s);
            }
            s.Name = string.IsNullOrWhiteSpace(name) ? "Игрок " + s.Slot : name; s.Hero = (Hero)hero; s.Team = (Team)team;
            SendRoster();
        }

        List<ulong> Others()
        {
            var l = new List<ulong>();
            if (nm == null || !nm.IsServer) return l;
            foreach (var id in nm.ConnectedClientsIds) if (id != nm.LocalClientId) l.Add(id);
            return l;
        }

        void SendRoster()
        {
            if (!IsHost || nm == null || !nm.IsListening) return;
            var to = Others(); if (to.Count == 0) return;
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe((byte)Mode); w.WriteValueSafe((byte)Difficulty); w.WriteValueSafe((byte)Roster.Count);
            foreach (var s in Roster) { w.WriteValueSafe(s.ClientId); w.WriteValueSafe((byte)s.Slot); w.WriteValueSafe(s.Name ?? ""); w.WriteValueSafe((byte)s.Hero); w.WriteValueSafe((byte)s.Team); }
            nm.CustomMessagingManager.SendNamedMessage("roster", to, w, NetworkDelivery.ReliableSequenced);
        }

        void OnRoster(ulong from, FastBufferReader r)
        {
            r.ReadValueSafe(out byte mode); r.ReadValueSafe(out byte diff); r.ReadValueSafe(out byte n);
            Mode = (GameMode)mode; Difficulty = diff;
            Roster.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = new LobbySeat();
                r.ReadValueSafe(out s.ClientId); r.ReadValueSafe(out byte slot); r.ReadValueSafe(out s.Name); r.ReadValueSafe(out byte hero); r.ReadValueSafe(out byte team);
                s.Slot = slot; s.Hero = (Hero)hero; s.Team = (Team)team;
                Roster.Add(s);
                if (s.ClientId == nm.LocalClientId) MySlot = slot;
            }
        }

        // ---------- начало матча ----------
        public void SendStart(Match m)
        {
            State = Phase.InGame;
            var to = Others(); if (to.Count == 0) return;
            using var w = new FastBufferWriter(128, Allocator.Temp, 4096);
            w.WriteValueSafe((byte)m.Mode); w.WriteValueSafe((byte)m.Difficulty); w.WriteValueSafe((byte)m.Players.Count);
            foreach (var p in m.Players) { w.WriteValueSafe((byte)p.Team); w.WriteValueSafe((byte)p.Hero); w.WriteValueSafe((sbyte)p.HumanSlot); }
            nm.CustomMessagingManager.SendNamedMessage("start", to, w, NetworkDelivery.ReliableSequenced);
            hookIds.Clear(); wallIds.Clear(); outEvents.Clear(); sendT = 0;
            for (int i = 0; i < 8; i++) { inputs[i] = default; doneDash[i] = doneAbil[i] = doneBall[i] = 0; }
        }

        void OnStart(ulong from, FastBufferReader r)
        {
            r.ReadValueSafe(out byte mode); r.ReadValueSafe(out byte diff); r.ReadValueSafe(out byte n);
            var seats = new List<Match.Seat>();
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out byte team); r.ReadValueSafe(out byte hero); r.ReadValueSafe(out sbyte slot);
                seats.Add(new Match.Seat { Team = (Team)team, Hero = (Hero)hero, Slot = slot });
            }
            Snaps.Clear(); timeOffset = float.NaN;
            State = Phase.InGame;
            MatchStarted?.Invoke((GameMode)mode, diff, seats);
        }

        // ---------- ввод друзей (друг → хост) ----------
        struct NetInput { public float Mx, My; public byte Held, Dash, Abil, Ball; }
        readonly NetInput[] inputs = new NetInput[8];
        readonly byte[] doneDash = new byte[8], doneAbil = new byte[8], doneBall = new byte[8];
        byte myDash, myAbil, myBall; float inT;

        /// <summary>Друг: отправить ввод хосту (кнопки-нажатия считаются счётчиком, чтобы не теряться).</summary>
        public void SendInput(InputCommand c, float dt)
        {
            if (c.Dash) myDash++; if (c.Ability) myAbil++; if (c.BallToMe) myBall++;
            inT -= dt; if (inT > 0) return; inT = 1f / 60;
            if (nm == null || !nm.IsConnectedClient) return;
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(Mathf.FloatToHalf(c.MoveX)); w.WriteValueSafe(Mathf.FloatToHalf(c.MoveY));
            w.WriteValueSafe((byte)((c.Kick ? 1 : 0) | (c.Lob ? 2 : 0) | (c.CurveLeft ? 4 : 0) | (c.CurveRight ? 8 : 0)));
            w.WriteValueSafe(myDash); w.WriteValueSafe(myAbil); w.WriteValueSafe(myBall);
            nm.CustomMessagingManager.SendNamedMessage("in", NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
        }

        void OnInput(ulong from, FastBufferReader r)
        {
            var s = Roster.Find(x => x.ClientId == from); if (s == null) return;
            r.ReadValueSafe(out ushort mx); r.ReadValueSafe(out ushort my);
            var x = new NetInput { Mx = Mathf.HalfToFloat(mx), My = Mathf.HalfToFloat(my) };
            r.ReadValueSafe(out x.Held); r.ReadValueSafe(out x.Dash); r.ReadValueSafe(out x.Abil); r.ReadValueSafe(out x.Ball);
            inputs[s.Slot] = x;
        }

        /// <summary>Хост: команда для слота на один шаг симуляции.</summary>
        public InputCommand TakeInput(int slot)
        {
            var x = inputs[slot];
            var c = new InputCommand { MoveX = x.Mx, MoveY = x.My, Kick = (x.Held & 1) != 0, Lob = (x.Held & 2) != 0, CurveLeft = (x.Held & 4) != 0, CurveRight = (x.Held & 8) != 0 };
            if (doneDash[slot] != x.Dash) { c.Dash = true; doneDash[slot]++; }
            if (doneAbil[slot] != x.Abil) { c.Ability = true; doneAbil[slot]++; }
            if (doneBall[slot] != x.Ball) { c.BallToMe = true; doneBall[slot]++; }
            return c;
        }

        // ---------- снимки (хост → друзья) ----------
        readonly Dictionary<Hook, ushort> hookIds = new Dictionary<Hook, ushort>();
        readonly Dictionary<IceWall, ushort> wallIds = new Dictionary<IceWall, ushort>();
        readonly List<SimEvent> outEvents = new List<SimEvent>();
        float sendT;

        public void AddEvents(List<SimEvent> evts) { if (IsHost) outEvents.AddRange(evts); }

        public void HostTick(Match m, float simTime, float dt)
        {
            sendT -= dt; if (sendT > 0) return; sendT = 1f / 30;
            var to = Others();
            if (to.Count > 0)
            {
                using var w = new FastBufferWriter(1024, Allocator.Temp, 16384);
                NetSnapshot.Write(w, m, simTime, outEvents, hookIds, wallIds);
                nm.CustomMessagingManager.SendNamedMessage("snap", to, w, w.Length > 1100 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.UnreliableSequenced);
            }
            outEvents.Clear();
            if (hookIds.Count > 32) { var keep = new HashSet<Hook>(m.Hooks); var dead = new List<Hook>(); foreach (var k in hookIds.Keys) if (!keep.Contains(k)) dead.Add(k); foreach (var k in dead) hookIds.Remove(k); }
            if (wallIds.Count > 32) { var keep = new HashSet<IceWall>(m.Walls); var dead = new List<IceWall>(); foreach (var k in wallIds.Keys) if (!keep.Contains(k)) dead.Add(k); foreach (var k in dead) wallIds.Remove(k); }
        }

        float timeOffset = float.NaN; // время хоста минус своё
        float lastEventT = -1;

        void OnSnap(ulong from, FastBufferReader r)
        {
            Snap s;
            try { s = NetSnapshot.Read(r); } catch (Exception e) { Debug.LogWarning("[Arena] плохой снимок: " + e.Message); return; }
            if (Snaps.Count > 0 && s.T <= Snaps[Snaps.Count - 1].T) return; // старый
            Snaps.Add(s);
            if (Snaps.Count > 40) Snaps.RemoveAt(0);
            float off = s.T - Time.unscaledTime;
            timeOffset = float.IsNaN(timeOffset) ? off : (off > timeOffset ? Mathf.Lerp(timeOffset, off, 0.3f) : Mathf.Lerp(timeOffset, off, 0.03f));
        }

        /// <summary>Друг: показать матч на момент «сейчас минус задержка»; fx получает события этого отрезка.</summary>
        public void ClientTick(Match m, float delay, Action<SimEvent> fx)
        {
            if (Snaps.Count == 0 || float.IsNaN(timeOffset)) return;
            float rt = Time.unscaledTime + timeOffset - delay;
            int i = Snaps.Count - 1;
            while (i > 0 && Snaps[i - 1].T > rt) i--;
            Snap b = Snaps[i], a = i > 0 ? Snaps[i - 1] : b;
            if (rt >= b.T) a = b; // впереди снимков нет — показываем последний
            float t = b.T > a.T ? Mathf.Clamp01((rt - a.T) / (b.T - a.T)) : 1;
            NetSnapshot.Apply(m, a, b, t);
            foreach (var s in Snaps)
            {
                if (s.T <= lastEventT || s.T > rt) continue;
                foreach (var e in s.Events) fx(NetSnapshot.EventFor(m, e));
                lastEventT = s.T;
            }
        }

        public bool Connected => nm != null && nm.IsListening && (IsHost || nm.IsConnectedClient);
    }
}
