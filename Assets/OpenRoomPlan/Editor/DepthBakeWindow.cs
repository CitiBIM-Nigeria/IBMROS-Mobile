using System.Linq;
using UnityEditor;
using UnityEngine;
using OpenRoomPlan.Core;
using OpenRoomPlan.Capture;
using OpenRoomPlan.Depth;

namespace OpenRoomPlan.Editor
{
    /// <summary>
    /// Runs the DA-V2-S depth model over a recorded session in the Editor and writes the result into
    /// <c>models/&lt;modelId&gt;/</c> as standard depth .bin files, so the Eval Tool reconstructs from it
    /// (Depth source = Model) through the same RANSAC→Manhattan pipeline that scores platform/LiDAR depth.
    ///
    /// Produces the depth-variant-matrix rows directly:
    ///   • <b>Net-only aligned</b>  → id "depth-anything-v2-small"        (V1: DA-V2-S alone, scale-aligned)
    ///   • <b>Fused with platform</b> → id "depth-anything-v2-small-fused" (V2: the confidence-weighted candidate)
    ///
    /// Alignment turns the net's relative/disparity output into metres against platform depth, LiDAR GT, or
    /// sparse VIO anchors (the on-device path, when the session recorded points/).
    /// Menu: OpenRoomPlan ▸ Bake Depth (DA-V2-S).
    /// </summary>
    public sealed class DepthBakeWindow : EditorWindow
    {
        enum AlignSource { PlatformDepth, LiDAR_GT, VIO_Anchors, None_NetIsMetric }

        const string IdNetOnly = DepthProviders.DAV2SmallId;             // "depth-anything-v2-small"
        const string IdFused = DepthProviders.DAV2SmallId + "-fused";    // "depth-anything-v2-small-fused"

        string[] _sessions = new string[0];
        int _sessionIdx;
        AlignSource _align = AlignSource.PlatformDepth;
        bool _fuseWithPlatform;
        string _result = "";
        Vector2 _scroll;

        [MenuItem("OpenRoomPlan/Bake Depth (DA-V2-S)")]
        static void Open() => GetWindow<DepthBakeWindow>("OpenRoomPlan Bake Depth");

        void OnEnable() => Refresh();

        void Refresh()
        {
            _sessions = CaptureSessionPlayer.ListSessions().OrderBy(s => s).ToArray();
            _sessionIdx = Mathf.Clamp(_sessionIdx, 0, Mathf.Max(0, _sessions.Length - 1));
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_sessions.Length == 0)
                    EditorGUILayout.HelpBox("No recorded sessions found.", MessageType.Info);
                else
                    _sessionIdx = EditorGUILayout.Popup(_sessionIdx, _sessions);
                if (GUILayout.Button("↻", GUILayout.Width(28))) Refresh();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scale alignment", EditorStyles.boldLabel);
            _align = (AlignSource)EditorGUILayout.EnumPopup("Align net to", _align);
            EditorGUILayout.HelpBox(
                "DA-V2-S emits relative disparity. Align to platform depth (Android), LiDAR GT (iPhone), or " +
                "sparse VIO anchors (needs a session recorded with points/). Choose 'None' only for a " +
                "metric-fine-tuned model.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fusion", EditorStyles.boldLabel);
            _fuseWithPlatform = EditorGUILayout.Toggle("Fuse with platform depth (V2)", _fuseWithPlatform);
            EditorGUILayout.HelpBox(
                _fuseWithPlatform
                    ? $"Writes the confidence-weighted platform+net candidate to models/{IdFused}/."
                    : $"Writes net-only aligned depth to models/{IdNetOnly}/.", MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_sessions.Length == 0))
                if (GUILayout.Button("Bake DA-V2-S depth into session", GUILayout.Height(32)))
                    Bake();

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_result, MessageType.None);
            }
            EditorGUILayout.EndScrollView();
        }

        void Bake()
        {
            string sessionId = _sessions[_sessionIdx];
            var player = new CaptureSessionPlayer(sessionId);
            string outId = _fuseWithPlatform ? IdFused : IdNetOnly;

            if (!DepthModelRegistry.TryCreate(DepthProviders.DAV2SmallId, out var model))
            {
                _result = "Model not registered — is OpenRoomPlan.Depth compiled?";
                return;
            }

            int baked = 0, aligned = 0, fused = 0, skipped = 0, total = player.Frames.Count;
            model.Initialize();
            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (i % 3 == 0)
                        EditorUtility.DisplayProgressBar("Baking DA-V2-S",
                            $"Frame {i}/{total} (baked {baked})", i / (float)Mathf.Max(1, total));

                    if (!player.TryLoadInput(i, out var input)) { skipped++; continue; }
                    var res = model.Infer(input);
                    if (!res.HasData) { skipped++; continue; }

                    int w = res.width, h = res.height;
                    float[] metric = res.isMetric ? res.depthMeters : null;

                    if (metric == null)
                    {
                        metric = AlignToMetric(player, i, input, res, out bool didAlign);
                        if (metric == null) { skipped++; continue; }
                        if (didAlign) aligned++;
                    }

                    if (_fuseWithPlatform)
                    {
                        if (!player.TryLoadPlatformDepth(i, out var pd, out int pw, out int ph)) { skipped++; continue; }
                        player.TryLoadPlatformConfidence(i, out var conf, out int cw, out int ch);
                        float[] confAligned = (conf != null && cw == pw && ch == ph) ? conf : null;
                        metric = DepthFusion.Fuse(pd, confAligned, pw, ph, metric, w, h, out w, out h);
                        if (metric == null) { skipped++; continue; }
                        fused++;
                    }

                    SessionIO.WriteDepthBin(SessionIO.ModelDepthPath(sessionId, outId, i), metric, w, h);
                    baked++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                model.Dispose();
            }

            _result = $"Baked {baked}/{total} frames  (aligned {aligned}, fused {fused}, skipped {skipped})\n" +
                      $"→ models/{outId}/\n\n" +
                      $"Now open Eval Tool, set Depth source = Model, id = {outId}, Reconstruct.";
        }

        /// <summary>Convert the net's relative/disparity output to metres per the chosen anchor source.</summary>
        float[] AlignToMetric(CaptureSessionPlayer player, int i, in DepthFrameInput input,
                              in DepthFrameResult res, out bool didAlign)
        {
            didAlign = false;
            int w = res.width, h = res.height;
            switch (_align)
            {
                case AlignSource.None_NetIsMetric:
                    return res.depthMeters; // user asserts the model is metric

                case AlignSource.VIO_Anchors:
                {
                    if (input.metricAnchorsWorld == null || input.metricAnchorsWorld.Count == 0) return null;
                    var m = ScaleAligner.AlignToAnchors(res.depthMeters, w, h, res.isInverseValues,
                                input.intrinsics, input.cameraPose, input.metricAnchorsWorld, out _);
                    didAlign = m != null;
                    return m;
                }

                default: // PlatformDepth or LiDAR_GT — dense reference map
                {
                    bool haveRef = _align == AlignSource.PlatformDepth
                        ? player.TryLoadPlatformDepth(i, out float[] refD, out int rw, out int rh)
                        : player.TryLoadLiDARDepth(i, out refD, out rw, out rh);
                    if (!haveRef) return null;
                    var m = ScaleAligner.AlignToReferenceDepth(res.depthMeters, w, h, res.isInverseValues,
                                refD, rw, rh, out _);
                    didAlign = m != null;
                    return m;
                }
            }
        }
    }
}
