// Сборка игры для Windows: меню «Arena → Собрать Windows (игра)». Кладёт в Builds/Win (ArenaStrikers.exe + папки).
// Другу отправлять папку Builds/Win целиком (zip), без *_BackUpThisFolder_ButDontShipItWithYourGame.
// После сборки сервера Unity оставляет активной платформу Linux — этот пункт сам переключит на Windows.
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ArenaWindowsBuild
{
    const string OutDir = "Builds/Win";
    const string Scene = "Assets/Arena/Arena.unity";

    [MenuItem("Arena/Собрать Windows (игра)")]
    static void Build()
    {
        EditorApplication.ExecuteMenuItem("Arena/Обновить шейдеры для сборки");
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutDir + "/ArenaStrikers.exe",
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        string msg = "[Arena] Windows: " + s.result + ", " + (s.totalSize / (1024 * 1024)) + " МБ, " + (int)s.totalTime.TotalSeconds + " с, ошибок " + s.totalErrors;
        Debug.Log(msg);
        File.WriteAllText("Logs/arena-win-build.log", msg + "\n" + System.DateTime.Now + "\n");
        if (s.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(OutDir + "/ArenaStrikers.exe");
    }
}
