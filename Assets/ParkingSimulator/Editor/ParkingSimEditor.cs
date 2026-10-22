using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ParkingSim.EditorTools
{
    public static class ParkingSimEditor
    {
        const string ScenePath = "Assets/Scenes/ParkingSimulator.unity";

        [MenuItem("Parking Simulator/シーンを作成して開く")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Parking Simulator").AddComponent<GameManager>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Parking Simulator/Mac 用にビルド")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/ParkingSimulator.app");

        [MenuItem("Parking Simulator/Windows 用にビルド")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/ParkingSimulator.exe");

        static void Build(BuildTarget target, string path)
        {
            if (!File.Exists(ScenePath)) CreateScene();
            PlayerSettings.productName = "Impreza Parking Simulator";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, path, target, BuildOptions.None);
            Debug.Log($"Build {target}: {report.summary.result} -> {path}");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
