// Сборка выделенного сервера: меню «Arena → Собрать сервер (Linux)». Кладёт сервер в Builds/Server
// (ArenaServer.x86_64 + папка _Data) — эту папку целиком загружают на VPS (инструкция — Tools/Server/README.md).
// Нужен модуль Unity «Linux Dedicated Server Build Support».
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ArenaServerBuild
{
    const string OutDir = "Builds/Server";
    const string Scene = "Assets/Arena/Arena.unity";

    [MenuItem("Arena/Собрать сервер (Linux)")]
    static void Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
        {
            EditorUtility.DisplayDialog("Нет модуля Linux",
                "В Unity не установлен модуль для Linux.\n\nUnity Hub → Installs → у Unity 6.6 нажать ⚙ → Add modules → отметить «Linux Dedicated Server Build Support» (и «Linux Build Support (IL2CPP)», если предложит) → Install. Потом перезапустить Unity и снова выбрать это меню.", "Понятно");
            return;
        }
        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutDir + "/ArenaServer.x86_64",
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server, // без графики и звука, сразу в режиме сервера (UNITY_SERVER)
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        string msg = "[Arena] сервер Linux: " + s.result + ", " + (s.totalSize / (1024 * 1024)) + " МБ, " + (int)s.totalTime.TotalSeconds + " с, ошибок " + s.totalErrors;
        Debug.Log(msg);
        File.WriteAllText("Logs/arena-server-build.log", msg + "\n" + System.DateTime.Now + "\n");
        if (s.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(OutDir + "/ArenaServer.x86_64");
        // после сборки сервера вернуть обычную платформу — иначе Play в редакторе пойдёт в режиме Dedicated Server
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
    }
}
