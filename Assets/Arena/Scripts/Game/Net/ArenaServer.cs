// Выделенный сервер: та же игра, запущенная с ключом -server (или собранная как Dedicated Server — тогда сама).
// Без графики, звука и своего игрока: слушает порт и держит комнаты «Найти матч» (ServerHub).
// Запуск: ArenaStrikers -batchmode -nographics -server [-port 7777] [-snaprate 120] [-autostart 60]
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

        ServerHub hub;
        ushort port;
        float restartT;
        const int BusyFps = 120, IdleFps = 10; // без игроков сервер почти спит

        void Start()
        {
            Application.targetFrameRate = IdleFps;
            Application.runInBackground = true;
            port = ushort.TryParse(Arg("-port", "7777"), out var p) ? p : Online.DirectPort;
            Online.SnapRate = int.TryParse(Arg("-snaprate", "120"), out var r) ? Mathf.Clamp(r, 20, 120) : 120; // снимков в секунду
            if (float.TryParse(Arg("-autostart", "60"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)) ServerHub.AutoStart = Mathf.Clamp(a, 5, 600);
            hub = new ServerHub(tuning);
            hub.Start(port);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (hub == null) return;
            if (hub.Failed)
            {   // порт занят, ошибка сети — пробуем заново через 5 с
                restartT += dt;
                if (restartT > 5) { restartT = 0; Debug.LogWarning("[Arena] перезапуск сервера: " + hub.Status); hub.Stop(); hub = new ServerHub(tuning); hub.Start(port); }
                return;
            }
            hub.Tick(dt);
            Application.targetFrameRate = hub.Players > 0 ? BusyFps : IdleFps;
        }

        void OnApplicationQuit() { hub?.Stop(); }
    }
}
