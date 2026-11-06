// Автотесты симуляции из Unity: меню «Arena → Автотесты симуляции», отчёт — Logs/arena-tests.log.
// Тесты — Assets/Arena/Scripts/Sim/Tests/SimSelfTest.cs (тот же код гоняет и Tools/SimTests без Unity).
// Запуск без меню: файл Logs/run-sim-tests.flag (Claude кладёт его сам; Unity запустит, когда скрипты пересоберутся).
using System.IO;
using Arena.Sim;
using UnityEditor;
using UnityEngine;

namespace Arena.EditorTools
{
    [InitializeOnLoad]
    static class ArenaSimTests
    {
        static double nextCheck;
        static string LogsDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        static string Flag => Path.Combine(LogsDir, "run-sim-tests.flag");
        static readonly System.DateTime loadedAt = System.DateTime.Now;

        static ArenaSimTests() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            nextCheck = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(Flag)) return;
            foreach (var f in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Arena/Scripts"), "*.cs", SearchOption.AllDirectories))
                if (File.GetLastWriteTime(f) > loadedAt) { AssetDatabase.Refresh(); return; } // сначала пересобрать
            try { File.Delete(Flag); } catch { return; }
            Run(false);
        }

        [MenuItem("Arena/Автотесты симуляции (быстро)")]
        static void RunQuick() => Run(true);

        [MenuItem("Arena/Автотесты симуляции (полностью)")]
        static void RunFull() => Run(false);

        static void Run(bool quick)
        {
            EditorUtility.DisplayProgressBar("Арена", "Автотесты симуляции…", 0.5f);
            SimSelfTest.Result r;
            try { r = SimSelfTest.RunAll(quick, null); }
            finally { EditorUtility.ClearProgressBar(); }
            Directory.CreateDirectory(LogsDir);
            string path = Path.Combine(LogsDir, "arena-tests.log");
            File.WriteAllText(path, r.Report.ToString());
            string head = r.Failed == 0 && r.IssueCount.Count == 0 ? "Все автотесты пройдены" : "Автотесты: есть проблемы";
            if (r.Failed == 0 && r.IssueCount.Count == 0) Debug.Log("[Arena] " + head + " (" + r.Passed + " сценариев). Отчёт: " + path);
            else Debug.LogWarning("[Arena] " + head + ": провалено " + r.Failed + ", нарушений " + r.IssueCount.Count + ". Отчёт: " + path);
        }
    }
}
