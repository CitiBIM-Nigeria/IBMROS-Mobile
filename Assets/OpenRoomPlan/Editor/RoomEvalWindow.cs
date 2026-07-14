using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using OpenRoomPlan.Core;
using OpenRoomPlan.Capture;
using OpenRoomPlan.Reconstruction;

namespace OpenRoomPlan.Editor
{
    /// <summary>
    /// Editor window that turns a recorded session into a reconstructed room + numbers, entirely offline.
    /// Pick a session and a depth source, Reconstruct, and see the cloud + room in the Scene view plus
    /// dimensions/validity. Optionally overlay the LiDAR-GT reconstruction (iPhone sessions) to compare.
    /// Menu: OpenRoomPlan ▸ Eval Tool.
    /// </summary>
    public sealed class RoomEvalWindow : EditorWindow
    {
        enum DepthSource { Platform, LiDAR_GT, Model }

        string[] _sessions = new string[0];
        int _sessionIdx;
        DepthSource _depthSource = DepthSource.Platform;
        string _modelId = "moge-2";

        float _minDepth = 0.3f, _maxDepth = 5f, _voxel = 0.03f;
        int _stride = 2;
        bool _flipV;

        string _result = "";
        Vector2 _scroll;

        [MenuItem("OpenRoomPlan/Eval Tool")]
        static void Open() => GetWindow<RoomEvalWindow>("OpenRoomPlan Eval");

        void OnEnable() => RefreshSessions();

        void RefreshSessions()
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
                    EditorGUILayout.HelpBox("No recorded sessions found under persistentDataPath/OpenRoomPlan/Sessions.", MessageType.Info);
                else
                    _sessionIdx = EditorGUILayout.Popup(_sessionIdx, _sessions);
                if (GUILayout.Button("↻", GUILayout.Width(28))) RefreshSessions();
            }
            if (GUILayout.Button("Open sessions folder"))
            {
                Directory.CreateDirectory(SessionIO.SessionsRoot);
                EditorUtility.RevealInFinder(SessionIO.SessionsRoot);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Depth source", EditorStyles.boldLabel);
            _depthSource = (DepthSource)EditorGUILayout.EnumPopup("Source", _depthSource);
            if (_depthSource == DepthSource.Model)
                _modelId = EditorGUILayout.TextField("Model id (models/<id>)", _modelId);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            _minDepth = EditorGUILayout.FloatField("Min depth (m)", _minDepth);
            _maxDepth = EditorGUILayout.FloatField("Max depth (m)", _maxDepth);
            _stride = EditorGUILayout.IntSlider("Pixel stride", _stride, 1, 8);
            _voxel = EditorGUILayout.Slider("Voxel size (m)", _voxel, 0.01f, 0.1f);
            _flipV = EditorGUILayout.Toggle("Flip depth V (calibration)", _flipV);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_sessions.Length == 0))
            {
                if (GUILayout.Button("Reconstruct", GUILayout.Height(32)))
                    Reconstruct(overlayGT: false);
                if (GUILayout.Button("Reconstruct + overlay LiDAR GT"))
                    Reconstruct(overlayGT: true);
            }
            if (GUILayout.Button("Clear preview")) ClearPreview();

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_result, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        void Reconstruct(bool overlayGT)
        {
            string sessionId = _sessions[_sessionIdx];
            var player = new CaptureSessionPlayer(sessionId);

            var candidate = BuildModel(player, sessionId, _depthSource, out var points, out int usedFrames);
            RoomModel gt = default; bool hasGT = false;
            if (overlayGT)
                gt = BuildModel(player, sessionId, DepthSource.LiDAR_GT, out _, out _, out hasGT);

            var preview = GetPreview();
            preview.points = points;
            preview.candidate = candidate; preview.hasCandidate = true;
            preview.groundTruth = gt; preview.hasGroundTruth = overlayGT && hasGT;

            _result = Format(sessionId, player, candidate, points?.Length ?? 0, usedFrames,
                             overlayGT && hasGT, gt);

            Selection.activeGameObject = preview.gameObject;
            SceneView.FrameLastActiveSceneView();
            SceneView.RepaintAll();
        }

        RoomModel BuildModel(CaptureSessionPlayer player, string sessionId, DepthSource src,
                             out Vector3[] points, out int usedFrames)
            => BuildModel(player, sessionId, src, out points, out usedFrames, out _);

        RoomModel BuildModel(CaptureSessionPlayer player, string sessionId, DepthSource src,
                             out Vector3[] points, out int usedFrames, out bool anyDepth)
        {
            var recon = new RoomReconstructor(_voxel);
            usedFrames = 0; anyDepth = false;
            int total = player.Frames.Count;
            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (i % 5 == 0)
                        EditorUtility.DisplayProgressBar("OpenRoomPlan Eval",
                            $"Integrating frame {i}/{total}", i / (float)Mathf.Max(1, total));

                    if (!player.TryLoadInput(i, out var input)) continue;
                    if (!TryGetDepth(player, sessionId, i, src, out var depth, out int w, out int h)) continue;

                    anyDepth = true;
                    recon.AddFrame(depth, w, h, input.intrinsics, input.cameraPose,
                                   _minDepth, _maxDepth, _stride, _flipV);
                    usedFrames++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            points = recon.Points.ToArray();
            return recon.Solve();
        }

        bool TryGetDepth(CaptureSessionPlayer player, string sessionId, int i, DepthSource src,
                         out float[] depth, out int w, out int h)
        {
            switch (src)
            {
                case DepthSource.Platform: return player.TryLoadPlatformDepth(i, out depth, out w, out h);
                case DepthSource.LiDAR_GT: return player.TryLoadLiDARDepth(i, out depth, out w, out h);
                case DepthSource.Model:
                    depth = null; w = h = 0;
                    string p = SessionIO.ModelDepthPath(sessionId, _modelId, i);
                    if (!File.Exists(p)) return false;
                    depth = SessionIO.ReadDepthBin(p, out w, out h);
                    return true;
                default: depth = null; w = h = 0; return false;
            }
        }

        static string Format(string sessionId, CaptureSessionPlayer player, RoomModel m,
                             int pointCount, int usedFrames, bool hasGT, RoomModel gt)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Session: {sessionId}");
            sb.AppendLine($"Device: {player.Manifest.deviceModel} ({player.Manifest.platform}); LiDAR={player.Manifest.hasLiDAR}");
            sb.AppendLine($"Frames used: {usedFrames}/{player.Frames.Count}   Points: {pointCount}");
            sb.AppendLine();
            if (m.valid)
            {
                int snapped = m.walls?.Count(w => w.manhattanSnapped) ?? 0;
                sb.AppendLine($"CANDIDATE room: VALID");
                sb.AppendLine($"  Dimensions L×W×H: {m.dimensions.x:F2} × {m.dimensions.y:F2} × {m.dimensions.z:F2} m");
                sb.AppendLine($"  Walls: {m.walls?.Length ?? 0} ({snapped} Manhattan-snapped)");
                sb.AppendLine($"  Floor Y: {m.floorY:F2}   Ceiling Y: {m.ceilingY:F2}");
            }
            else
            {
                sb.AppendLine("CANDIDATE room: INVALID (no closed room found — check flipV, depth source, or coverage)");
            }

            if (hasGT && gt.valid)
            {
                sb.AppendLine();
                sb.AppendLine($"LiDAR GT dimensions: {gt.dimensions.x:F2} × {gt.dimensions.y:F2} × {gt.dimensions.z:F2} m");
                if (m.valid)
                {
                    Vector3 d = m.dimensions - gt.dimensions;
                    sb.AppendLine($"  Δ vs GT: {Mathf.Abs(d.x)*100f:F1} / {Mathf.Abs(d.y)*100f:F1} / {Mathf.Abs(d.z)*100f:F1} cm");
                }
            }
            return sb.ToString();
        }

        static RoomPreview GetPreview()
        {
            var existing = Object.FindFirstObjectByType<RoomPreview>();
            if (existing != null) return existing;
            var go = new GameObject("OpenRoomPlan Preview") { hideFlags = HideFlags.DontSave };
            return go.AddComponent<RoomPreview>();
        }

        static void ClearPreview()
        {
            var existing = Object.FindFirstObjectByType<RoomPreview>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            SceneView.RepaintAll();
        }
    }
}
