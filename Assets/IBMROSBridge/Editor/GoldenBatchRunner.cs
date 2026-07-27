using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IBMROS.Bridge.EditorTools
{
    /// <summary>
    /// Editor entry point for headless golden regression runs (P1 harness, batch mode).
    ///
    /// Usage (editor must be closed — batch mode needs the project lock):
    ///
    ///   IBMROS_GOLDEN_MODE=verify \
    ///   "/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity" \
    ///     -batchmode -projectPath "&lt;project&gt;" \
    ///     -executeMethod IBMROS.Bridge.EditorTools.GoldenBatchRunner.Run \
    ///     -logFile golden-run.log
    ///
    /// IBMROS_GOLDEN_MODE=capture regenerates the goldens instead of verifying.
    /// Note: no -quit flag — the process exits itself with 0 (pass) or 1 (fail) via
    /// GoldenRegressionRunner.RunBatchAndExit once play mode finishes the run.
    /// </summary>
    public static class GoldenBatchRunner
    {
        private const string SCENE_PATH = "Assets/Exoa/FloorMapDesigner/Scenes/FloorMapEditor.unity";

        public static void Run()
        {
            string mode = System.Environment.GetEnvironmentVariable("IBMROS_GOLDEN_MODE");
            if (string.IsNullOrEmpty(mode))
            {
                Debug.LogError("[IBMROS Goldens] IBMROS_GOLDEN_MODE env var not set (capture|verify); aborting.");
                EditorApplication.Exit(2);
                return;
            }
            Debug.Log("[IBMROS Goldens] Batch " + mode + " starting — opening " + SCENE_PATH);
            EditorSceneManager.OpenScene(SCENE_PATH);
            // Entering play mode hands control to GoldenRegressionRunner.BatchBootstrap,
            // which runs the fixtures and exits the process with the result code.
            EditorApplication.EnterPlaymode();
        }
    }
}
