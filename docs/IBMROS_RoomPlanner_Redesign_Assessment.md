# IBMROS Room Planner — Redesign Implementation Assessment

**Date:** 2026-07-27
**Method:** 8 parallel code-inspection passes over the working tree + live Unity MCP verification (scene hierarchies, play-mode gateway test, undo/redo round-trip, screenshots of generated 2D/3D geometry). All claims below are code-verified unless marked *unverified*.
**Reference:** screen recording of a commercial room-planner app (Create → room type → floor-plan presets → 2D editor with dimensions → 2D/3D toggle → furnish/walkthrough). Notably, that app's "2D editor" is a top-down 3D render with touch handles — the same model Exoa uses.

---

## 1. What currently exists

Two disconnected worlds live in this project:

**World A — the shipping MyStuffs app** (`Main.unity` → `Room.unity`):
- `Main.unity` (build 0): UI Toolkit shell — `UIManager` + `ScreenNavigator`, AWS Cognito auth, main screen. "Add Room" = `MainAppController.OnAddRoomClicked()` → `SceneManager.LoadScene("Room")` (MainAppController.cs:535). No preset step, no 2D step, no room model.
- `Room.unity` (build 3): hand-modeled ProBuilder shell (4 walls + floor + ceilings, non-convex MeshColliders), baked lightmaps/probes/area lights, first-person `CameraController` (joystick + swipe + SphereCast wall slide).
- Production-grade furniture pipeline: `FurnitureRepository` (DynamoDB ros-products/ros-categories) → `FurnitureDataService` → `FurnitureModelLoader` (CloudFront GLB, glTFast, versioned disk cache) → `FurnitureSpawnManager`/`FurniturePlacer` (ghost preview, tap-confirm) → `ObjectManipulator`/`ObjectDragHandler` manipulation → transform-level `UndoRedoManager`.
- QR/deep-link services: complete, placeholder domain.
- **Zero persistence**: furniture layout dies on scene exit; the home screen's rooms list is a permanent empty-state.

**World B — Exoa FloorMapEditor + IBMROSBridge** (`FloorMapEditor.unity`, build 4, unreachable from the app):
- Exoa Floor Map Designer (we own and patch the source; 71 `// IBMROS:` tagged edits): polygon rooms + doors/windows as **FloorMapV2 JSON**; procedural 2D→3D (walls with door/window holes cut via Clipper+LibTess, tessellated floors/ceilings, MeshColliders); TouchCameraPro ortho↔perspective animated camera switch; hardened SaveSystem (atomic writes + rolling backups).
- `Assets/IBMROSBridge` (~4,200 lines, IBMROS.Core / IBMROS.Bridge.*): `FloorPlanDocument` (observable document), `DocumentEvents`, **`FloorPlanEditor` gateway** (meters-based: CreateRoom/CreateRectRoom/SplitRoom/AddOpening/MoveItemPoints/…; every op = one labelled undo step), snapshot `UndoRedoService` + `RestoreReconciler` (identity-keyed, blink-free), `ScopedRebuild` + `OpeningHostRegistry`, `AutosaveService`, `SelectionService`, `RectRoomTool`, golden-regression harness + 84-assertion `ApiSmokeTest`.
- 2D and 3D are **states of one scene** (top-down ortho draw ↔ perspective preview) — exactly the reference video's 2D/3D toggle.

**Live-verified today via Unity MCP** (play mode in FloorMapEditor.unity):
`FloorPlanEditor.CreateRectRoom(4, 5, …)` + `AddOpening(Door, …)` → document items with stable ids → procedural 3D room with the door hole cut into the wall → colliders on semantic layers (Floor/ExteriorWall/Roof/InteriorWalls) → undo ×2 empties the document, redo ×2 restores both items **with identical ids** → animated 2D↔3D camera switch works.

**Also important:** the `IBMROS.RoomEngine/RoomView/App` assemblies named in the older roadmap **do not exist** — that from-scratch engine was superseded on 2026-07-17, never committed, and deleted. `RoomDesignTesting.unity` contains only missing-script stubs from it. The stale root `IBMROS.*.csproj` files and `.github` CI globs pointing at `Assets/IBMROS/**` are residue.

**Critical repo risk:** `Assets/Exoa/`, `Assets/IBMROSBridge/`, `FloorMapEditor.unity`, all five July docs, `.github/`, and the QR services are **untracked in git** (last commit 2026-07-16). The previous engine was permanently lost exactly this way.

## 2. Existing systems worth keeping

| System | Verdict |
|---|---|
| FloorMapV2 document + FloorPlanEditor gateway + DocumentEvents | Keep — this *is* the "room data independent of visuals" the brief asks for |
| Snapshot undo/redo + reconciler + autosave + golden harness | Keep |
| Exoa procedural 2D→3D generation (walls/openings/colliders) | Keep |
| TouchCameraPro cameras (ortho pan/zoom, perspective orbit, animated switch) | Keep |
| Furniture/catalog pipeline (repository → data service → GLB loader → spawn) | Keep unchanged — room-agnostic |
| Main.unity app shell, auth, navigation, QR deep-link routing | Keep |
| InputManager/ObjectManipulator gesture layer + placement/drag concepts | Adapt (see §3 bugs) |
| First-person CameraController (Room.unity) | Adapt for 3D walkthrough mode |

## 3. Legacy / broken — replace or fix

- **Furniture-through-walls root cause (found):** `ProjectSettings/TagManager.asset` defines **two layers named "Floor"** (indices 7 *and* 9) and an unused "Wall" layer (10). Every wall in Room.unity sits on layer 9 (the duplicate "Floor"); `ObjectDragHandler` hardcodes `LayerMask.GetMask("Wall")` (= empty layer 10), so the wall collide-and-slide **silently never hits anything** — and its zero-mask warning can't fire because the layer name exists. `FurniturePlacer` raycasts only the floor layer (through walls) with no overlap validation at confirm; rotation/scale have no spatial checks; drag footprints are axis-aligned AABBs. Ironically, Exoa's generated rooms put walls on layer 10/"Wall" — the mask would work out of the box; only the floor layer (9 vs serialized 7) needs aligning.
- **Furniture lighting mismatch:** room look is baked; runtime GLBs get a 0.4-intensity shadowless directional + flat ambient + blob shadow → "pasted-on" furniture. The new generated room needs a realtime-friendly rig.
- Room.unity's static ProBuilder geometry (not data-driven) — superseded by document-generated rooms.
- Exoa's desktop demo UI (left menus, sliders, WizardPopup, `support.exoa.fr` welcome popup on device, mouse-only `ControlPointsController` with right-click/Alt-click interactions) — replace with touch-first UI.
- Dead: `LoadScene("IKEAStore")` (scene deleted), `AWS UGUI.unity` (63 MB legacy), `RoomDesignTesting.unity` stubs, stale CI/Tooling/csproj, SilverTau RoomScanning in build (iOS-LiDAR-only, overlaps OpenRoomPlan).
- Package hygiene: glTFast is only a **transitive** dep of the prerelease AI Assistant package (removing it would break furniture loading) — promote to manifest; unpinned unused UnityGLTF git dep; stale WebP registry.

## 4 & 5. Scene strategy: **Hybrid (Option C)**

**Create one new scene — `RoomDesigner.unity` — hosting the full 2D↔3D experience**, assembled from the proven runtime parts (Exoa `Grid` + `AppController` + procedural builders + TouchCameraPro rig + IBMROSBridge services) plus the MyStuffs furniture stack, with new touch-first UI. Keep `Main.unity` as the shell — Create Room and the preset picker are new UI Toolkit screens there, no scene change until the editor opens.

Why not Option A (refactor `FloorMapEditor.unity` in place): it is the vendor demo app — desktop uGUI everywhere, welcome popup, dead menu paths to scenes that don't exist — and the golden harness + ApiSmokeTest load it headlessly for regression; keeping it pristine preserves both the reference implementation and the test bed.

Why not pure Option B (green-field): the hard 60% (document model, gateway, undo, procedural geometry, save system, cameras) already exists and is live-verified; rebuilding it would repeat the July 17 mistake the docs explicitly reversed.

The new scene needs only four ingredients for the Exoa runtime to function (verified in code): a `Grid` (identical 10×10 m normalization plane — the save-format contract), a configured `AppController`, `FloorMapSerializer`/reader, and the `FloorController_Prefab` resources. The bridge services (undo, autosave, selection) self-install into any scene containing a `FloorMapSerializer`. Room.unity and FloorMapEditor.unity stay untouched as fallback/reference until the new flow is proven, then Room.unity leaves the build.

## 6. What EXOA handles for us

- Room/door/window document schema + JSON persistence (+ our hardening)
- All 2D→3D mesh generation incl. openings, with colliders on semantic layers
- 2D↔3D animated camera transition; touch camera gestures (pinch/pan/orbit)
- Grid snapping (0.5 m in shipped scene) and control-point editing primitives
- Multi-floor support (defer UI for it)
- What Exoa does *not* have: furniture (Interior module absent), presets, touch-first editing UI, undo (ours), any app navigation.

## 7. What we must build

1. **Preset system** — data-driven FloorMapV2 generators (Rectangle, L, T, Z, …) executed through the gateway; adding a preset = adding a polygon definition.
2. **Preset picker screen** (UI Toolkit, in Main.unity) with plan thumbnails.
3. **Touch-first 2D editor UI**: tap-select walls/corners/openings, corner drag handles, **wall-edge drag** (= gateway MoveItemPoints on both endpoints), live dimension labels, add/remove wall points, door/window placement mode, room-size dialog, undo/redo buttons, 2D/3D toggle button. (Roadmap A6 territory — the vendor mouse ControlPointsController gets replaced as the interaction surface.)
4. **Continue-to-3D / furnish mode**: same scene, camera switch + optional first-person walkthrough; realtime lighting rig for generated rooms; furniture UI (existing panels) enabled in 3D mode.
5. **Furniture in the document**: additive FloorMapV2 extension (product id/canonical id, position, rotation, scale) → furnished rooms finally persist; saved-rooms list on the main screen.
6. **Collider/layer unification**: single Floor/Wall layer convention; oriented-footprint overlap checks on placement/drag; fix the TagManager duplicate-layer trap.
7. **Navigation**: Main → (preset) → RoomDesigner scene → back; preserve the QR deep-link entry (scan → product lands in the designer).

## 8. How 2D plan, room data, and 3D room connect

**FloorMapV2 (via FloorPlanDocument) is the single source of truth.** The 2D editor UI mutates it only through the `FloorPlanEditor` gateway (labelled, undoable ops) → `DocumentEvents` fire → `ScopedRebuild` regenerates exactly the affected 3D meshes → the 3D/furnish mode reads the same document; furniture placements become document items. Save/load = document JSON. Future room-scan (OpenRoomPlan) and floor-plan-photo import are just additional producers of FloorMapV2 — the architecture the docs already committed to (P9 scan lane).

## 9. Biggest technical risks

1. **Everything untracked in git** — the whole foundation is one `git clean` from oblivion. Mitigation: commit first, before any redesign work.
2. **UI-is-truth residue (A2 step 2c)**: Exoa's serializer still gathers save data from vendor UI items, and the gateway requires the live scene (Grid + serializer + hidden vendor menu). The new UI must treat the gateway as its only write path; finishing the document-authority inversion is scheduled, not assumed.
3. **Grid normalization contract**: saved coordinates are normalized to a specific 10×10 m plane; every consuming scene must reproduce it exactly (pin one Grid prefab project-wide).
4. **Two undo systems / furniture-document reconciliation** — the split-brain (transform undo vs document snapshots) must converge; phased: geometry undo first, furniture ops recorded into the document next.
5. **Rebuild performance during touch drags** on mid-range Android (Clipper/LibTess re-tessellation, 0.1 s debounce) — *unverified*; profile early, coalesce during drag (undo service already coalesces).
6. **Generated-room lighting quality** without baked lightmaps — needs a deliberate realtime rig (directional + ambient/probe strategy shared by room and furniture).
7. **Device touch behavior** of the interim editor (touch-as-mouse emulation) — *unverified on device*; the new input layer removes the dependency.
8. Camera-stack handoff (TouchCameraPro orbit vs first-person walkthrough) needs one integration experiment.

## 10. Recommended implementation phases

- **Phase 0 — Safety & hygiene**: commit all untracked work in logical commits; fix TagManager duplicate layer; promote glTFast to manifest; remove dead IKEAStore path; (defer CI fix or delete stale workflow).
- **Phase 1 — RoomDesigner scene + presets**: assemble the new scene (Grid/AppController/serializer/camera rig, vendor UI hidden, bridge services active); preset generator + picker screen; Main → picker → scene navigation. *Exit criteria: pick Rectangle → land in 2D editor with a room; undo works; autosave works.*
- **Phase 2 — Touch 2D editor**: selection + handles + wall drag + dimensions + openings placement + room-size dialog + undo/redo UI, mouse-compatible in editor.
- **Phase 3 — Continue to 3D**: camera switch, lighting rig, walkthrough camera, collider/layer conventions on generated geometry.
- **Phase 4 — Furniture integration**: existing catalog/placement stack in the 3D mode against generated geometry (correct masks, oriented footprints, overlap-validated placement); furniture into the document; save/load + saved-rooms list.
- **Phase 5 — Polish & cleanup**: retire Room.unity from the flow, remove dead code/scenes from build, full-flow test (Step 7 of the brief) via MCP + on-device pass.

**Scanning/photo-import stay future features**: the scanning UI slot (button) can appear in Phase 4/5 UI as a disabled/"coming soon" entry; OpenRoomPlan already targets FloorMapV2 as its output, so nothing in this plan blocks it.
