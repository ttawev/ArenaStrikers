// Сборка для браузера (WebGL): меню «Arena → Собрать WebGL (браузер)».
// Кладёт игру в Builds/WebGL. Сжатие Gzip с распаковкой в браузере — сборка открывается на любом хостинге (itch.io и т.п.)
// без настройки сервера. «…и запустить» — Unity сам поднимет локальный сервер и откроет игру в браузере.
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ArenaWebGLBuild
{
    const string OutDir = "Builds/WebGL";
    const string Scene = "Assets/Arena/Arena.unity";

    [MenuItem("Arena/Собрать WebGL (браузер)")] static void Build() => Run(false);
    [MenuItem("Arena/Собрать WebGL и запустить в браузере")] static void BuildRun() => Run(true);

    static void Run(bool andRun)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            EditorUtility.DisplayDialog("Нет модуля WebGL",
                "В Unity не установлен модуль «WebGL Build Support».\n\nUnity Hub → Installs → у Unity 6.6 нажать ⚙ → Add modules → отметить WebGL Build Support → Install. Потом перезапустить Unity и снова выбрать это меню.", "Понятно");
            return;
        }
        EditorApplication.ExecuteMenuItem("Arena/Обновить шейдеры для сборки"); // шейдеры материалов, созданных кодом

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;   // распаковка в браузере, если сервер не отдаёт заголовки сжатия
        PlayerSettings.WebGL.dataCaching = true;              // повторный запуск — без повторной загрузки
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.runInBackground = true;

        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = andRun ? BuildOptions.AutoRunPlayer : BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        string msg = "[Arena] WebGL: " + s.result + ", " + (s.totalSize / (1024 * 1024)) + " МБ, " + (int)s.totalTime.TotalSeconds + " с, ошибок " + s.totalErrors;
        Debug.Log(msg);
        File.WriteAllText("Logs/arena-webgl-build.log", msg + "\n" + System.DateTime.Now + "\n");
        if (s.result == BuildResult.Succeeded && !andRun) EditorUtility.RevealInFinder(OutDir + "/index.html");
    }
}
