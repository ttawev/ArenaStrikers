// Menu "Arena → Собрать сцену": creates Assets/Arena/Arena.unity with everything the game needs,
// adds it to the build and opens it. Run it once; press Play afterwards.
using System.Linq;
using Arena.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arena.EditorTools
{
    static class ArenaMenu
    {
        const string ScenePath = "Assets/Arena/Arena.unity";

        [MenuItem("Arena/Собрать сцену", priority = 1)]
        static void BuildScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();

            new GameObject("Arena").AddComponent<ArenaGame>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorUtility.DisplayDialog("SpellStrike", "Сцена собрана: " + ScenePath + "\n\nНажми Play, чтобы играть.", "Ок");
        }

        [MenuItem("Arena/Открыть сцену", priority = 2)]
        static void OpenScene()
        {
            if (!System.IO.File.Exists(ScenePath)) { BuildScene(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
