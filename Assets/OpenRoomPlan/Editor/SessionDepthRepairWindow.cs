using System;
using System.IO;
using System.Linq;
using OpenRoomPlan.Capture;
using UnityEditor;
using UnityEngine;

namespace OpenRoomPlan.EditorTools
{
    /// <summary>
    /// One-shot repair for sessions recorded before the ARCore depth-format fix.
    ///
    /// THE BUG BEING REPAIRED. CaptureSessionRecorder.ReadFloatPlane assumed depth planes
    /// were float32 metres, but ARCore delivers XRCpuImage.Format.DepthUint16 — 16-bit
    /// millimetres at pixelStride 2. It advanced by the real stride (2) while decoding 4
    /// bytes, so it wrote OVERLAPPING windows of the uint16 plane reinterpreted as floats.
    /// Result: files of the right size and shape whose values are ~1e14 or ~0.
    ///
    /// WHY THE DATA IS RECOVERABLE. Written float k occupies source bytes [k*2, k*2+3],
    /// i.e. source uint16 pair (k, k+1). So reading the payload back as a uint16 stream and
    /// keeping every SECOND value recovers the original millimetre sample for every pixel
    /// exactly — u16[2*k] == source[k] — as long as rowStride == width*2 (no row padding,
    /// which is the case for 160x90 ARCore depth). Nothing is interpolated or guessed here.
    ///
    /// Verified against sess_20260805_114510 (Pixel 8 Pro, 423 frames): recovered depth is
    /// 1.0–3.3 m with 0.5–1.4 cm between adjacent pixels, i.e. piecewise-smooth real depth,
    /// versus 0.1% plausible pixels before repair.
    ///
    /// This is deliberately a separate, obvious, one-shot tool rather than silent
    /// compatibility code in the load path: a reader that quietly "fixes" data it does not
    /// understand is how a format bug survives twice. Repaired files are rewritten in the
    /// normal float32-metres layout, so nothing downstream needs to know this happened.
    ///
    /// Menu: OpenRoomPlan ▸ Repair Session Depth (uint16 → metres).
    /// </summary>
    public sealed class SessionDepthRepairWindow : EditorWindow
    {
        private string[] _sessions = Array.Empty<string>();
        private int _index;
        private bool _dryRun = true;
        private string _result = "";
        private Vector2 _scroll;

        [MenuItem("OpenRoomPlan/Repair Session Depth (uint16 → metres)")]
        private static void Open() => GetWindow<SessionDepthRepairWindow>("ORP Repair Depth");

        private void OnEnable() => Rescan();

        private void Rescan()
        {
            _sessions = CaptureSessionPlayer.ListSessions().OrderBy(s => s).ToArray();
            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _sessions.Length - 1));
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Repairs depth recorded before the ARCore DepthUint16 fix.\n\n" +
                "Only touches sessions whose depth actually decodes as broken — a healthy " +
                "session is detected and skipped, so running this twice is safe.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_sessions.Length == 0)
                    EditorGUILayout.LabelField("No sessions found in persistentDataPath.");
                else
                    _index = EditorGUILayout.Popup("Session", _index, _sessions);
                if (GUILayout.Button("Rescan", GUILayout.Width(70))) Rescan();
            }

            _dryRun = EditorGUILayout.ToggleLeft(
                "Dry run (inspect and report, write nothing)", _dryRun);

            using (new EditorGUI.DisabledScope(_sessions.Length == 0))
            {
                if (GUILayout.Button(_dryRun ? "Inspect session" : "Repair session (overwrites depth/)",
                                     GUILayout.Height(30)))
                    Run(_sessions[_index]);
            }

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space();
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.SelectableLabel(_result, EditorStyles.wordWrappedLabel,
                                                GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void Run(string sessionId)
        {
            string depthDir = Path.Combine(SessionIO.SessionDir(sessionId), "depth");
            if (!Directory.Exists(depthDir))
            {
                _result = $"No depth/ folder in {sessionId}.";
                return;
            }

            string[] files = Directory.GetFiles(depthDir, "*.bin");
            Array.Sort(files);
            if (files.Length == 0)
            {
                _result = $"{sessionId}: depth/ is empty — nothing to repair " +
                          "(that session recorded no platform depth at all).";
                return;
            }

            int repaired = 0, healthy = 0, skipped = 0;
            var log = new System.Text.StringBuilder();

            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (i % 25 == 0 &&
                        EditorUtility.DisplayCancelableProgressBar(
                            _dryRun ? "Inspecting depth" : "Repairing depth",
                            $"{Path.GetFileName(files[i])}  ({i + 1}/{files.Length})",
                            (i + 1) / (float)files.Length))
                    {
                        log.AppendLine("CANCELLED by user.");
                        break;
                    }

                    var verdict = RepairOne(files[i], _dryRun, out string note);
                    switch (verdict)
                    {
                        case Verdict.Repaired: repaired++; break;
                        case Verdict.Healthy: healthy++; break;
                        default: skipped++; break;
                    }
                    if (note != null && log.Length < 4000)
                        log.AppendLine($"{Path.GetFileName(files[i])}: {note}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _result =
                $"{sessionId} — {files.Length} depth files\n" +
                $"  {(_dryRun ? "would repair" : "repaired")}: {repaired}\n" +
                $"  already healthy (left alone): {healthy}\n" +
                $"  skipped (unrecognised layout): {skipped}\n\n" +
                (log.Length > 0 ? "First findings:\n" + log : "");

            if (!_dryRun && repaired > 0)
                Debug.Log($"[ORP] Repaired {repaired} depth frames in {sessionId}.");
        }

        private enum Verdict { Repaired, Healthy, Skipped }

        /// <summary>
        /// Decides whether one file is the broken interleaved form and, unless dry-running,
        /// rewrites it as float32 metres.
        /// </summary>
        private static Verdict RepairOne(string path, bool dryRun, out string note)
        {
            note = null;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8)
            {
                note = "too short";
                return Verdict.Skipped;
            }

            int w = BitConverter.ToInt32(bytes, 0);
            int h = BitConverter.ToInt32(bytes, 4);
            long payload = bytes.Length - 8L;
            if (w <= 0 || h <= 0 || payload != (long)w * h * 4)
            {
                note = $"unexpected layout ({w}x{h}, {payload} B payload)";
                return Verdict.Skipped;
            }

            // Healthy? Decode as float32 metres and see whether the values are room-shaped.
            int plausible = 0, count = w * h;
            for (int i = 0; i < count; i++)
            {
                float d = BitConverter.ToSingle(bytes, 8 + i * 4);
                if (d > 0.05f && d < 30f && !float.IsNaN(d)) plausible++;
            }
            if (plausible > count / 4)
            {
                note = null;
                return Verdict.Healthy;
            }

            // Broken: recover source uint16 millimetres from every second value.
            var metres = new float[count];
            int recovered = 0;
            for (int k = 0; k < count; k++)
            {
                int off = 8 + k * 4;                       // == payload uint16 index 2*k
                int mm = bytes[off] | (bytes[off + 1] << 8);
                float d = mm * 0.001f;
                if (d > 0.05f && d < 30f) recovered++;
                metres[k] = d;
            }

            if (recovered <= count / 4)
            {
                note = $"neither float32 nor interleaved uint16 looks like depth " +
                       $"({plausible} float / {recovered} uint16 plausible of {count})";
                return Verdict.Skipped;
            }

            if (!dryRun)
                SessionIO.WriteDepthBin(path, metres, w, h);

            note = $"{plausible}/{count} plausible as float32, {recovered}/{count} as uint16-mm " +
                   $"{(dryRun ? "→ would repair" : "→ repaired")}";
            return Verdict.Repaired;
        }
    }
}
