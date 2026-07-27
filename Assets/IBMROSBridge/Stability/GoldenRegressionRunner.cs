using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Exoa.Designer;
using Exoa.Events;
using UnityEngine;

namespace IBMROS.Bridge.Stability
{
    /// <summary>
    /// IBMROS P1 Task 1 — Golden regression harness.
    ///
    /// Fixtures are FloorMapV2 JSON saves committed as TextAssets under
    /// Resources/IBMROSGoldenFixtures/. Each fixture is loaded through the real
    /// FloorMapSerializer, given time to settle past the 0.1 s rebuild throttle, then all
    /// generated mesh stats (verts / tris / bounds) are captured as sorted lines and
    /// compared against the committed golden file.
    ///
    /// Usage (add this component to any GameObject in FloorMapEditor.unity, enter play
    /// mode, then use the component context menu):
    ///   1. "Adopt Fixtures From Saves"  — copies your real saves into the fixtures folder
    ///   2. "Capture Goldens"            — writes/overwrites the golden files
    ///   3. "Verify Goldens"             — diffs current output against the goldens
    /// Commit both the fixtures and the goldens. Run Verify after every phase.
    /// </summary>
    public class GoldenRegressionRunner : MonoBehaviour
    {
        private const string FIXTURES_RESOURCE_FOLDER = "IBMROSGoldenFixtures";
        private const string FIXTURES_ASSET_DIR = "Assets/Resources/IBMROSGoldenFixtures";
        private const string GOLDEN_SUFFIX = ".golden";
        private const float SETTLE_SECONDS = 1.0f;

        /// <summary>Result of the last Verify run, for CI/scripted checks.</summary>
        public static bool LastVerifyPassed { get; private set; }

        private bool running;

        /// <summary>
        /// Headless batch mode: when the IBMROS_GOLDEN_MODE environment variable is set
        /// ("capture" or "verify") and the floor map editor scene loads, the runner
        /// self-installs, runs all fixtures, and exits the process with 0 (pass) or 1.
        /// Launched by GoldenBatchRunner.Run via -executeMethod (see that file for the
        /// full command line). This is the roadmap's "headless bootstrap" for CI.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BatchBootstrap()
        {
            string mode = System.Environment.GetEnvironmentVariable("IBMROS_GOLDEN_MODE");
            if (string.IsNullOrEmpty(mode))
                return;
            mode = mode.Trim().ToLowerInvariant();
            if (mode != "capture" && mode != "verify")
                return; // other modes (e.g. apitest) have their own bootstraps
            if (GameObject.FindAnyObjectByType<FloorMapSerializer>() == null)
                return;
            GameObject go = new GameObject("IBMROS_GoldenBatch");
            GoldenRegressionRunner runner = go.AddComponent<GoldenRegressionRunner>();
            runner.StartCoroutine(runner.RunBatchAndExit(mode == "capture"));
        }

        private IEnumerator RunBatchAndExit(bool capture)
        {
            // Let the editor scene finish booting (UISaving start actions, menus).
            yield return new WaitForSeconds(1.5f);
            yield return RunAllFixtures(capture);
            int exitCode = (capture || LastVerifyPassed) ? 0 : 1;
            Debug.Log("[IBMROS Goldens] Batch run finished, exit code " + exitCode);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(exitCode);
#else
            Application.Quit(exitCode);
#endif
        }

        [ContextMenu("1 - Adopt Fixtures From Saves")]
        public void AdoptFixturesFromSaves()
        {
#if UNITY_EDITOR
            string savesPath = SaveSystem.Create(SaveSystem.Mode.FILE_SYSTEM).GetBasePath(HDSettings.EXT_FLOORMAP_FOLDER);
            string[] saves = Directory.Exists(savesPath) ? Directory.GetFiles(savesPath, "*.json") : new string[0];
            if (saves.Length == 0)
            {
                Debug.LogWarning("[IBMROS Goldens] No saves found in " + savesPath + " — draw and save a floor map first.");
                return;
            }
            Directory.CreateDirectory(FIXTURES_ASSET_DIR);
            foreach (string save in saves)
            {
                string dest = Path.Combine(FIXTURES_ASSET_DIR, Path.GetFileName(save));
                File.Copy(save, dest, true);
                Debug.Log("[IBMROS Goldens] Adopted fixture " + dest);
            }
            UnityEditor.AssetDatabase.Refresh();
#else
            Debug.LogWarning("[IBMROS Goldens] Adopting fixtures is an editor-only operation.");
#endif
        }

        [ContextMenu("2 - Capture Goldens")]
        public void CaptureGoldens()
        {
            StartRun(capture: true);
        }

        [ContextMenu("3 - Verify Goldens")]
        public void VerifyGoldens()
        {
            StartRun(capture: false);
        }

        private void StartRun(bool capture)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[IBMROS Goldens] Enter play mode in FloorMapEditor.unity first.");
                return;
            }
            if (running)
            {
                Debug.LogWarning("[IBMROS Goldens] A run is already in progress.");
                return;
            }
            StartCoroutine(RunAllFixtures(capture));
        }

        private IEnumerator RunAllFixtures(bool capture)
        {
            running = true;
            FloorMapSerializer serializer = GameObject.FindAnyObjectByType<FloorMapSerializer>();
            if (serializer == null)
            {
                Debug.LogError("[IBMROS Goldens] No FloorMapSerializer in scene — run inside FloorMapEditor.unity.");
                running = false;
                yield break;
            }

            List<TextAsset> fixtures = Resources.LoadAll<TextAsset>(FIXTURES_RESOURCE_FOLDER)
                .Where(t => !t.name.EndsWith(GOLDEN_SUFFIX, StringComparison.Ordinal))
                .OrderBy(t => t.name, StringComparer.Ordinal)
                .ToList();
            if (fixtures.Count == 0)
            {
                Debug.LogWarning("[IBMROS Goldens] No fixtures in Resources/" + FIXTURES_RESOURCE_FOLDER +
                    " — run 'Adopt Fixtures From Saves' first.");
                running = false;
                yield break;
            }

            int passed = 0, failed = 0;
            foreach (TextAsset fixture in fixtures)
            {
                bool loadFailed = false;
                try
                {
                    GameEditorEvents.OnRequestClearAll?.Invoke(clearFloorsUI: true, clearFloorMapUI: true, clearScene: true);
                    serializer.DeserializeToScene(fixture.text);
                    GameEditorEvents.OnFileLoaded?.Invoke(GameEditorEvents.FileType.FloorMapFile);
                }
                catch (Exception e)
                {
                    Debug.LogError("[IBMROS Goldens] FAIL " + fixture.name + " — fixture failed to load: " + e.Message);
                    failed++;
                    loadFailed = true;
                }
                if (loadFailed)
                    continue;

                // Settle past the 0.1s rebuild throttle, queued rebuilds and building pass.
                yield return new WaitForSeconds(SETTLE_SECONDS);

                List<string> lines = CaptureMeshStats();
                if (capture)
                {
                    WriteGolden(fixture.name, lines);
                    passed++;
                }
                else if (VerifyAgainstGolden(fixture.name, lines))
                    passed++;
                else
                    failed++;
            }

#if UNITY_EDITOR
            if (capture)
                UnityEditor.AssetDatabase.Refresh();
#endif
            LastVerifyPassed = !capture && failed == 0;
            string verb = capture ? "GOLDEN CAPTURE" : "GOLDEN VERIFY";
            Debug.Log("[IBMROS Goldens] " + verb + ": " + passed + "/" + (passed + failed) + " fixtures " +
                (capture ? "captured" : (failed == 0 ? "PASS" : "FAIL")));
            running = false;
        }

        /// <summary>
        /// Captures one deterministic line per generated mesh. Lines are sorted so
        /// instantiation-order nondeterminism cannot flake the diff (roadmap P1 risk note).
        /// </summary>
        public static List<string> CaptureMeshStats()
        {
            List<string> lines = new List<string>();
            MeshFilter[] filters = GameObject.FindObjectsByType<MeshFilter>();
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null)
                    continue;
                string path = TransformPath(mf.transform);
                // Editing aids, not generated geometry:
                if (path.Contains("Ghost") || path.Contains("Grid") || path.Contains("ControlPoint"))
                    continue;
                Mesh m = mf.sharedMesh;
                Bounds b = m.bounds;
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}|v:{1}|t:{2}|c:{3}|s:{4}|p:{5}",
                    path, m.vertexCount, m.triangles.Length / 3,
                    V3(b.center), V3(b.size), V3(mf.transform.position)));
            }
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        private static string V3(Vector3 v)
        {
            // Round to millimetres: rebuilds are deterministic, but float noise below
            // this scale is meaningless for regression purposes.
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})",
                Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f);
        }

        private static string TransformPath(Transform t)
        {
            StringBuilder sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }

        private void WriteGolden(string fixtureName, List<string> lines)
        {
#if UNITY_EDITOR
            Directory.CreateDirectory(FIXTURES_ASSET_DIR);
            string path = Path.Combine(FIXTURES_ASSET_DIR, fixtureName + GOLDEN_SUFFIX + ".txt");
            File.WriteAllLines(path, lines);
            Debug.Log("[IBMROS Goldens] Captured " + lines.Count + " mesh lines -> " + path);
#else
            Debug.LogWarning("[IBMROS Goldens] Capturing goldens is an editor-only operation.");
#endif
        }

        private bool VerifyAgainstGolden(string fixtureName, List<string> lines)
        {
            TextAsset golden = Resources.Load<TextAsset>(FIXTURES_RESOURCE_FOLDER + "/" + fixtureName + GOLDEN_SUFFIX);
            if (golden == null)
            {
                Debug.LogError("[IBMROS Goldens] FAIL " + fixtureName + " — no golden committed. Run Capture first.");
                return false;
            }
            List<string> expected = golden.text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (expected.SequenceEqual(lines, StringComparer.Ordinal))
            {
                Debug.Log("[IBMROS Goldens] PASS " + fixtureName + " (" + lines.Count + " meshes)");
                return true;
            }

            var missing = expected.Except(lines).Take(10).ToList();
            var unexpected = lines.Except(expected).Take(10).ToList();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[IBMROS Goldens] FAIL " + fixtureName + " — expected " + expected.Count +
                " lines, got " + lines.Count + ".");
            foreach (string l in missing)
                sb.AppendLine("  - missing:    " + l);
            foreach (string l in unexpected)
                sb.AppendLine("  + unexpected: " + l);
            Debug.LogError(sb.ToString());
            return false;
        }
    }
}
