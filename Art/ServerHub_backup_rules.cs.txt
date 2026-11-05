// Выделенный сервер, «Найти матч»: много комнат в одном процессе (один UDP-порт).
// Игрок подключается — сервер сам сажает его в открытую комнату (матч ещё не начат, есть места) или создаёт новую.
// В комнате до 8 игроков, 4 на 4; первый вошедший — главный: может начать сразу (пустые места займут боты) или ждать.
// Автостарт: через autoStart секунд после входа первого, через fullStart — когда комната заполнилась.
// После матча комната снова открыта (с теми же игроками), автостарт заново. Все вышли — комната удаляется.
// Сообщения те же, что у комнаты с другом (Online): hello, in, rules, go → roster, start, snap;
// в roster в конце — номер комнаты, место главного, секунд до автостарта, вместимость (версия сети 3).
using System;
using System.Collections.Generic;
using Arena.Sim;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Game
{
    public class ServerRoom
    {
        public enum St { Lobby, Playing, Ended }
        public int No;
        public St State = St.Lobby;
        public readonly List<LobbySeat> Roster = new List<LobbySeat>();
        public int Difficulty = 1;
        public Match Match;
        public float Acc, SimTime, AutoT, EndT, SendT;
        public int LeaderSlot { get { int m = -1; foreach (var s in Roster) if (m < 0 || s.Slot < m) m = s.Slot; return m; } } // главный — вошедший раньше всех
        // ввод игроков по местам 0..7 (как у хоста в Online)
        public readonly Queue<(uint seq, InputCommand c)>[] InQ = new Queue<(uint, InputCommand)>[8];
        public readonly uint[] Acked = new uint[8], Queued = new uint[8];
        public readonly InputCommand[] Held = new InputCommand[8];
        public readonly bool[] PendDash = new bool[8], PendAbil = new bool[8], PendBall = new bool[8];
        public readonly InputCommand[] Cmds = new InputCommand[8];
        // снимки
        public readonly Dictionary<Hook, ushort> HookIds = new Dictionary<Hook, ushort>();
        public readonly Dictionary<IceWall, ushort> WallIds = new Dictionary<IceWall, ushort>();
        public readonly List<SimEvent> OutEvents = new List<SimEvent>();

        public ServerRoom() { for (int i = 0; i < 8; i++) InQ[i] = new Queue<(uint, InputCommand)>(); }

        public void ResetSlot(int slot)
        {
            if (slot < 0 || slot > 7) return;
            InQ[slot].Clear(); Queued[slot] = 0; Acked[slot] = 0; Held[slot] = default;
            PendDash[slot] = PendAbil[slot] = PendBall[slot] = false;
        }

        public InputCommand TakeInput(int slot)
        {
            var q = InQ[slot];
            while (q.Count > Online.MaxQueue)
            {   // очередь разрослась (рывок связи) — пропускаем старые шаги, нажатия не теряем
                var d = q.Dequeue(); Acked[slot] = d.seq;
                PendDash[slot] |= d.c.Dash; PendAbil[slot] |= d.c.Ability; PendBall[slot] |= d.c.BallToMe;
            }
            InputCommand c;
            if (q.Count > 0) { var x = q.Dequeue(); Acked[slot] = x.seq; c = x.c; }
            else c = Held[slot];
            c.Dash |= PendDash[slot]; c.Ability |= PendAbil[slot]; c.BallToMe |= PendBall[slot];
            PendDash[slot] = PendAbil[slot] = PendBall[slot] = false;
            var held = c; held.Dash = held.Ability = held.BallToMe = false; Held[slot] = held;
            return c;
        }

        public int Count(Team t) { int n = 0; foreach (var s in Roster) if (s.Team == t) n++; return n; }
    }

    public class ServerHub
    {
        public const int Capacity = 8;
        /// <summary>Секунд до автостарта после входа первого игрока / после матча.</summary>
        public static float AutoStart = 60;
        /// <summary>Секунд до старта, когда комната заполнилась.</summary>
        public static float FullStart = 5;
        /// <summary>Секунд после конца матча до того, как комната снова откроется.</summary>
        public static float AfterMatch = 8;

        readonly Tuning tuning;
        NetworkManager nm;
        public readonly List<ServerRoom> Rooms = new List<ServerRoom>();
        readonly Dictionary<ulong, ServerRoom> byClient = new Dictionary<ulong, ServerRoom>();
        int nextNo = 1;
        float pingT;
        public bool Failed { get; private set; }
        public string Status = "";
        public int Players => byClient.Count;

        // нагрузка за минуту
        double workMs, workMax; int frames; long bytesOut; float statT = 60;

        public ServerHub(Tuning tuning) { this.tuning = tuning; }

        public bool Start(ushort port)
        {
            try
            {
                nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    var go = new GameObject("NetworkManager"); go.SetActive(false);
                    var t = go.AddComponent<UnityTransport>();
                    nm = go.AddComponent<NetworkManager>();
                    nm.NetworkConfig = new NetworkConfig { NetworkTransport = t, EnableSceneManagement = false, ConnectionApproval = false };
                    nm.RunInBackground = true;
                    go.SetActive(true); Object.DontDestroyOnLoad(go);
                }
                if (nm.IsListening) nm.Shutdown();
                var ut = nm.GetComponent<UnityTransport>() ?? nm.gameObject.AddComponent<UnityTransport>();
                nm.NetworkConfig.NetworkTransport = ut;
                ut.SetConnectionData("0.0.0.0", port, "0.0.0.0");
                if (!nm.StartServer()) { Fail("Сервер не смог открыть порт " + port); return false; }
                var cm = nm.CustomMessagingManager;
                cm.RegisterNamedMessageHandler("hello", OnHello);
                cm.RegisterNamedMessageHandler("in", OnInput);
                cm.RegisterNamedMessageHandler("rules", OnRules);
                cm.RegisterNamedMessageHandler("go", OnGo);
                nm.OnClientDisconnectCallback += OnDisconnect;
                Failed = false; Status = "";
                Debug.Log("[Arena] сервер «Найти матч» слушает порт " + port + ", комнаты по " + Capacity + " игроков");
                return true;
            }
            catch (Exception e) { Fail("Не удалось запустить сервер: " + e.Message); return false; }
        }

        void Fail(string msg) { Failed = true; Status = msg; Debug.LogWarning("[Arena] " + msg); Stop(); }

        public void Stop()
        {
            try
            {
                if (nm == null) return;
                nm.OnClientDisconnectCallback -= OnDisconnect;
                var cm = nm.CustomMessagingManager;
                if (cm != null) foreach (var n in new[] { "hello", "in", "rules", "go" }) cm.UnregisterNamedMessageHandler(n);
                if (nm.IsListening) nm.Shutdown();
            }
            catch { }
            Rooms.Clear(); byClient.Clear();
        }

        // ---------- подбор комнаты ----------
        ServerRoom FindRoom()
        {   // открытая комната с наибольшим числом игроков — чтобы комнаты быстрее заполнялись
            ServerRoom best = null;
            foreach (var r in Rooms)
                if (r.State == ServerRoom.St.Lobby && r.Roster.Count < Capacity && (best == null || r.Roster.Count > best.Roster.Count)) best = r;
            if (best != null) return best;
            best = new ServerRoom { No = nextNo++, AutoT = AutoStart };
            Rooms.Add(best);
            Debug.Log("[Arena] новая комната #" + best.No + " (всего " + Rooms.Count + ")");
            return best;
        }

        static readonly Hero[] AllHeroes = { Hero.Fire, Hero.Hook, Hero.Teleport, Hero.Curve, Hero.Shade, Hero.Wall };

        /// <summary>Герой не повторяется в команде: занят — первый свободный.</summary>
        static Hero FreeHero(ServerRoom r, LobbySeat me, Hero want, Team team)
        {
            bool Taken(Hero h) => r.Roster.Exists(s => s != me && s.Team == team && s.Hero == h);
            if (!Taken(want)) return want;
            foreach (var h in AllHeroes) if (!Taken(h)) return h;
            return want;
        }

        void OnHello(ulong from, FastBufferReader rd)
        {
            rd.ReadValueSafe(out string name); rd.ReadValueSafe(out byte hero); rd.ReadValueSafe(out byte team);
            int ver = 0; try { rd.ReadValueSafe(out ver); } catch { }
            if (ver != Online.NetVersion)
            {
                Debug.LogWarning("[Arena] отклонён игрок «" + name + "»: версия " + ver + ", нужна " + Online.NetVersion);
                try { nm.DisconnectClient(from, "Версия игры не совпадает: у тебя " + (ver == 0 ? "старая" : ver.ToString()) + ", на сервере " + Online.NetVersion + ". Обнови игру."); } catch { }
                return;
            }
            if (!byClient.TryGetValue(from, out var r))
            {   // новый игрок — в открытую комнату, в команду, где меньше людей
                r = FindRoom();
                int slot = 0; while (r.Roster.Exists(x => x.Slot == slot)) slot++;
                var t = r.Count(Team.Home) <= r.Count(Team.Away) ? Team.Home : Team.Away;
                var s = new LobbySeat { ClientId = from, Slot = slot, Team = t };
                r.Roster.Add(s); byClient[from] = r; r.ResetSlot(slot);
                if (r.Roster.Count == 1) r.AutoT = AutoStart;
                s.Name = Clean(name, slot); s.Hero = FreeHero(r, s, (Hero)hero, t);
                Debug.Log("[Arena] «" + s.Name + "» → комната #" + r.No + " (" + r.Roster.Count + "/" + Capacity + ")");
            }
            else
            {   // уже в комнате: смена героя/команды (команда — если там есть место)
                var s = r.Roster.Find(x => x.ClientId == from); if (s == null) return;
                s.Name = Clean(name, s.Slot);
                var want = (Team)team;
                if (want != s.Team && r.State == ServerRoom.St.Lobby && r.Count(want) < Capacity / 2) s.Team = want;
                s.Hero = FreeHero(r, s, (Hero)hero, s.Team);
            }
            SendRoster(r);
        }

        static string Clean(string n, int slot) { n = (n ?? "").Trim(); if (n.Length > 16) n = n.Substring(0, 16); return n == "" ? "Игрок " + (slot + 1) : n; }

        bool IsLeader(ulong from, out ServerRoom r)
        {
            if (!byClient.TryGetValue(from, out r)) return false;
            var s = r.Roster.Find(x => x.ClientId == from);
            return s != null && s.Slot == r.LeaderSlot;
        }

        void OnRules(ulong from, FastBufferReader rd)
        {
            rd.ReadValueSafe(out byte mode); rd.ReadValueSafe(out byte diff);
            if (!IsLeader(from, out var r)) return;
            r.Difficulty = Mathf.Clamp(diff, 0, 2); // формат на сервере всегда 4 на 4
            SendRoster(r);
        }

        void OnGo(ulong from, FastBufferReader rd)
        {
            if (IsLeader(from, out var r) && r.State != ServerRoom.St.Playing) StartMatch(r);
        }

        void OnInput(ulong from, FastBufferReader rd)
        {
            if (!byClient.TryGetValue(from, out var r)) return;
            var s = r.Roster.Find(x => x.ClientId == from); if (s == null) return;
            int slot = Mathf.Clamp(s.Slot, 0, 7);
            rd.ReadValueSafe(out byte n);
            for (int i = 0; i < n; i++)
            {
                rd.ReadValueSafe(out uint seq); rd.ReadValueSafe(out ushort mx); rd.ReadValueSafe(out ushort my); rd.ReadValueSafe(out byte b);
                if (seq <= r.Queued[slot] || seq <= r.Acked[slot]) continue;
                var c = new InputCommand
                {
                    MoveX = Mathf.HalfToFloat(mx), MoveY = Mathf.HalfToFloat(my),
                    Kick = (b & 1) != 0, Lob = (b & 2) != 0, CurveLeft = (b & 4) != 0, CurveRight = (b & 8) != 0,
                    Dash = (b & 16) != 0, Ability = (b & 32) != 0, BallToMe = (b & 64) != 0, CallPass = (b & 128) != 0,
                };
                r.InQ[slot].Enqueue((seq, c)); r.Queued[slot] = seq;
            }
        }

        void OnDisconnect(ulong id)
        {
            if (!byClient.TryGetValue(id, out var r)) return;
            byClient.Remove(id);
            var s = r.Roster.Find(x => x.ClientId == id);
            if (s != null)
            {
                r.Roster.Remove(s); r.ResetSlot(s.Slot);
                if (r.Match != null && r.State != ServerRoom.St.Lobby) { var p = r.Match.Players.Find(x => x.HumanSlot == s.Slot); if (p != null) p.HumanSlot = -1; } // дальше за него бот
                Debug.Log("[Arena] «" + s.Name + "» вышел из комнаты #" + r.No + " (" + r.Roster.Count + "/" + Capacity + ")");
            }
            if (r.Roster.Count == 0) { Rooms.Remove(r); Debug.Log("[Arena] комната #" + r.No + " закрыта (всего " + Rooms.Count + ")"); }
            else SendRoster(r);
        }

        // ---------- матч ----------
        void StartMatch(ServerRoom r)
        {
            var humans = new List<Match.Seat>();
            foreach (var s in r.Roster) humans.Add(new Match.Seat { Team = s.Team, Hero = s.Hero, Slot = s.Slot });
            r.Match = new Match(tuning, Environment.TickCount ^ (r.No * 7919));
            r.Match.LocalSlot = 99; // своего игрока у сервера нет
            r.Match.PlayIntro = true; // выход команд — его видят все игроки комнаты
            r.Match.SetupLineup(GameMode.FourVsFour, humans, r.Difficulty);
            r.Acc = 0; r.SimTime = 0; r.SendT = 0;
            r.HookIds.Clear(); r.WallIds.Clear(); r.OutEvents.Clear();
            for (int i = 0; i < 8; i++) r.ResetSlot(i);
            r.State = ServerRoom.St.Playing;
            var to = Ids(r);
            if (to.Count > 0)
            {
                using var w = new FastBufferWriter(128, Allocator.Temp, 4096);
                var m = r.Match;
                w.WriteValueSafe((byte)m.Mode); w.WriteValueSafe((byte)m.Difficulty); w.WriteValueSafe((byte)m.Players.Count);
                foreach (var p in m.Players) { w.WriteValueSafe((byte)p.Team); w.WriteValueSafe((byte)p.Hero); w.WriteValueSafe((sbyte)p.HumanSlot); }
                nm.CustomMessagingManager.SendNamedMessage("start", to, w, NetworkDelivery.ReliableSequenced);
            }
            Debug.Log("[Arena] комната #" + r.No + ": матч начат, людей " + humans.Count + ", ботов " + (Capacity - humans.Count));
        }

        List<ulong> Ids(ServerRoom r) { var l = new List<ulong>(r.Roster.Count); foreach (var s in r.Roster) l.Add(s.ClientId); return l; }

        void SendRoster(ServerRoom r)
        {
            if (nm == null || !nm.IsListening) return;
            var to = Ids(r); if (to.Count == 0) return;
            using var w = new FastBufferWriter(256, Allocator.Temp, 4096);
            w.WriteValueSafe((byte)GameMode.FourVsFour); w.WriteValueSafe((byte)r.Difficulty); w.WriteValueSafe((byte)r.Roster.Count);
            foreach (var s in r.Roster) { w.WriteValueSafe(s.ClientId); w.WriteValueSafe((byte)s.Slot); w.WriteValueSafe(s.Name ?? ""); w.WriteValueSafe((byte)s.Hero); w.WriteValueSafe((byte)s.Team); w.WriteValueSafe((short)s.Ping); }
            // версия 3: комната, главный, автостарт, вместимость
            w.WriteValueSafe((ushort)r.No); w.WriteValueSafe((sbyte)r.LeaderSlot);
            w.WriteValueSafe((short)(r.State == ServerRoom.St.Lobby ? Mathf.CeilToInt(Mathf.Max(0, r.AutoT)) : -1));
            w.WriteValueSafe((byte)Capacity);
            nm.CustomMessagingManager.SendNamedMessage("roster", to, w, NetworkDelivery.ReliableSequenced);
        }

        int Rtt(ulong id)
        {
            try { var t = nm.NetworkConfig.NetworkTransport; return t != null && nm.IsListening ? (int)t.GetCurrentRtt(id) : -1; }
            catch { return -1; }
        }

        /// <summary>Каждый кадр сервера.</summary>
        public void Tick(float dt)
        {
            if (nm == null || Failed) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const float DT = Online.DT;
            for (int ri = Rooms.Count - 1; ri >= 0; ri--)
            {
                var r = Rooms[ri];
                switch (r.State)
                {
                    case ServerRoom.St.Lobby:
                        if (r.Roster.Count == 0) break;
                        if (r.Roster.Count >= Capacity) r.AutoT = Mathf.Min(r.AutoT, FullStart);
                        r.AutoT -= dt;
                        if (r.AutoT <= 0) StartMatch(r);
                        break;
                    case ServerRoom.St.Playing:
                        r.Acc += dt;
                        while (r.Acc >= DT)
                        {
                            for (int k = 0; k < 8; k++) r.Cmds[k] = r.TakeInput(k);
                            r.Match.Step(DT, r.Cmds);
                            r.SimTime += DT; r.Acc -= DT;
                            r.OutEvents.AddRange(r.Match.Events); r.Match.Events.Clear();
                            foreach (var line in r.Match.Log) Debug.Log("[Arena] #" + r.No + " " + line);
                            r.Match.Log.Clear();
                            if (r.Match.Phase == MatchPhase.Ended)
                            {
                                r.State = ServerRoom.St.Ended; r.EndT = AfterMatch;
                                Debug.Log("[Arena] комната #" + r.No + ": матч окончен " + r.Match.ScoreHome + ":" + r.Match.ScoreAway);
                                break;
                            }
                        }
                        Snap(r, dt);
                        break;
                    case ServerRoom.St.Ended:
                        Snap(r, dt);
                        r.EndT -= dt;
                        if (r.EndT <= 0) { r.State = ServerRoom.St.Lobby; r.AutoT = AutoStart; SendRoster(r); } // комната снова открыта
                        break;
                }
            }
            pingT -= dt;
            if (pingT <= 0)
            {   // раз в секунду: пинги и состав (с секундами до автостарта)
                pingT = 1;
                foreach (var r in Rooms) { foreach (var s in r.Roster) s.Ping = Rtt(s.ClientId); SendRoster(r); }
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            workMs += ms; workMax = Math.Max(workMax, ms); frames++;
            statT -= dt;
            if (statT <= 0) LogLoad();
        }

        void Snap(ServerRoom r, float dt)
        {
            r.SendT -= dt; if (r.SendT > 0) return;
            r.SendT += 1f / Mathf.Max(10, Online.SnapRate); if (r.SendT < 0) r.SendT = 0;
            var to = Ids(r);
            if (to.Count > 0)
            {
                using var w = new FastBufferWriter(1024, Allocator.Temp, 16384);
                NetSnapshot.Write(w, r.Match, r.SimTime, r.OutEvents, r.HookIds, r.WallIds, r.Acked);
                nm.CustomMessagingManager.SendNamedMessage("snap", to, w, w.Length > 1100 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.UnreliableSequenced);
                bytesOut += (long)w.Length * to.Count;
            }
            r.OutEvents.Clear();
            if (r.HookIds.Count > 32) { var keep = new HashSet<Hook>(r.Match.Hooks); var dead = new List<Hook>(); foreach (var k in r.HookIds.Keys) if (!keep.Contains(k)) dead.Add(k); foreach (var k in dead) r.HookIds.Remove(k); }
            if (r.WallIds.Count > 32) { var keep = new HashSet<IceWall>(r.Match.Walls); var dead = new List<IceWall>(); foreach (var k in r.WallIds.Keys) if (!keep.Contains(k)) dead.Add(k); foreach (var k in dead) r.WallIds.Remove(k); }
        }

        /// <summary>Раз в минуту — нагрузка в журнал: по ней видно, сколько комнат выдержит сервер.</summary>
        void LogLoad()
        {
            float span = 60 - statT; statT = 60;
            int playing = 0; foreach (var r in Rooms) if (r.State != ServerRoom.St.Lobby) playing++;
            double avg = frames > 0 ? workMs / frames : 0, busy = span > 0 ? workMs / (span * 1000) * 100 : 0;
            double kbs = span > 0 ? bytesOut / 1024.0 / span : 0;
            Debug.Log("[Arena] нагрузка: комнат " + Rooms.Count + " (матчей " + playing + "), игроков " + Players
                      + " | работа " + avg.ToString("0.00") + " мс/кадр, макс " + workMax.ToString("0.0") + " мс, занято ядра " + busy.ToString("0.0") + "%"
                      + " | исходящий " + kbs.ToString("0") + " КБ/с ≈ " + (kbs * 8 / 1024).ToString("0.00") + " Мбит/с");
            workMs = 0; workMax = 0; frames = 0; bytesOut = 0;
        }
    }
}
