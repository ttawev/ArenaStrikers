// Лаборатория ботов: гоняет матчи ботов без графики (симуляция — чистый C#) и пишет отчёт в Logs/bot-lab.log:
// голы, автоголы, пасы низом/верхом и сколько доходит, удары, выносы, насколько боты толпятся.
// Запуск: меню «Arena → Лаборатория ботов» или файл Logs/run-bot-lab.flag (Claude кладёт его сам; Unity запустит при обновлении).
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arena.Sim;
using UnityEditor;
using UnityEngine;

namespace Arena.EditorTools
{
    [InitializeOnLoad]
    static class ArenaBotLab
    {
        static double nextCheck;
        static string LogsDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        static string Flag => Path.Combine(LogsDir, "run-bot-lab.flag");

        static readonly System.DateTime loadedAt = System.DateTime.Now;
        static ArenaBotLab() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            nextCheck = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(Flag)) return;
            // скрипты поменялись после загрузки — сначала пусть Unity пересоберёт, иначе прогоним старую версию ботов
            foreach (var f in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Arena/Scripts"), "*.cs", SearchOption.AllDirectories))
                if (File.GetLastWriteTime(f) > loadedAt) { AssetDatabase.Refresh(); return; }
            try { File.Delete(Flag); } catch { return; }
            Run();
        }

        class Acc
        {
            public int Matches, Goals, Own, Defl, Saves, PGT, PGO, PLT, PLO, Shots, Clears, Waits, Cancels, Samples, Close, Crowd;
            public double NearSum, CrowdSum; public float Secs;
            public readonly List<string> OwnLines = new List<string>(), Notes = new List<string>();
        }

        [MenuItem("Arena/Лаборатория ботов (прогнать матчи)")]
        static void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sb = new StringBuilder();
            sb.AppendLine("Лаборатория ботов " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var summary = new StringBuilder();
            foreach (var (mode, count) in new[] { (GameMode.FourVsFour, 40), (GameMode.TwoVsTwo, 30) })
            {
                var a = new Acc();
                for (int i = 0; i < count; i++) Play(mode, 1000 + i, 2, a);
                float m = Mathf.Max(1, a.Matches);
                string line = string.Format(
                    "{0}: матчей {1} | голов/матч {2:0.0}, автоголов всего {3}, после рикошета {17} | пас низом {4} (дошло {5:0}%) , навес {6} (дошло {7:0}%) на матч {8:0.0}/{9:0.0} | удары/матч {10:0.0} (голов на удар {18:0}%), сейвы вратарей/матч {19:0.0}, выносы/матч {11:0.0} | ждали разворота {12}, отменили {13} | ближайший партнёр в среднем {14:0} | «впритык» (<150) {15:0.0}% | у мяча (<250) игроков {16:0.0}",
                    mode, a.Matches, a.Goals / m, a.Own, a.PGT, 100f * a.PGO / Mathf.Max(1, a.PGT), a.PLT, 100f * a.PLO / Mathf.Max(1, a.PLT),
                    a.PGT / m, a.PLT / m, a.Shots / m, a.Clears / m, a.Waits, a.Cancels, a.NearSum / Mathf.Max(1, a.Samples), 100f * a.Close / Mathf.Max(1, a.Samples), a.CrowdSum / Mathf.Max(1, a.Crowd), a.Defl, 100f * a.Goals / Mathf.Max(1, a.Shots), a.Saves / m);
                sb.AppendLine(line); summary.AppendLine(line);
                foreach (var l in a.OwnLines) sb.AppendLine("   " + l);
                int shown = 0; foreach (var l in a.Notes) { if (shown++ > 15) break; sb.AppendLine("   · " + l); }
            }
            sb.AppendLine("время " + sw.ElapsedMilliseconds + " мс");
            Directory.CreateDirectory(LogsDir);
            File.WriteAllText(Path.Combine(LogsDir, "bot-lab.log"), sb.ToString());
            File.AppendAllText(Path.Combine(LogsDir, "bot-lab-history.log"), System.DateTime.Now.ToString("MM-dd HH:mm") + "\n" + summary);
            Debug.Log("[Arena] Лаборатория ботов готова: Logs/bot-lab.log (" + sw.ElapsedMilliseconds + " мс)");
        }

        static void Play(GameMode mode, int seed, int diff, Acc a)
        {
            var m = new Match(new Tuning(), seed);
            m.Setup(mode, Hero.Fire, diff);
            foreach (var p in m.Players) p.HumanSlot = -1;
            var noInput = new InputCommand[8];
            const float DT = 1f / 120;
            float sampleT = 0, t = 0;
            int guard = 0;
            while (m.Phase == MatchPhase.Playing && guard++ < 200 * 120)
            {
                m.Step(DT, noInput); t += DT;
                m.Events.Clear();
                foreach (var l in m.Log)
                {
                    if (l.StartsWith("АВТОГОЛ") || l.StartsWith("рикошет")) a.OwnLines.Add("сид " + seed + ": " + l);
                    else a.Notes.Add("сид " + seed + ": " + l);
                }
                m.Log.Clear();
                sampleT += DT;
                if (sampleT >= 0.5f && m.Freeze <= 0)
                {
                    sampleT = 0;
                    foreach (var p in m.Players)
                    {
                        float near = 1e9f;
                        foreach (var q in m.Players) if (q != p && q.Team == p.Team) near = Mathf.Min(near, Mathf.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)));
                        if (near < 1e8f) { a.NearSum += near; a.Samples++; if (near < 150) a.Close++; }
                    }
                    int crowd = 0; foreach (var p in m.Players) if (Mathf.Sqrt((p.X - m.Ball.X) * (p.X - m.Ball.X) + (p.Y - m.Ball.Y) * (p.Y - m.Ball.Y)) < 250) crowd++;
                    a.CrowdSum += crowd; a.Crowd++;
                }
            }
            var s = m.Stats;
            a.Matches++; a.Goals += s.Goals; a.Own += s.OwnGoals; a.Defl += s.Deflected; a.Saves += s.Saves; a.PGT += s.PassGroundTry; a.PGO += s.PassGroundOk; a.PLT += s.PassLobTry; a.PLO += s.PassLobOk;
            a.Shots += s.Shots; a.Clears += s.Clears; a.Waits += s.TurnWaits; a.Cancels += s.KickCancels; a.Secs += t;
        }
    }
}
