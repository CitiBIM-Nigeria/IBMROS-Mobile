using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using OpenRoomPlan.Capture;

namespace OpenRoomPlan.Editor
{
    /// <summary>
    /// Generates a ready-to-build AR capture scene (AR Session + XR Origin rig + recorder + UI Toolkit
    /// record screen) programmatically — far more reliable than hand-authoring scene YAML.
    /// Menu: OpenRoomPlan ▸ Create Capture Scene.
    /// After running: enable ARCore/ARKit in XR Plug-in Management, press Play (uses AR Foundation XR
    /// Simulation in the Editor) or build to a device, then tap Record.
    /// </summary>
    public static class CaptureSceneBuilder
    {
        const string ScenePath = "Assets/MyStuffs/Scenes/OpenRoomPlanCapture.unity";
        const string SharedPanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
        const string CapturePanelSettingsPath = "Assets/OpenRoomPlan/UI/CapturePanelSettings.asset";
        const string UxmlPath = "Assets/OpenRoomPlan/UI/CaptureScreen.uxml";
        const string UssPath = "Assets/OpenRoomPlan/UI/CaptureScreen.uss";

        [MenuItem("OpenRoomPlan/Create Capture Scene")]
        public static void CreateCaptureScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- AR Session ---
            var sessionGO = new GameObject("AR Session");
            sessionGO.AddComponent<ARSession>();
            sessionGO.AddComponent<ARInputManager>();

            // --- XR Origin + camera rig ---
            var originGO = new GameObject("XR Origin");
            var origin = originGO.AddComponent<XROrigin>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGO.transform, false);

            var camGO = new GameObject("AR Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(offset.transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            var camManager = camGO.AddComponent<ARCameraManager>();
            camGO.AddComponent<ARCameraBackground>();
            var occ = camGO.AddComponent<AROcclusionManager>();
            ConfigureTrackedPoseInput(camGO.AddComponent<TrackedPoseDriver>());

            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            // Handheld AR: session-space tracking, no VR-style standing-height offset. Leaving the
            // XROrigin defaults (NotSpecified + 1.1176) lifts the camera 1.12 m above the trackables.
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;
            var pcm = originGO.AddComponent<ARPointCloudManager>();
            var pointCloudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/AR Default Point Cloud.prefab");
            if (pointCloudPrefab != null) SetRef(pcm, "m_PointCloudPrefab", pointCloudPrefab);
            else Debug.LogWarning("[ORP] No point-cloud viz prefab found; feature points will be invisible (data still recorded).");

            // Plane detection + visualization (surfaces light up as they're scanned).
            var planeManager = originGO.AddComponent<ARPlaneManager>();
            SetRef(planeManager, "m_PlanePrefab", GetOrCreatePlanePrefab());

            // --- Recorder (on the AR Camera, beside the managers it consumes) ---
            var rec = camGO.AddComponent<CaptureSessionRecorder>();
            SetRef(rec, "cameraManager", camManager);
            SetRef(rec, "occlusionManager", occ);
            SetRef(rec, "pointCloudManager", pcm);
            SetRef(rec, "arCamera", cam);

            // --- UI Toolkit record screen ---
            var panelSettings = GetOrCreateCapturePanelSettings();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uxml == null) Debug.LogWarning($"[ORP] UXML not found at {UxmlPath}");

            var uiGO = new GameObject("Capture UI");
            var doc = uiGO.AddComponent<UIDocument>();
            SetRef(doc, "m_PanelSettings", panelSettings);
            SetRef(doc, "sourceAsset", uxml);
            var ctrl = uiGO.AddComponent<CaptureScreenController>();
            SetRef(ctrl, "recorder", rec);
            SetRef(ctrl, "styleSheet", uss);
            SetRef(ctrl, "planeManager", planeManager);
            SetRef(ctrl, "pointCloudManager", pcm);
            SetRef(ctrl, "arCamera", cam);

            // EventSystem for runtime UI Toolkit pointer input under the new Input System.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // --- Save + register in build settings ---
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);

            Debug.Log($"[ORP] Capture scene created: {ScenePath}\n" +
                      "Test in Editor: press Play (AR Foundation XR Simulation). On device: enable " +
                      "ARCore/ARKit in XR Plug-in Management and Build & Run.");
            EditorUtility.DisplayDialog("OpenRoomPlan",
                $"Capture scene created and added to Build Settings:\n{ScenePath}\n\n" +
                "• Editor: press Play (XR Simulation) to try the UI/flow.\n" +
                "• Device: enable ARCore/ARKit in XR Plug-in Management, then Build & Run.\n\n" +
                "Recordings are saved under Application.persistentDataPath/OpenRoomPlan/Sessions.",
                "OK");
        }

        /// <summary>
        /// AddComponent&lt;TrackedPoseDriver&gt; from script leaves the input actions with NO bindings
        /// (the editor "XR Origin (Mobile AR)" preset is what normally supplies them), so the AR camera's
        /// transform never updates on device: overlays look glued to the screen, the recorder's motion
        /// gate never passes (frames stuck at 1), coverage never fills. Bind the standard XR/handheld-AR
        /// controls explicitly. ARInputManager (on the AR Session) publishes these devices.
        /// </summary>
        static void ConfigureTrackedPoseInput(TrackedPoseDriver tpd)
        {
            var pos = new InputAction("Position", InputActionType.Value, expectedControlType: "Vector3");
            pos.AddBinding("<XRHMD>/centerEyePosition");
            pos.AddBinding("<HandheldARInputDevice>/devicePosition");

            var rot = new InputAction("Rotation", InputActionType.Value, expectedControlType: "Quaternion");
            rot.AddBinding("<XRHMD>/centerEyeRotation");
            rot.AddBinding("<HandheldARInputDevice>/deviceRotation");

            var state = new InputAction("Tracking State", InputActionType.Value, expectedControlType: "Integer");
            state.AddBinding("<XRHMD>/trackingState");
            state.AddBinding("<HandheldARInputDevice>/trackingState");

            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);
            tpd.trackingStateInput = new InputActionProperty(state);
        }

        /// <summary>
        /// The capture screen gets its OWN PanelSettings: the app's shared asset targets a 1200x800
        /// landscape reference, which blows this portrait phone UI up until buttons overflow the screen.
        /// Reference here is a 393x852 portrait phone, match-width, so USS px behave as logical points.
        /// The theme is copied from the shared asset so visuals stay consistent with the app.
        /// </summary>
        static PanelSettings GetOrCreateCapturePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(CapturePanelSettingsPath);
            if (existing != null) return existing;

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "CapturePanelSettings";
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(393, 852);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0f; // match width — one-column portrait UI

            var shared = AssetDatabase.LoadAssetAtPath<PanelSettings>(SharedPanelSettingsPath);
            if (shared != null && shared.themeStyleSheet != null)
                ps.themeStyleSheet = shared.themeStyleSheet;
            else
                Debug.LogWarning("[ORP] Shared PanelSettings/theme not found; capture panel has no theme.");

            AssetDatabase.CreateAsset(ps, CapturePanelSettingsPath);
            return ps;
        }

        static void SetRef(Component c, string field, Object value)
        {
            var so = new SerializedObject(c);
            var prop = so.FindProperty(field);
            if (prop != null) { prop.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
            else Debug.LogWarning($"[ORP] serialized field '{field}' not found on {c.GetType().Name}");
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // --- AR plane visualization prefab (URP-safe material) ---
        const string PrefabDir = "Assets/OpenRoomPlan/Prefabs";

        static void EnsurePrefabDir()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/OpenRoomPlan", "Prefabs");
        }

        static Material GetOrCreatePlaneMaterial()
        {
            const string p = PrefabDir + "/PlaneViz.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) return m;

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) { Debug.LogWarning("[ORP] URP/Unlit shader not found; plane may render magenta"); sh = Shader.Find("Sprites/Default"); }
            m = new Material(sh)
            {
                color = new Color(0.3f, 0.7f, 1f, 0.35f),
                renderQueue = 3000,
            };
            m.SetColor("_BaseColor", new Color(0.3f, 0.7f, 1f, 0.35f));
            m.SetFloat("_Surface", 1f);   // 0 opaque, 1 transparent
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            EnsurePrefabDir();
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static GameObject GetOrCreatePlanePrefab()
        {
            const string p = PrefabDir + "/ARPlaneViz.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (existing != null) return existing;

            EnsurePrefabDir();
            var go = new GameObject("ARPlaneViz",
                typeof(MeshFilter), typeof(MeshRenderer), typeof(ARPlaneMeshVisualizer), typeof(ARPlane));
            go.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreatePlaneMaterial();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, p);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
