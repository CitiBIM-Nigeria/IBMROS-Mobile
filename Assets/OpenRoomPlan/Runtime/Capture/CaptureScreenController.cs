using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// UI Toolkit controller for the capture screen: safe-area padding, Record/Stop, and live scan
    /// feedback (turn-around coverage meter + plane/point/frame counts). Wired by the scene generator.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CaptureScreenController : MonoBehaviour
    {
        const int SectorCount = 12; // 30° each — a full turn fills the coverage bar

        [SerializeField] CaptureSessionRecorder recorder;
        [SerializeField] StyleSheet styleSheet;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] ARPointCloudManager pointCloudManager;
        [SerializeField] Camera arCamera;

        VisualElement _root, _coverageFill;
        Button _record, _stop;
        Label _status, _coverage, _counts;
        readonly bool[] _sectors = new bool[SectorCount];

        void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            if (_root == null) { Debug.LogWarning("[ORP] Capture UI has no rootVisualElement"); return; }

            if (styleSheet != null && !_root.styleSheets.Contains(styleSheet))
                _root.styleSheets.Add(styleSheet);

            _record = _root.Q<Button>("record-button");
            _stop = _root.Q<Button>("stop-button");
            _status = _root.Q<Label>("status-label");
            _coverage = _root.Q<Label>("coverage-label");
            _counts = _root.Q<Label>("counts-label");
            _coverageFill = _root.Q<VisualElement>("coverage-fill");

            if (_record != null) _record.clicked += OnRecord;
            if (_stop != null) _stop.clicked += OnStop;

            // Safe area: recompute paddings whenever the panel lays out (handles rotation / notch).
            var safeTarget = _root.Q<VisualElement>("capture-root") ?? _root;
            safeTarget.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea(safeTarget));

            if (planeManager != null)
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            if (arCamera == null) arCamera = Camera.main;

            Refresh();
        }

        void OnDestroy()
        {
            if (_record != null) _record.clicked -= OnRecord;
            if (_stop != null) _stop.clicked -= OnStop;
        }

        void ApplySafeArea(VisualElement target)
        {
            float w = target.layout.width, h = target.layout.height;
            if (w <= 0f || h <= 0f) return; // not laid out yet
            var sa = Screen.safeArea;

            // Screen-space insets as fractions, applied in panel units via the laid-out size.
            float topFrac = (Screen.height - (sa.y + sa.height)) / Screen.height;
            float bottomFrac = sa.y / Screen.height;
            float leftFrac = sa.x / Screen.width;
            float rightFrac = (Screen.width - (sa.x + sa.width)) / Screen.width;

            const float basePad = 16f;
            target.style.paddingTop = topFrac * h + basePad;
            target.style.paddingBottom = bottomFrac * h + basePad;
            target.style.paddingLeft = leftFrac * w + basePad;
            target.style.paddingRight = rightFrac * w + basePad;
        }

        void Update()
        {
            if (recorder == null || !recorder.IsRecording) return;
            UpdateCoverage();
            Refresh();
        }

        void UpdateCoverage()
        {
            if (arCamera == null) return;
            Vector3 f = arCamera.transform.forward;
            if (f.sqrMagnitude < 1e-6f) return;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;      // -180..180
            int sector = Mathf.FloorToInt(((yaw + 180f) % 360f) / (360f / SectorCount));
            sector = Mathf.Clamp(sector, 0, SectorCount - 1);
            _sectors[sector] = true;
        }

        int CoveredSectors()
        {
            int n = 0;
            for (int i = 0; i < SectorCount; i++) if (_sectors[i]) n++;
            return n;
        }

        int CountPoints()
        {
            if (pointCloudManager == null) return 0;
            int n = 0;
            foreach (var pc in pointCloudManager.trackables)
                if (pc.positions.HasValue) n += pc.positions.Value.Length;
            return n;
        }

        void OnRecord()
        {
            if (recorder != null && !recorder.IsRecording)
            {
                for (int i = 0; i < SectorCount; i++) _sectors[i] = false;
                recorder.StartRecording();
            }
            Refresh();
        }

        void OnStop()
        {
            if (recorder != null && recorder.IsRecording) recorder.StopRecording();
            Refresh();
        }

        void Refresh()
        {
            bool rec = recorder != null && recorder.IsRecording;

            int covered = CoveredSectors();
            float pct = covered / (float)SectorCount * 100f;
            if (_coverage != null) _coverage.text = $"Coverage: {covered} / {SectorCount} directions";
            if (_coverageFill != null) _coverageFill.style.width = Length.Percent(pct);

            if (_counts != null)
            {
                int planes = planeManager != null ? planeManager.trackables.count : 0;
                _counts.text = $"planes {planes} · points {CountPoints()} · frames {(recorder != null ? recorder.FrameCount : 0)}";
            }

            if (_status != null)
            {
                if (rec)
                    _status.text = covered >= SectorCount
                        ? "● REC — full turn captured ✓ you can Stop"
                        : $"● REC — keep turning ({covered}/{SectorCount})";
                else
                    _status.text = "Idle — tap Record to scan a room";
            }

            _record?.SetEnabled(recorder != null && !rec);
            _stop?.SetEnabled(rec);
        }
    }
}
