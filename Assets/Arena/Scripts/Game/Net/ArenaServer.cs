// Выделенный сервер: та же игра, запущенная с ключом -server (или собранная как Dedicated Server — тогда сама).
// Без графики, звука и своего игрока: слушает порт, держит комнату, считает матч и рассылает снимки.
// Запуск: ArenaStrikers -batchmode -nographics -server [-port 7777] [-name "Арена"]
// Первый вошедший игрок — главный: выбирает формат и сложность и начинает матч. Все вышли — сервер снова ждёт.
using System;
using Arena.Sim;
using UnityEngine;

namespace Arena.Game
{
    public class ArenaServer : MonoBehaviour
    {
        public Tuning tuning;

        /// <summary>Запущены ли мы как сервер.</summary>
        public static bool IsServerMode
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                foreach (var a in Environment.GetCommandLineArgs()) if (a == "-server") return true;
                return false;
#endif
            }
        }

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        enum S { Lobby, Playing, Ended }
        S state = S.Lobby;
        Match match; Online net;
        float acc, simTime, statusT, restartT;
        readonly InputCommand[] cmds = new InputCommand[8];
        const float DT = 1f / 120;

        void Start()
        {
            Application.targetFrameRate = 120;    // серверу хватит; без этого батч-режим крутит кадры без остановки
            Application.runInBackground = true;
            ushort port = ushort.TryParse(Arg("-port", "7777"), out var p) ? p : Online.DirectPort;
            Online.SnapRate = int.TryParse(Arg("-snaprate", "120"), out var r) ? Mathf.Clamp(r, 20, 120) : 120; // снимков в секунду
            match = new Match(tuning, Environment.TickCount);
            StartNet(port);
        }

        void StartNet(ushort port)
        {
            net = new Online();
            net.HostServer(port);
            Debug.Log("[Arena] выделенный сервер запущен, порт " + port + ". Ждём игроков.");
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (net == null) return;
            if (net.State == Online.Phase.Failed)
            {   // порт занят, ошибка сети — пробуем заново через 5 с
                restartT += dt;
                if (restartT > 5) { restartT = 0; Debug.LogWarning("[Arena] перезапуск сервера: " + net.Status); net.Leave(); StartNet(ushort.TryParse(Arg("-port", "7777"), out var p) ? p : Online.DirectPort); }
                return;
            }
            net.Tick(dt);

            // вышедших заменяют боты; все вышли — назад в комнату
            while (net.LeftSlots.Count > 0)
            {
                int slot = net.LeftSlots.Dequeue();
                var pl = match.Players.Find(x => x.HumanSlot == slot);
                if (pl != null) pl.HumanSlot = -1;
                Debug.Log("[Arena] игрок с места " + slot + " вышел, в комнате: " + net.Roster.Count);
            }
            if (state != S.Lobby && net.Roster.Count == 0) { state = S.Lobby; net.State = Online.Phase.Lobby; Debug.Log("[Arena] все вышли — ждём игроков"); }

            if (net.StartRequested)
            {
                net.StartRequested = false;
                if (net.Roster.Count > 0) StartMatch();
            }

            if (state == S.Playing)
            {
                acc += dt;
                while (acc >= DT)
                {
                    for (int k = 0; k < cmds.Length; k++) cmds[k] = net.TakeInput(k);
                    match.Step(DT, cmds);
                    simTime += DT; acc -= DT;
                    net.AddEvents(match.Events); match.Events.Clear();
                    foreach (var line in match.Log) Debug.Log("[Arena] " + line);
                    match.Log.Clear();
                    if (match.Phase == MatchPhase.Ended) { state = S.Ended; Debug.Log("[Arena] матч окончен " + match.ScoreHome + ":" + match.ScoreAway); break; }
                }
            }
            if (state != S.Lobby) net.HostTick(match, simTime, dt);

            statusT -= dt;
            if (statusT <= 0)
            {
                statusT = 60;
                var sb = new System.Text.StringBuilder();
                foreach (var s in net.Roster) sb.Append(" ").Append(s.Name).Append(" (").Append(s.Ping).Append(" мс)");
                Debug.Log("[Arena] " + state + ", игроков " + net.Roster.Count + ":" + sb);
            }
        }

        void StartMatch()
        {
            var humans = new System.Collections.Generic.List<Match.Seat>();
            int home = 0, away = 0;
            foreach (var st in net.Roster) { humans.Add(new Match.Seat { Team = st.Team, Hero = st.Hero, Slot = st.Slot }); if (st.Team == Team.Home) home++; else away++; }
            var mode = net.Mode;
            int need = Mathf.Max(home, away);
            if (Match.TeamSize(mode) < need) mode = need <= 2 ? GameMode.TwoVsTwo : GameMode.FourVsFour;
            match.T = tuning; match.LocalSlot = 99; // своего игрока у сервера нет
            match.SetupLineup(mode, humans, net.Difficulty);
            acc = 0;
            net.SendStart(match);
            state = S.Playing;
            Debug.Log("[Arena] матч начат: " + mode + ", людей " + humans.Count);
        }

        void OnApplicationQuit() { net?.Leave(); }
    }
}
