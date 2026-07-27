# EXOA Home Designer — Technical Due Diligence Report (v2, Expanded)

**Project:** IBMROS Mobile — Room Design Feature
**Date:** 2026-07-17 (v2 — expanded architecture review)
**Scope:** Full architecture review of the EXOA room-building system, based on (a) direct source-code reverse engineering of the plugin installed in this project (`Assets/Exoa/`, 248 C# files), (b) the complete official documentation of Home Designer Suite 2026 (homedesigner.exoa.dev/docs, self-identified "v3.1"), (c) the Asset Store listing (ID 271115, package v1.2, 2026-07-04, **$198**), and (d) the public roadmap.

**Evidence labels used throughout:**
- ✅ **CONFIRMED (source)** — verified by reading the actual C# code in this project.
- 📄 **CONFIRMED (docs)** — stated verbatim in official documentation/store page.
- 🔶 **INFERRED** — reasonable technical deduction, not directly verifiable.

---

## 0. Executive Summary and Critical Finding

### ⚠️ Critical finding: you own the *deprecated legacy asset*, not Home Designer Suite 2026

The plugin installed in `Assets/Exoa/` is **Floor Plan Designer 2025 (a.k.a. Floor Map Designer)** — the legacy-generation EXOA asset, now marked **DEPRECATED** on the Asset Store. It is *not* Home Designer Suite 2026. Evidence (✅ source): namespace `Exoa.Designer`, `AppController.States { Idle, Draw, PreviewBuilding, PlayMode }`, `GameEditorEvents`, `FloorMapV2` JSON schema, `#if FLOORMAP_MODULE` / `#if INTERIOR_MODULE` symbols — all of which the Suite docs explicitly list as removed/renamed ("Old symbols (`FLOORMAP_MODULE`, `INTERIOR_MODULE`, `FBX_EXPORTER`) are no longer used").

Consequences:

- The **furniture module (InteriorDesigner)** is not installed — only referenced behind `#if INTERIOR_MODULE`. You currently have room/wall/opening generation only.
- The installed version has **no undo/redo, no HeadlessBuildingAPI, no ServiceLocator, no room splitting, no runtime GLB export, no unified data format**. All of those exist only in the 2026 Suite ($198, requires Unity 6 — your project is on 6000.4.6f1, so compatible; the store lists 6000.3.9 as the build baseline).
- EXOA offers a loyalty discount (25–60%) to owners of the legacy assets (homedesigner.exoa.dev/discount).

### Headline answers

1. **How are wall openings made?** ✅ CONFIRMED: **2D polygon boolean subtraction (Clipper `ctDifference`) + tessellation (LibTess) + full procedural wall regeneration.** No 3D CSG, no mesh hiding, no runtime boolean on 3D meshes. The wall is rebuilt from scratch as a clean 2D polygon-with-holes every time anything changes. 📄 The Suite additionally builds spaces **twice** per rebuild: pass 1 establishes wall geometry so openings can snap to exact wall positions; pass 2 cuts the holes.
2. **Can you swap in arbitrary downloaded GLB doors?** **Yes — the single most important architectural fact in this report.** The wall hole is driven purely by *opening data* (`width, height, xPos, yPos`), not by the door model. The visible door is a completely separate mesh positioned in the hole. Any GLB door works if you (a) measure its bounds, (b) write those into the opening record, (c) parent it at the opening anchor. Automatable in ~a day (Section 4.4).
3. **Is the geometry better than ProBuilder?** Yes, categorically, for this use case (Section 5.1) — and ProBuilder cannot run at runtime in builds at all.
4. **Is the architecture sound at 10× scale?** Mostly yes at the *engine* layer (data-driven, event-decoupled, headless-capable); no at the *application shell* layer (static events, mutable singletons, single-active-building). Section 8.7 details exactly what to firewall and what to redesign.
5. **Should you build on it?** **Yes — upgrade to Home Designer Suite 2026 and build on top, but own four subsystems yourself: the furniture/catalog pipeline, the door/window visual catalog, the XR interaction layer, and the AI layer.** Full reasoning in Section 15.

---

## 1. What Is Actually Installed (Inventory)

✅ CONFIRMED (source) — `Assets/Exoa/` contains four packages:

| Folder | Contents | Role |
|---|---|---|
| `FloorMapDesigner/` | 2D drawing UI, `ControlPointsController` (731 LOC), `OpeningController`, serializer, scenes | The 2D floor-plan editor module |
| `Common/` | `Procedural*` generators, controllers, serialization, events, save system, ProceduralToolkit fork (Clipper + LibTessDotNet), grid, outline effect | Shared engine — all mesh generation lives here |
| `BuildingDesigner/` | One editor script (`BuildingModuleSetup.cs`) | Stub only |
| `TouchCameraLite/` | Orbit/pan/pinch camera | Camera |

Key classes and line counts: `ProceduralRoom` (336), `ProceduralSpace` (174), `ProceduralOpening` (288), `ProceduralBuilding` (235), `ProceduralRoofs/RoofTop` (370), `DataModel` (322), `FloorMapReader` (179), `FloorController` (213), `ControlPointsController` (731), `OpeningController` (205), `SaveSystem` (276), `AppController` (228).

The project also contains (relevant later): **ProceduralToolkit** (standalone copy), **OpenRoomPlan** (Core/Depth/Reconstruction/Capture), and **SilverTau RoomPlanUnityKit** — i.e., you already have room-scanning assets that pair naturally with the AI "image/scan → room" vision (Section 11).

---

## 2. Room Generation

### 2.1 Pipeline (✅ CONFIRMED, source)

```
2D draw (ControlPointsController: grid-snapped control points)
        │  normalized grid coords stored in DataModel.FloorMapItem.normalizedPositions
        ▼
FloorMapReader.DeserializeToScene(json)
        ▼
FloorController.BuildFloor(FloorMapLevel)          ── one per storey
        ├── collect opening items → List<GenericOpening>
        ├── BuildOpening() per door/window            (visual meshes)
        ├── BuildRoom() per room  → ProceduralRoom.Generate(points)
        │       ├── GetClockwise(points)              (winding normalization)
        │       ├── GenerateWalls()                   (Section 3)
        │       ├── GenerateFloor()                   (LibTess tessellation)
        │       ├── GenerateCeiling()                 (floor mesh moved up + flipped)
        │       ├── GenerateRoomBox()                 (merged collider + reflection probe volume)
        │       └── AddMeshColliders()
        └── ProceduralBuilding.Generate(rooms)        (exterior shell + roof)
```

- **Input is an arbitrary simple polygon.** Rooms are lists of Vector3 control points; the floor is tessellated with LibTessDotNet, which handles **concave/irregular shapes** correctly. ✅ Irregular rooms: supported. 🔶 Curved walls: not supported (no arc primitive anywhere in the data model; roadmap lists "Rounded Corners" as *In Development* for the Suite 📄).
- **Rooms are fully regenerated on every edit.** No incremental mesh patching; `Generate()` rebuilds walls, floor, ceiling, box. Rebuilds are **throttled** (`OpeningController.delayBetweenRebuilds = 0.1s` with a queued-rebuild flag). ✅ 📄 The Suite keeps the throttle (configurable) and adds the **two-pass build** (geometry pass → hole-cut pass).
- **Multi-room / multi-floor:** `FloorMapV2.floors[]`; floors stacked at `Y = i × wallsHeight`. ✅ The Suite keeps this (`FloorData`, `PreviewModeManager` stacks floors and rebuilds exterior/roof to full height 📄).
- **Room modification after creation:** yes — control points are live objects; dragging fires `OnControlPointsChanged` → rebuild. ✅ The Suite adds **room splitting** and per-item actions (`FloorPlanItemAction { EditPoints, Split, Move, Remove, Paint }`) 📄, plus `SpaceType.SingleWall` for free-standing walls and `SpaceType.Outside` for terraces.

### 2.2 Scalability: "thousands of users creating rooms"

The architecture is **client-side and file-based** — each user generates meshes locally, so user count is irrelevant to the mesh system. What matters:

- **Per-device rebuild cost** (Section 9) — comfortably fine at room/house scale on mobile.
- **Persistence/multi-user is yours.** The Suite ships a self-hosted web portal (source on GitHub: `Exoa-Interactive/HomeDesigner-WebPortal`) with registration/auth/cloud project storage/sharing; you point the SDK at your server URL in `HomeDesignerSettings` 📄. **The docs do not document its REST endpoints, auth scheme, or DB schema** — treat it as starter code to read, not a contract. For production scale (accounts, sync, sharing, catalogs) build your own backend; the JSON-document model makes that trivial (each project is one small versioned document — a natural fit for object storage + a metadata DB).
- 🔶 Verdict: generation architecture scales fine; the vendor portal is demo-grade; persistence/collaboration is your build.

---

## 3. Wall System

### 3.1 How walls are actually built (✅ CONFIRMED, source — `ProceduralRoom.GenerateWalls` / `CreateWallWithOpening`)

1. The room polygon is offset **inward** by `interiorWallThickness` using Clipper's `PathOffsetter`. Wall thickness is a *polygon offset*, and **corners are resolved by Clipper's join logic — there is no special-case corner code at all.** Corners are implicitly watertight and mitered.
2. For each polygon edge, a wall is built **in 2D wall-local space**: a rectangle `(0,0)→(length, height)`.
3. Openings intersecting that edge are subtracted (Section 4).
4. The remaining polygon-with-holes is tessellated flat; hole outlines are **extruded by wall thickness** to create reveal/jamb faces (`MathUtils.Extrude`).
5. The mesh is rotated into place with a **Y-axis-constrained rotation** (we fixed a `FromToRotation` 180°-flip bug for walls running in exact −X during this review — evidence the codebase still has live edge cases ✅).
6. **Each wall segment gets its own `MeshFilter`/`MeshRenderer`** (`separateWallsList`) — this is why per-wall material painting and per-wall selection work, and it keeps meshes small.
7. **Exterior shell:** `ProceduralBuilding` unions all room contours (Clipper), offsets **outward** by `exteriorWallThickness`, and runs the same `CreateWallWithOpening` with `inward=false`. Openings cut through *both* interior wall and exterior shell because both consult the same openings list. ✅

### 3.2 Quality assessment

- **Mesh cleanliness:** Excellent. Minimal planar tessellation + thin extrusions. No hidden faces, no boolean slivers, no T-junction debris. 🔶 Vertex counts per wall in the dozens.
- **UVs:** Planar UVs from wall length/height (`MathUtils.GenerateUVs`) — correct for tiling materials. **No UV2/lightmap channel.** ✅
- **Baked lighting:** ⚠️ Runtime-generated meshes **cannot be lightmapped** — `Unwrapping.GenerateSecondaryUVSet` is editor-only and baking is an editor process. A Unity constraint, not a plugin flaw; would apply equally to a from-scratch system. The plugin's answer: per-room **ReflectionProbe** auto-fitted to room bounds ✅ + realtime lights. For your product: realtime lighting + probes, or **Adaptive Probe Volumes** (Unity 6 URP) for authored showcase content.
- **Collision:** `MeshCollider` auto-added to walls/floor/ceiling + merged `roomBox` collider. ✅ Fine for placement raycasts; for XR physics consider swapping wall segments to box colliders (cheap to derive from the same data).
- **Superior to ProBuilder?** Yes — Section 5.1.

### 3.3 Weaknesses (✅ observed in source)

- `FloorController.GetGrid()` calls `FindObjectOfType<Grid>()` per invocation; `AppController` is a mutable singleton holding global wall settings. Workable, but global-state-heavy (the Suite partially addresses this with `ServiceLocator` 📄 — Section 8.4).
- Openings match walls by **proximity threshold** (`distance < 0.2f` from the wall line), not explicit wall references — robust for editing UX; a door dragged near two close parallel walls could match both. 🔶 Rare at 0.2 m.
- Legacy regenerates *all* rooms on any opening change (`OnRequestRebuildAllRooms` global event). Fine for a handful of rooms; needs dirty-flagging for big buildings (Section 9).

---

## 4. Doors, Windows and Openings — The Core Question

### 4.1 The technique (✅ CONFIRMED, source)

**It is not runtime CSG. It is not hidden geometry. The wall mesh is regenerated with the hole already absent.**

In `ProceduralRoom.CreateWallWithOpening`:

```csharp
var clipper = new PathClipper();
clipper.AddPath(subject, PolyType.ptSubject);        // wall rectangle (2D)
foreach (opening) {
    // opening rect computed from xPos (along wall), yPos, width, height
    clipper.AddPath(clip, PolyType.ptClip);          // hole rectangle (2D)
    md.Add(MathUtils.Extrude(clip, normal, thickness)); // jamb/reveal faces
}
clipper.Clip(ClipType.ctDifference, ref output);     // wall minus holes
tessellator.AddContour(output[i]);                   // triangulate result
```

Because subtraction happens in 2D polygon space *before* any 3D mesh exists, the result is always clean: no degenerate triangles, no coplanar-face artifacts — the classic failure modes of 3D boolean approaches simply cannot occur.

**Doors are special-cased**: their clip rectangle always extends from the floor (`y = −0.001`) to `doorsHeight`, regardless of `yPos`; windows use their configured vertical position. ✅

### 4.2 How the opening knows where it is (✅ CONFIRMED)

`ProceduralSpace.GetOpeningsBetweenPoints` projects each opening's `worldPos` onto each wall edge; anything within 0.2 m is claimed by that wall and given an `xPos` (distance along the wall). An opening is: **`{ type, width, height, xPos, yPos, worldPos, direction }`** — pure data. Moving a door = changing `worldPos` and firing a rebuild event. This is exactly the behavior in your reference app screenshots (drag/resize a door, wall re-opens around it live).

📄 Suite v3 opening record adds: `hasWindow` (glass panel in doors), `windowFrameSize`, `windowSubDivH/V` (mullion subdivision), `Archway` as a third `OpeningType`, and `directions : List<Vector3>` (wall normals) — richer, same architecture.

### 4.3 The door you *see* is not the hole

`ProceduralOpening` generates the visible door/window mesh procedurally (front/back faces via the same 2D-clip technique, glass quad, sphere handle, mullion cylinders) and is instantiated as a **separate GameObject positioned at the opening** (`FloorController.BuildOpening`). The wall does not know or care what the door looks like. ✅

📄 The Suite works the same way ("Spaces are built twice… the second pass cuts the wall holes"; `ProceduralOpening` supports Door/Window/Archway). **No documented API for supplying a custom door prefab to the opening system** — the `DoorsWindows` furniture category places *decorative* models only. 🔶

### 4.4 Can you use arbitrary downloaded GLB doors? — YES (with a thin adapter you write)

Because hole ≠ door model, integration is a small, fully automatable adapter:

```csharp
// Pseudocode — works against both legacy and Suite architectures
public GenericOpening CreateOpeningForModel(GameObject glbDoor, Vector3 wallPoint, Vector3 wallDir)
{
    Bounds b = glbDoor.GetRendererBounds();          // measure the model
    var op = new GenericOpening {
        type   = OpeningType.Door,
        width  = b.size.x,                            // hole matches model
        height = b.size.y,
        worldPos = wallPoint,
        direction = wallDir,
    };
    openings.Add(op);
    RebuildWallsEvent();                              // hole appears
    glbDoor.transform.SetPositionAndRotation(
        AnchorFor(op),                                // same math as BuildOpening()
        Quaternion.LookRotation(wallDir));
    return op;
}
```

Per-model setup: **none beyond bounds measurement**, *if* catalog models follow conventions (pivot at hinge-side bottom corner, closed door, +Z facing out). For arbitrary internet GLBs, add a one-time normalization step (re-pivot, measure, thumbnail) at import — the same step you already need for furniture. Edge cases: doors wider than their wall segment (already clamped ✅); visual frames thinner than the wall (the extruded jamb/reveal already exists — cosmetic liner only).

**Verdict: your biggest concern is a non-issue architecturally.** This design is *more* compatible with catalog doors than a CSG system would be, because the hole is parametric data you control. In the Suite, drive it through `HeadlessBuildingAPI.CreateDoor/CreateWindow` (Section 8.5) rather than touching internals.

---

## 5. Comparison With Your Current Approach and the Reference App

### 5.1 vs. your ProBuilder workflow

| Aspect | ProBuilder (current) | EXOA system |
|---|---|---|
| Where it runs | **Editor only** — users can never edit rooms in a build | Fully runtime ✅ |
| Openings | Model placed on wall surface, no hole (your stated problem) or destructive editor booleans | Real holes, parametric, non-destructive, live-editable ✅ |
| Geometry quality | Boolean ops leave n-gons, slivers, broken UVs | Clean 2D-clipped tessellation ✅ |
| Data model | Mesh *is* the data (opaque) | JSON floor plan *is* the data; mesh derived — ideal for save/load, sync, AI |
| Editing walls later | Manual vertex surgery | Drag a control point, everything regenerates |

Recommendation: **retire ProBuilder for rooms entirely.** Keep it only as an editor-time prop-modeling convenience.

### 5.2 vs. the reference application (your screenshots)

Reference app shows: 2D↔3D toggle, walk mode, doors embedded in walls with live resize handles + dimension chips (4′8″ × 9′2″), a door-type catalog (single/double/arch/entry/French) with instant swap, panel-based sliding doors.

- **Same core technique** 🔶: embedded, movable, resizable doors imply the identical data-driven-hole approach — parametric opening + separate door visual.
- **EXOA already matches:** embedded openings, move/resize with live wall re-cutting, 2D→3D, procedural door/window variants, dimension-driven editing.
- **Reference app does better / you must add:** (1) **direct 3D manipulation gizmos on openings** with dimension chips — an interaction layer over the existing data model (handles edit `width/height/worldPos` + fire rebuild); (2) **rich door catalog** — authored models, i.e. exactly your IKEA-style pipeline (Section 4.4); (3) imperial dimension chips — trivial UI.

### 5.3 Subsystem-by-subsystem: what IBMROS has / plugin solves / you still build

| Subsystem | You have today | Suite solves | You still build | Reuse or replace? |
|---|---|---|---|---|
| Room drawing & 3D generation | ProBuilder (editor-only) | ✅ entire runtime pipeline | Dimension chips, curved walls (if needed) | **Reuse Suite; retire ProBuilder** |
| Walls/corners/thickness | Manual modeling | ✅ parametric, mitered, per-wall materials | Box-collider variant for XR | Reuse |
| Doors/windows with real holes | ❌ (surface-placed models — your key pain) | ✅ parametric holes + rebuild | **GLB door visual adapter** (4.4); 3D drag handles | Reuse holes; **replace procedural door visuals with your catalog** |
| Furniture placement | Manual placement, no snapping | ✅ ghost preview, floor/wall snap, collision validation (Suite only) | **Runtime GLB module provider** (Section 6); catalog at scale | Reuse mechanics; **replace catalog/settings DB** |
| IKEA catalog & ingestion | ros-pipeline (SKUs, cat-tree, S3 snapshots) ✅ | ❌ (build-time settings DB only) | Server catalog, CDN, streaming, LOD; metadata mapping onto `SceneObjectItemData` | **Own entirely — core IP** |
| Save/load/versioning | ❌ for rooms | ✅ UnifiedBuildingData v3, versioned, migration-friendly | Your schema superset; server sync | Reuse format as base; own the superset |
| Undo/redo | ❌ | ✅ 50 JSON snapshots | Command layer if furniture-heavy scenes need finer grain | Reuse |
| Camera | — | ✅ TouchCameraLite (mobile-ready) | XR camera N/A (head-tracked) | Reuse for flat; N/A in XR |
| Room scanning | OpenRoomPlan + SilverTau RoomPlanKit ✅ | ❌ (roadmap "Planned" only) | Scan→`FloorSpaceItemData` converter | **Own — you're ahead of the vendor here** |
| XR | Quest/OpenXR experience in-house (NAPTIN) | ❌ none, none planned 📄 | Full XR interaction layer (Section 10) | Own |
| AI | — | ❌ (roadmap aspirational) | Everything in Section 11; Suite's headless API is the enabler | Own |
| Multi-user/accounts | — | Demo-grade self-hosted portal 📄 | Production backend | Own (read their portal for the SDK contract) |

---

## 6. Furniture System

### 6.1 What exists

- **Legacy (installed):** furniture module *not installed*. Remains: `ModuleController` (grid-snap flag, ground/ceiling-tile flags, outline highlight), `ModuleDataModels` (catalog JSON with `prefab, id, sku, type, price, title, description` — note **sku and price**: designed with shopping in mind ✅), snapping math in `Join.cs` (wall snap via raycast sweep, room containment, `LJoint/RJoint/FJoint` module-to-module joints), selection/outline/ghost system, Transform-based thumbnail generator.
- **Suite 2026** 📄: full system — "One collider is all it takes. Objects automatically detect and snap to floors, walls, and ceilings"; ghost preview; long-press drag; 15° arrow-key rotation; collision + floor-bounds validation; placement raycasts against two layers (`InteriorFloor`, `ExteriorFloor` — terraces included); `ModuleVariants` (runtime material/visual swap via `variantIndex`, no re-instantiation); 63 bundled prefabs (Appliances, BedRoom, DoorsWindows, Kitchen, Lights, LivingRoom, Patio).
- **Custom furniture pipeline** 📄 (exact steps): select prefab → `Tools > Exoa > Home Designer > Convert Game Object to Module` → resize generated BoxCollider to footprint → set category + thumbnail on `ModuleController` → **register in the `HomeDesignerSettings` ScriptableObject database** ("adding the prefab to the project folder alone is insufficient"). `HomeDesignerSettings` also stores material libraries, construction defaults, GLB-export toggle, and the web-portal URL.

### 6.2 The gap that matters for IKEA: runtime dynamic import

⚠️ **Both generations assume furniture is known at build time** (Resources folder / editor-registered settings DB, referenced by string `objectId`). **Runtime-downloaded GLB furniture is not supported out of the box** 🔶 (confirmed by absence: no docs mention runtime import; the converter is editor-only; the roadmap's "Online Modules Library" is *editor* pack import, not user-side).

But the module contract is small: root GameObject + BoxCollider footprint + `ModuleController` + catalog entry. Build a **runtime module provider**:

1. Download GLB → import with **glTFast** or UniGLTF (the Suite already depends on UniGLTF for export 📄 — it's in your dependency tree).
2. Compute `Bounds` → add BoxCollider footprint → attach `ModuleController` → their snapping/placement works, since it keys off the collider. 🔶 (high confidence; this is exactly what the editor tool automates.)
3. Replace catalog lookup (`objectId → settings DB prefab`) with `objectId → cached GLB path/URL`. This is the one place you *override* their code — isolate it behind your own `IModuleSource` interface.
4. Metadata (IKEA article number, price, dimensions): mirror the legacy `Module` sku/price pattern in your superset schema; `SceneObjectItemData.objectId` remains the join key to your catalog service.

**Millions of models:** 3D side = streaming + caching + LOD/impostors; catalog must live server-side with search. Their settings DB is demo-scale. Your existing **ros-pipeline** (real category tree, SKUs, S3 raw snapshots) is already the right shape — map its records to `objectId` + metadata and you own the catalog end-to-end.

---

## 7. Runtime Editing, Undo, Serialization

| Capability | Legacy (installed) | Suite 2026 |
|---|---|---|
| Draw rooms (2D control points, grid + path snapping) | ✅ | 📄 ✅ + `CreateRectangularRoom` helper |
| Edit room shape after creation | ✅ drag points | 📄 ✅ + `EditPoints`, `Split` |
| Add/remove walls | ✅ via room polygons; free-standing walls ❌ | 📄 ✅ `SpaceType.SingleWall` |
| Move/resize doors & windows | ✅ (2D plan + UI fields; `ReSnapControlPoints`) | 📄 ✅ + archways, door glass panels, mullion subdivision |
| Materials painting (per-wall) | ✅ separate wall meshes + `SpaceMaterialController` | 📄 ✅ Paint state; `RoomSetting { wallMat, floorMat, ceilingMat }` |
| Multi-floor | ✅ v2 format | 📄 ✅ `PreviewModeManager` full-height preview |
| Roofs | ✅ Flat/Hipped/Gabled (straight skeleton) | 📄 ✅ same, + overhang/thickness params |
| **Undo/redo** | ❌ **absent** | 📄 ✅ `UndoRedoManager`, 50 JSON snapshots; undo = destroy + rebuild |
| Save/load | ✅ JSON, `persistentDataPath`, v1→v2 migration in `DataModel` | 📄 ✅ `ProjectSerializer` (`SaveProject/LoadProject/TakeSnapshot`), unified `"v3"`, forward-compatible |
| Export | FBX (editor-triggered) ✅ | 📄 runtime **GLB on save** via UniGLTF (Section 12) |
| Cloud | `SaveSystem.Mode.ONLINE` stub ✅ | 📄 self-hosted web portal |

Notes:
- **Snapshot undo** (serialize whole building JSON, rebuild on undo) is crude but robust; validated by the compact format. For furniture-heavy scenes consider command-based undo later — don't build it up front. 🔶
- The versioned-JSON-with-migration pattern (✅ legacy `DataModel`; 📄 Suite "existing saves load correctly in newer versions") is exactly right; adopt it for your schema superset. ⚠️ No documented legacy-`FloorMapV2`→`v3` migration — write a converter if legacy saves matter (schemas map ~1:1 🔶).

---

## 8. API & Architecture Deep Dive (Suite 2026)

*Docs: 160+ scripts, 20+ controllers, 30+ UI components. This section is the reverse-engineered internal picture; 📄 unless noted.*

### 8.1 Layered architecture

```
┌────────────────────────────────────────────────────────────┐
│ UI layer (30+ components: UIFloorsMenu, UIFloorPlanMenu,   │
│ UIFurnishMenu, popups…) — replaceable; fires OnRequest*    │
├────────────────────────────────────────────────────────────┤
│ App shell: AppController state machine · HomeDesignerEvents│
│ (static Action hub) · BuildingUIBridge (scene↔UI sync)     │
├────────────────────────────────────────────────────────────┤
│ Orchestration: HeadlessBuildingAPI · BuildingFactory ·     │
│ ProjectSerializer · UndoRedoManager · ServiceLocator       │
├────────────────────────────────────────────────────────────┤
│ Scene controllers: BuildingController → FloorController →  │
│ SpaceController (Room/Outside) · OpeningController ·       │
│ ControlPointsController · InteriorDesigner · ModuleCtrl    │
├────────────────────────────────────────────────────────────┤
│ Procedural core: ProceduralRoom/Opening/Exterior/Roofs     │
│ (Clipper + LibTess + MeshDraft) — pure geometry from data  │
├────────────────────────────────────────────────────────────┤
│ Data: UnifiedBuildingData v3 (JSON, Newtonsoft custom)     │
└────────────────────────────────────────────────────────────┘
```

The load-bearing property: **every layer below the app shell is UI-independent** — the FAQ confirms fully headless operation (omit the Canvas + BuildingUIBridge). That is what makes XR and AI integrations clean.

### 8.2 State machine

`AppController : SingletonMonoBehaviour` — states: **Idle → Floors → FloorPlan → Paint → Furnish → Preview**. Transition = property assignment (`AppController.Instance.State = States.FloorPlan;`), which fires `OnAppStateChange`. "No hard-coded per-state logic — all behavior is event-driven"; UI panels subscribe and show/hide themselves. Adding a state = new enum value + subscribers. Clean, but note: state is *global* (one app mode at a time) — fine for your product shape.

### 8.3 Event system

**`HomeDesignerEvents`** — static `System.Action` delegates, categorized:

- File: `OnFileLoaded/OnFileCreated : Action<FileType>` · `OnFileSaved/OnFileChanged : Action<string, FileType>` · `OnScreenShotSaved : Action<string, MenuType>`
- Requests (UI→logic): `OnRequestClearAll : Action<bool,bool,bool>` · `OnRequestRebuild : Action` · `OnRequestRepositionOpenings : Action` · `OnRequestButtonAction : Action<ButtonAction,bool>` · `OnRequestFloorAction : Action<FloorPlanAction,string>` · `OnRequestFloorPlanItemAction : Action<FloorPlanItemAction,GameObject>`
- State/UI: `OnFloorChanged : Action<FloorData,int>` · `OnUndoRedoStateChanged : Action` · `OnDragEvent : Action<bool>` · `OnRenderForScreenshot : Action<bool>`
- Secondary hub: `CameraEvents` (e.g. `OnRequestObjectFocus`).

Custom UI drives the app by firing `OnRequest*` events — e.g. `HomeDesignerEvents.OnRequestButtonAction?.Invoke(ButtonAction.SaveProject, true)`. Documented caveat: statics aren't cleared on domain reload → always unsubscribe in `OnDisable/OnDestroy`; leaks cause duplicate handlers.

### 8.4 ServiceLocator

Lightweight DI: `Register<T>(service)`, `Get<T>()`, `Has<T>()`, `Clear()` (on scene unload). Docs recommend it over singletons "when writing new systems," but the shipped codebase still leans on `SingletonMonoBehaviour<T>` for AppController/ProjectSerializer/HeadlessBuildingAPI/BuildingFactory/BuildingUIBridge 🔶. Read: the vendor is mid-migration from singletons to DI; your code should register/resolve through your own interfaces regardless (Section 8.7).

### 8.5 HeadlessBuildingAPI — complete documented surface

`HeadlessBuildingAPI : SingletonMonoBehaviour` — "programmatic building creation without UI dependencies."

```csharp
// Buildings
BuildingController CreateBuilding(string name, BuildingConstructionSettings settings);
BuildingController CreateBuilding(string name, UnifiedBuildingData data);
BuildingController CreateBuildingFromJson(string name, string json);
string             GetBuildingJson(BuildingController building);
string             GetBuildingJson(UnifiedBuildingData data);
// Floors
FloorController    CreateFloor(BuildingController building);
FloorController    CreateFloor(FloorData data, BuildingController building);
// Rooms & openings
RoomController     CreateRectangularRoom(float w, float l, Vector3 pos, FloorController f, string name);
RoomController     CreateRoom(List<Vector3> points, FloorController f, string name);
RoomController     CreateRoom(FloorSpaceItemData data, FloorController f);
OpeningController  CreateDoor(FloorController f, Vector3 pos, float w, float h);
OpeningController  CreateWindow(FloorController f, Vector3 pos, float w, float h, float ypos);
OutsideController  CreateOutside(FloorSpaceItemData data, FloorController f);
void               DeleteSpace(SpaceController s, FloorController f);
void               DeleteOpening(OpeningController o, FloorController f);
// (Archway creation exists per controller docs; exact overload not shown 📄)
```

Canonical flow: create → populate → **`building.Build()`** (nothing is built until then 🔶) → optional `BuildingUIBridge.Instance?.ConnectBuildingToUI(building)` to attach the stock UI. Reference scene: `Demo_API` with `BuildingAPIExample`.

Supporting: `BuildingFactory.ConvertWorldPointsToNormalized / ConvertNormalizedPointsToWorld` (normalized coords make data grid-scale-independent — always convert before storing); `BuildingController` holds construction params + `GetData()/Build()/GetAllFloors()/SetCurrentBuilding()`; `FloorController.GetAllSpaces()/GetAllOpenings()/GetFloorData()`; `BuildingUIBridge.ConnectToUI/DisconnectFromUI(IObjectDrawer)`.

### 8.6 Data flow (one edit, end to end)

```
user drags door (UI/gizmo/XR/AI — any source)
  → OpeningController updates worldPos
  → HomeDesignerEvents.OnRequestRebuild
  → FloorController.Build() [pass 1: wall geometry → opening snap;
                             pass 2: hole cut via Clipper + LibTess]
  → meshes/colliders replaced; UndoRedoManager.TakeSnapshot()
  → OnUndoRedoStateChanged → UI refresh
```

### 8.7 Would this stay maintainable at 10×? What I'd redesign

**Keeps working at 10×:** the procedural core (pure functions over data), the JSON document model, the headless API, per-wall mesh granularity, event decoupling of UI.

**Strains at 10× (redesign or firewall):**

1. **Static event hub + mutable singletons** — global state makes parallel tests, multi-building scenes, and server-side use awkward. *Don't rewrite theirs*; route all your calls through one **command gateway** you own, and keep `Exoa.*` types out of your app code (`IRoomEngine` facade). If you later need multi-instance, you swap the facade's backend, not your app.
2. **Single active building** (`BuildingController.currentBuilding` global 📄) — blocks side-by-side comparison UX and multi-tenant server rendering. Same mitigation: facade + one-building-per-scene (additive scenes) if needed. 🔶
3. **Rebuild-the-world granularity** — global rebuild events are O(building) per edit. Add dirty-room filtering behind your gateway before large-building features.
4. **Settings DB & catalog** — editor-time ScriptableObject; replace with your `IModuleSource` (Section 6.2) from day one.
5. **Schema ownership** — define your superset of `UnifiedBuildingData v3` (IKEA metadata, user/session ids, your version field) immediately; write migration both directions. Never persist their raw schema as your canonical store.
6. **Testing** — the vendor ships an editor test assembly (`Exoa.HomeDesigner.Tests.Editor` 📄), but statics limit isolation. Your gateway is the testable seam: golden-file tests (JSON in → mesh stats/JSON out).

---

## 9. Performance

✅/🔶 Assessment from source + docs (docs contain **no performance guidance or documented limits** — no max rooms/floors/vertices 📄):

- **Mesh rebuild cost:** per-wall meshes are tiny (clipped rectangle + jamb extrusions). A 10-room floor with 20 openings ≈ a few thousand structural triangles. Clipper/LibTess at this size is sub-millisecond per wall. Dominant costs: GameObject churn (openings destroyed/re-instantiated per rebuild ✅), `MeshCollider` re-cooking, and the Suite's **double-pass build** 📄 (2× the geometry work per rebuild — still trivial at house scale). The 0.1 s throttle masks it during drags.
- **GC:** generation allocates freely (new Lists/MeshDrafts per rebuild ✅). Irrelevant at edit-time frequency; don't rebuild every frame mid-drag (throttle exists; rebuild-on-release is a cheap further win).
- **Global rebuilds:** any opening change rebuilds all rooms (legacy ✅). Add dirty-room filter for large buildings — small patch since rooms are independent objects.
- **Undo:** full destroy-and-rebuild per undo 📄 — acceptable at this scale; snapshot cap 50.
- **Quest:** geometry trivially light for Quest 3. Watch: per-room **ReflectionProbe** (bake-once-on-edit, never per-frame), MeshColliders (swap walls to boxes), and their screen-space UI (unusable in XR anyway — Section 10). Mobile touch is first-class 📄; fog force-disabled on Android/iOS ✅.
- **Vision Pro:** 🔶 no support claimed anywhere. visionOS + PolySpatial would require a shader/material audit (their standard/URP/HDRP materials → shader-graph conversion), UI replacement, and input rework — treat as a separate port, not a checkbox.
- **Large furniture libraries:** placement cost is per-instance raycast + collider checks — fine. The real scaling axis is **asset streaming** (your CDN/LOD pipeline), not the plugin.

---

## 10. XR Readiness

📄 **The Suite has no XR support and none is on the roadmap.**

Transfers cleanly to XR: all generated geometry + colliders (raycast-friendly), the data model, the **headless API (UI-free by design — the key asset)**. TouchCamera is irrelevant (head-tracked camera).

You must build: an **XR interaction layer** — XRI/hand-tracking grabs that write to the same data model (move opening = update `worldPos` + fire `OnRequestRebuild`; place furniture = raycast floor layers + set transform), teleport/locomotion (navmesh bakes fine on the generated floors 🔶), and world-space UI replacing their canvas. Estimated 🔶: weeks, not months, *because* data → generation → presentation is cleanly separated; nothing in the mesh pipeline cares where input comes from. Design rule: **never call their UI/controller MonoBehaviours from XR code — go through HeadlessBuildingAPI + events only.**

Your NAPTIN Quest/OpenXR experience is directly reusable here (interaction patterns, comfort, teleport).

---

## 11. AI Integration Readiness

Two properties make this architecture genuinely AI-ready:

1. **The floor plan is a compact, declarative, versioned JSON document** — ideal LLM target: small enough for one completion, schema-validatable, deterministically renderable to 3D.
2. **HeadlessBuildingAPI methods map one-to-one onto AI function-calling tools:**

| AI tool | Suite API |
|---|---|
| `create_building(name, settings)` | `CreateBuilding` |
| `add_room(points[])` / `add_rect_room(w,l,pos)` | `CreateRoom` / `CreateRectangularRoom` |
| `add_door(pos,w,h)` / `add_window(pos,w,h,y)` | `CreateDoor` / `CreateWindow` |
| `remove_room/opening(id)` | `DeleteSpace` / `DeleteOpening` |
| `get_state()` | `GetBuildingJson` |
| `load_design(json)` | `CreateBuildingFromJson` |

Per-goal architecture:

- **Prompt → room:** LLM emits your superset of `UnifiedBuildingData v3` → validate (polygon simplicity, opening-on-wall constraints) → `CreateBuildingFromJson`. Build the validator early; it also protects save/load.
- **AI interior designer / auto-furnish:** expose `GetBuildingJson` + your catalog (dimensions/categories) → model returns `sceneObjects[]` → placement validator (their collision/bounds checks 📄) rejects/repairs → deterministic post-pass (snap to walls, resolve overlaps via their snapping primitives).
- **Furniture recommendations:** catalog/embedding problem on your backend; plugin only *places* results.
- **Image/scan → editable room:** roadmap lists "Floor Plan Scanning" as *Planned* 📄 — don't wait. You already have **OpenRoomPlan + SilverTau RoomPlanKit** ✅; both produce wall/opening primitives that convert mechanically into `FloorSpaceItemData`. Hand-drawn sketch: vision model → wall segments → same JSON.
- **Room optimization:** state is JSON → search/scoring over documents, replayable through the headless API for preview.

**Do now so AI is easy later:** (1) your schema as a strict superset with your version field; (2) route **every** mutation — UI, XR, AI — through one command gateway over HeadlessBuildingAPI so undo, validation, and events are uniform; (3) keep the validator a standalone library usable server-side (headless Unity or a C# geometry port).

---

## 12. Export Pipeline & Platform Support

- **GLB export** 📄: via **UniGLTF v0.131.0** (git dependency + `UNIGLTF` scripting define). Export is **integrated into save only** — "Export GLB On Save" toggle in `HomeDesignerSettings`; a `.glb` is written next to the JSON. **No standalone programmatic export method is documented.** Scope: full model (walls/floors/ceilings/roofs/exterior; furniture if serialized). Not available on WebGL.
  - ⚠️ For your XR/preview pipelines, you'll likely want export-on-demand — expect to call UniGLTF directly yourself (small task; dependency already present). 🔶
- **No FBX in the Suite** (legacy had editor-only FBX; the `FBX_EXPORTER` define is deprecated 📄).
- **Platforms** 📄: Windows/Mac/Android/iOS; Built-in/URP/HDRP (pipeline switcher tool included); Unity 6 mandatory; **new Input System required** (remappable via `HomeDesignerInputActions`; regenerate via `Tools > Exoa > Create Input Actions Asset`). WebGL: core generation + serialization work, but no GLB export and sandboxed persistence; not an advertised target.
- **Web portal** 📄: self-hosted (GitHub `Exoa-Interactive/HomeDesigner-WebPortal`): accounts, cloud storage, sharing; SDK pointed at your URL; no usage fees. Endpoints/auth/DB **undocumented** — read the repo before relying on it.

---

## 13. Risks

1. **Vendor risk — the big one.** Single small developer; Suite v1.0 shipped 2026-05, v1.2 2026-07-04 (young update track); legacy assets were deprecated with paid migration and **breaking API + schema changes**. Assume it can happen again. Mitigations: full C# source ships (Extension Asset) — vendor the source into your VCS, wrap every EXOA call behind your interfaces (`IRoomEngine`, `IModuleSource`), own the schema superset.
2. **Licensing/activation** 📄: editor-menu activation (email + invoice), token stored locally; license check is editor-only and stripped from builds; deleting license DLLs raises a deliberate compile error. CI/team onboarding friction; the store's Extension Asset EULA governs seats regardless of "no seat limits" marketing. Budget seats; script activation into machine setup.
3. **No XR, no AI shipped** — both on you (Sections 10–11). Roadmap AI items are aspirational.
4. **Static events / single-active-building** — constrains testing and side-by-side UX until firewalled (Section 8.7).
5. **Bundled custom Newtonsoft.Json must not be replaced** (IL2CPP) 📄 — audit for conflicts with your project's `com.unity.nuget.newtonsoft-json` before importing the Suite; expect to reconcile duplicate-assembly errors. ⚠️ *This project already ships Unity's Newtonsoft package — flag for the spike week.*
6. **Legacy→v3 migration undocumented** — write a converter if legacy saves matter.
7. **No standalone GLB export API** — export-on-demand is a small custom task (Section 12).
8. **Docs/store version mismatch** (docs "v3.1" vs store "1.2") — cosmetic lineage artifact 🔶, but pin exact package versions in your manifest and changelog discipline on upgrade.
9. **Baked-lighting expectation** — runtime rooms mean realtime lighting + probes on any engine (Section 3.2); set stakeholder expectations early.

---

## 14. Gap Analysis vs. Commercial Planners (IKEA Planner / Planner5D / Floorplanner / Coohom / RoomSketcher)

**Suite 2026 gets you the structural core:** 2D↔3D room drawing, parametric openings, materials, multi-floor, roofs, undo, save/portal, GLB export, mobile touch — roughly the "Floorplanner base tier."

**Missing for commercial grade (build yourself):**

| Gap | Own it? | Notes |
|---|---|---|
| Product catalog at scale (search, CDN, streaming, LOD) | **Yes — core IP** | Section 6.2; ros-pipeline is your head start |
| Runtime GLB furniture import | Yes | thin adapter |
| Catalog doors/windows (real models in openings) | Yes | Section 4.4 adapter |
| 3D direct-manipulation gizmos + dimension chips | Yes | reference-app parity |
| Photoreal render/export (Coohom-style stills) | Later | cloud render service 🔶 |
| Collaboration/multi-user/accounts/sharing | Yes | portal is single-user starter code |
| XR walkthrough | Yes | Section 10 |
| AI suite | **Yes — core IP** | Section 11 |
| Measurements/annotations, imperial units | Yes | small |
| Curved walls, stairs, sloped ceilings | Plugin gap | roadmap: rounded corners only; stairs absent everywhere 📄 |

**Keep plugin-based:** polygon/room engine, wall+opening generation, serialization core, snapping primitives, undo. **Eventually replace (18–24 mo 🔶):** their UI entirely (yours from day one), the event layer (statics → your gateway/DI) if you go multi-building or heavy testing, and the catalog/settings DB immediately.

---

## 15. Final Recommendation

**Build on Home Designer Suite 2026 — adopt its structural core, heavily extend around it, and firewall it behind your own interfaces.** Neither "use as-is" nor "from scratch" survives scrutiny:

- **Not from scratch:** the hard, unglamorous 20% — polygon offsetting corner cases, winding normalization, opening-projection math, concave tessellation, the −X wall-rotation bug we personally fixed — is months of debugging with zero product differentiation. Clipper + LibTess + parametric openings is *the correct design* (evidently what your reference app uses too); rebuilding it buys risk, not value.
- **Not the legacy asset you have:** deprecated, no furniture module, no undo, no headless API. Upgrade (claim the loyalty discount).
- **Not as-is:** catalog, XR, AI, multi-user — your actual product — are outside its scope, and its app shell (static events, singletons, stock UI) should never leak into your codebase.

Your differentiation never touches wall meshes. It is: **IKEA catalog + dynamic GLB pipeline, XR walkthrough, and the AI design layer. Buy the commodity; build the moat.**

### Concrete next steps

1. **Purchase Suite 2026** (loyalty discount); verify license activation in your CI story. Unity 6000.4.6f1 ✓. **Spike-week checklist:** Newtonsoft duplicate-assembly reconciliation (Risk 5); run `Demo_API`; script a building via `HeadlessBuildingAPI` from JSON; confirm docs signatures compile (docs snippets show minor inconsistencies — trust the code).
2. **Door adapter (highest-value proof):** import one IKEA/GLB door via glTFast, auto-create its opening from bounds (Section 4.4), demo move/resize/swap in 3D. This single demo answers your original question end-to-end.
3. **Runtime module provider:** `IModuleSource` over downloaded GLBs (bounds → BoxCollider → ModuleController), replacing the settings-DB lookup; join to ros-pipeline catalog via `objectId`.
4. **Define your schema** as a versioned superset of `UnifiedBuildingData v3`; build the standalone validator.
5. **Command gateway:** route all mutations (UI/XR/AI) through one layer over HeadlessBuildingAPI (`IRoomEngine`); golden-file tests through it.
6. **Then** XR interaction layer (Quest first, reusing NAPTIN patterns), then AI prompt→JSON→building, wiring OpenRoomPlan/RoomPlanKit for scan-to-room.
7. **Fork and firewall:** vendor the source into your VCS; never let `Exoa.*` types leak above your interface layer.

---

*Report v2, compiled from direct source analysis of `Assets/Exoa/` (Floor Plan Designer 2025, legacy), official Home Designer Suite docs (homedesigner.exoa.dev/docs, "v3.1"), Asset Store listing (ID 271115, v1.2, 2026-07-04, $198), web-portal repo listing, and roadmap, as of 2026-07-17. ✅/📄 facts are verified; 🔶 items are engineering inferences to validate during the spike week.*
