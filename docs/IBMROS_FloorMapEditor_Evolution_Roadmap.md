# IBMROS Mobile — Floor Map Editor Evolution Roadmap
## Master Implementation Plan (supersedes `IBMROS_RoomDesigner_Implementation_Roadmap.md`)

**Version:** 1.0 · **Date:** 2026-07-18 · **Calibration:** solo dev (AI-assisted), phase-at-a-time execution.

**Philosophy (owner decision, binding):** the existing **Exoa Floor Map Editor is the foundation.** We do not replace it, rewrite it, or build a parallel planner. We evolve it — using everything learned from the Home Designer Suite 2026 research as the pattern book for *what to evolve toward*. Every phase begins with analysis of what exists; **Extend > Refactor > Replace**, and Replace is only permitted when extending or refactoring is demonstrably impractical.

**Companion documents:**
- `docs/EXOA_HomeDesigner_Technical_DueDiligence.md` (v2) — the factual basis; cited as *(DD §n)*.
- `docs/IBMROS_RoomDesigner_Implementation_Roadmap.md` — **superseded**, retained because several of its designs (opening-visual adapter, catalog pipeline, AI contract, validator) carry forward here in adapted form.

---

## Part I — Ground Rules and Current State

### 1. Rules of evolution (apply to every phase)

- **R1 — Analysis before code.** Each phase opens with the subsystem analysis table (What exists / How it works / Strengths / Weaknesses / What Suite 2026 does / Verdict / Why). No task starts until its verdict is written.
- **R2 — Extend > Refactor > Replace.** *Extend* = add alongside, Exoa code untouched or barely touched. *Refactor* = restructure Exoa code without changing behavior (guarded by before/after checks). *Replace* = last resort, and always behind the existing interface so the rest of the editor doesn't notice.
- **R3 — The editor ships after every phase.** No phase may leave `FloorMapEditor.unity` broken. Feature work happens on branches; the scene must always draw, save, and load.
- **R4 — Save-file compatibility is sacred.** `FloorMapV2` JSON remains the on-disk format. We extend it **additively** (new optional fields; Newtonsoft ignores unknowns, and Exoa's own v1→v2 converter ladder *(DD §7)* is the pattern for any future version bump). Every existing saved floor map must load forever.
- **R5 — We own this source now.** Upstream is deprecated; there will be no vendor updates. `Assets/Exoa/` is *our* code — we may edit it directly (we already have: wall-flip fix, fonts, URP materials). But every direct edit is tagged `// IBMROS:` so our diffs against pristine vendor source stay auditable.
- **R6 — New code lives in bridge folders.** Exoa compiles in `Assembly-CSharp` (no asmdefs), so integration code that touches Exoa types must too. Convention already established: `Assets/IBMROSBridge/<Feature>/`. Pure logic with no Exoa/Unity coupling may live in the parked `Assets/IBMROS/Engine` asmdef where it stays unit-testable.
- **R7 — The parked engine is an asset, not a rival.** `Assets/IBMROS/` (63 files, 64 green tests: schema, commands, validator, golden geometry harness, glTFast spike) is kept as (a) the geometry/validation test bed, (b) a reference implementation, (c) a quarry — we lift its pure classes (validator, snapping rules, catalog schema) into service of the Exoa editor wherever they fit. No new standalone-editor UI is built on it.

### 2. Baseline inventory — what exists today

| Layer | Component | State |
|---|---|---|
| Editor scene | `FloorMapEditor.unity` (the one scene) | Working: draw rooms/doors/windows/outside, materials, floors menu, save/load, thumbnails |
| 2D drawing | `ControlPointsController` (731 LOC): grid snap, path snap, axis assist | Working; monolithic; mouse-idiom inputs (Alt-delete, right-click) |
| Geometry | `ProceduralRoom/Space/Opening/Building/Roofs` — Clipper offset + boolean holes + LibTess *(DD §3–4)* | Working; we fixed the −X wall flip; rebuild-all on any change, throttled 0.1 s |
| Openings | Parametric holes (data ≠ visual) *(DD §4)*; procedural door/window/handle meshes | Working; **already being skinned** by `IBMROSBridge/CatalogOpenings/CatalogOpeningManager` (bounds-fitted catalog models, re-fit on resize, re-skin on rebuild) |
| Data | `DataModel.FloorMapV2` versioned JSON; `SaveSystem` (FILE_SYSTEM / RESOURCES / ONLINE stub); autosave absent | Working; no undo |
| App shell | `AppController` singleton, states `{Idle, Draw, PreviewBuilding, PlayMode}`; `GameEditorEvents` static delegates | Working; statics survive scenes; single active building |
| Input | `HDInputs` static keymap (F/S/Space/G/R/E, Esc, Alt), old Input Manager, `TouchSimulator` | Desktop-first; Space = 2D↔3D perspective toggle **keyboard-only**; Esc handled only in menu code path |
| Camera | `TouchCameraLite` orbit/pan/pinch + plan mode | Good; mobile-proven |
| Furniture | **Absent** (Interior module never installed). Scaffolding present: `ModuleController`, `Join.cs` snapping math, outline effect, thumbnail generator, module JSON catalog with SKU/price fields *(DD §6)* | To be built by us |
| Adjacent | `Assets/IBMROS/` parked engine + tests; `Assets/IBMROSBridge/CatalogOpenings/`; OpenRoomPlan (active dev — scan capture, TSDF, eval); SilverTau RoomPlanKit; glTFast spike validated | Assets to draw on |

### 3. What Suite 2026 has that we lack (the shopping list this roadmap works through)

From the due diligence *(DD §7–8, roadmap docs)*: **undo/redo (50 JSON snapshots) · a richer state machine (Floors→FloorPlan→Paint→Furnish→Preview) · room splitting · single free-standing walls · archways · rectangular-room quick tool · per-item actions (EditPoints/Split/Move/Remove/Paint) · furniture system with one-collider snapping, ghost preview, variants · HeadlessBuildingAPI · unified data file · runtime GLB export · smart snapping · redesigned UI/UX.** Each appears below in the phase where it lands, always as an *evolution* of what we have.

### 4. Phase map

```
P1 Stabilise ─► P2 Editing Workflow & UX (undo, cancel, selection) ─► P3 Missing Structural Features
     ─► P4 Room Manipulation & Rebuild Perf ─► P5 Doors & Windows (catalog) ─► P6 Furniture System
     ─► P7 IKEA Runtime Pipeline ─► P8 XR ─► P9 AI ─► P10 Commercial
(P5 continues work already started in IBMROSBridge; P9's scan lane runs parallel via OpenRoomPlan track)
```

Rough effort (solo weeks): P1: 1–2 · P2: 3–4 · P3: 3–4 · P4: 2–3 · P5: 3–4 · P6: 4–6 · P7: 4–6 · P8: 4–6 · P9: 4–8 · P10: 6+.

---

## Part II — The Phases

---

## Phase 1 — Analyse and Stabilise the Existing Floor Map Editor (1–2 weeks)

**Objective.** Make the editor a trustworthy foundation: audited, regression-guarded, debuggable, and safe with user data — before any feature work.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | A working editor with known rough edges: we already hit and fixed one geometry bug (−X wall flip); fonts and URP materials needed patching; logging is `HDLogger` with categories; no tests; no save backups |
| How it works | Event-driven rebuild-all; JSON saves straight over the previous file; errors surface as `AlertPopup` or silently |
| Strengths | Small surface (≈3.6 k LOC core); deterministic rebuild from JSON — ideal for golden testing; our patches prove the code is workable |
| Weaknesses | Zero regression protection; a bad save corrupts the only copy; debug affordances are editor-only key hacks (P to pause, I to hide UI) |
| Suite 2026 difference | Ships with demo scenes as implicit tests; still no automated tests either — we can do better |
| **Verdict** | **Extend** (add harnesses and safety around the code, not in it) |
| Why | Stability work requires no behavioral change; touching internals now would be unguarded surgery |

### Tasks

1. **Golden regression harness** — the single highest-leverage item. Reuse the parked engine's `GoldenAssert`/golden-file pattern (R7): for a set of fixture `FloorMapV2` files, load through `FloorMapReader`, capture mesh stats (vert/tri counts, bounds, checksums) per room/wall/opening, commit as goldens. Runs as an edit-mode test entering play mode in `FloorMapEditor.unity` or via a headless bootstrap. Every later phase is guarded by this.
2. **Save safety:** write-to-temp-then-swap in `SaveSystem`; keep N rolling backups (`{name}.json.bak1..3`); load-failure fallback to the newest backup. (Pure extension inside `SaveSystem` — ~50 LOC, tag `// IBMROS:`.)
3. **Autosave:** timer + after-N-changes snapshot to an `_autosave` slot; offer recovery on launch. (Uses the same serializer; this is also the undo groundwork for P2.)
4. **Bug backlog sweep:** reproduce and file every known irritation (touch gaps, Esc inconsistency, opening-near-corner behavior, thumbnail edge cases). Fix only crashes/data-loss now; UX fixes belong to P2 where they're designed, not patched.
5. **Crash/error visibility:** wrap `FloorMapReader.DeserializeToScene` and save paths in structured try/catch with user-facing recovery, not silent logs.
6. **Vendor-diff audit:** generate the diff of `Assets/Exoa/` vs pristine asset (we have the package archives) and commit it as `docs/exoa-local-patches.diff` — our `// IBMROS:` ledger, baseline for R5.

**Deliverables.** Green golden suite in CI; save/backup/autosave shipped; patch ledger committed; triaged backlog.
**Dependencies.** None. **Risks.** Golden harness flakiness from nondeterministic ordering — sort captured stats.
**Never postpone:** goldens, save safety. **Postponable:** everything cosmetic found in the sweep.

#### P1 progress (2026-07-18)

> **Correction (2026-07-18, owner decision):** the IBMROS-specific files outside `Assets/Exoa/`
> (the parked engine `Assets/IBMROS/`, `IBMROSBridge/CatalogOpenings/`, and the original Stability
> scripts) were **intentionally deleted** to restart the IBMROS implementation from a clean state;
> this document wasn't updated at the time. Standing rule going forward: **the repository is the
> source of truth over this document** — anything described here that can't be found in the
> codebase should be assumed gone. The Task 1/Task 3 files below were rebuilt from this
> document's descriptions and compile clean (Unity 6000.4.6f1 batch mode). Consequences
> recorded: R7's "parked engine" no longer exists — every "lift from parked engine" item
> (validator, polygon math, snap rules) now means *rebuild fresh*; the P5 `CatalogOpeningManager`
> head start is also gone and P5 starts from the parametric-opening architecture alone.
>
> **UI direction (owner decision, same date):** this is a mobile-first product; touch is the
> primary interaction model for every feature (keyboard shortcuts are editor-testing conveniences
> only). All UI is designed and implemented by the AI engineer. The existing Exoa uGUI canvas is
> neither presumed kept nor presumed replaced by UI Toolkit — the framework decision is made on
> technical merit, with trade-offs justified, when the P2 UX work begins in earnest.

- ✅ **Task 2 — Save safety** (`SaveSystem.cs`, tagged `// IBMROS:`): atomic temp-write-then-swap; rolling `.bak1..3` for `.json`; load-failure fallback to newest backup with deferred error alert. Bonus data-loss fix found during work: saves were `Encoding.ASCII` — any non-ASCII character (room names) was silently corrupted to `?`; now UTF-8.
- ✅ **Task 5 — Crash/error visibility** (`FloorMapReader.cs`): the load path cleared the scene *before* parsing, so a corrupt file left an empty editor with a silent exception. Now: parse guarded, backups tried in order, user-facing "Recovered From Backup" / "Corrupt File" alerts; disk never touched on failure.
- ✅ **Task 3 — Autosave** (`IBMROSBridge/Stability/AutosaveService.cs`, extension-only): self-bootstraps only in scenes containing `FloorMapSerializer`; dirty-tracks via rebuild events; saves every 60 s to `FloorMapsAutosave/{name}.json` (separate folder — file-list UI unpolluted); on file open, offers restore when autosave is newer. Also the undo groundwork for P2.
- ✅ **Task 1 — Golden harness shipped, now fully headless** (`IBMROSBridge/Stability/GoldenRegressionRunner.cs`): fixtures = FloorMapV2 JSONs as TextAssets under `Resources/IBMROSGoldenFixtures/`; loads through the real serializer, settles past the 0.1 s rebuild throttle, captures sorted per-mesh stats (verts/tris/bounds), line-diffs vs committed goldens. Context-menu driven in-editor (Adopt saves → Capture → Verify) **and batch-driven headless**: `IBMROS_GOLDEN_MODE=capture|verify Unity -batchmode -executeMethod IBMROS.Bridge.EditorTools.GoldenBatchRunner.Run` (no `-quit`; process exits 0/1 with the result — the CI hook). **Three synthetic fixtures authored and committed** (rect room · concave L-room + door + window · two floors + outside area), each validated through the real vendor deserializer by the dotnet harness (36 assertions green). Remaining: run the capture command once while the editor is closed, commit the goldens.
- ✅ **Task 6 — Patch ledger** committed: `docs/exoa-local-patches.diff` (baseline = pre-migration Downloads copy; upstream deprecated, no pristine archive exists — caveat recorded in the file header).
- ⏳ **Task 4 — Bug backlog sweep**: pending; capture during first golden run session.

---

## Phase 2 — Improve the Core Editing Workflow (UX + Undo) (3–4 weeks)

**Objective.** Close the usability gap with Suite 2026 and the reference app: undo/redo, cancel-anywhere, touch-native editing, visible mode state, better selection — treated as first-class engineering, not polish.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Editing works but with desktop idioms: Alt+click deletes points, right-click is "option", Esc handled only inside `UIFloorMapMenu`; Space toggles 2D↔3D perspective (keyboard-only, `HDInputs.ChangePlanMode`); selection/highlight via outline exists for some objects; no undo of any kind *(DD §7)*; feedback while drawing is minimal |
| How it works | `HDInputs` static polling of legacy Input Manager; `ControlPointsController` owns draw/edit interaction in one 731-LOC class; UI items (`UIBaseItem`/`UIRoomItem`/`UIOpeningItem`) each own settings + buttons; `AppController.States` is coarse (`Idle/Draw/PreviewBuilding/PlayMode`) |
| Strengths | The interaction *model* (control points, snapping, live rebuild) is exactly right and users like it; snapping (grid/path/axis) already good *(DD §2.1)*; perspective toggle and floors menu already exist — they need surfacing, not building |
| Weaknesses | No undo is the #1 deficiency; inputs untouchable on mobile; no visible tool state; can't cancel a half-drawn room cleanly everywhere; deletion undiscoverable |
| Suite 2026 difference | Undo/redo = 50 JSON snapshots, `TakeSnapshot()` before each user action *(DD §7)*; explicit `ButtonAction` enum with Undo/Redo buttons; redesigned UI with visible mode chips; touch-first controls 📄 |
| **Verdict** | Undo: **Extend** (snapshot layer over existing serializer — zero engine change). Inputs: **Refactor** (`HDInputs` gains an action-based layer; call sites migrate gradually). Tool/selection UX: **Extend** (new bridge components + small `// IBMROS:` hooks in `ControlPointsController`). |
| Why | The Suite itself proves snapshot-undo works *on this exact architecture* — their undo destroys and rebuilds from JSON, which our editor already does on every load. Replacing the input system wholesale (new Input System migration) is not justified yet: an action facade gives touch bindings now and keeps the migration option open. |

### Tasks

1. **Undo/redo (the Suite's #1 lesson, transplanted).** `UndoRedoService` (bridge): before each mutating action, serialize the current floor map via the existing `FloorMapSerializer` into a bounded ring (50, like the Suite); undo/redo = clear + `FloorMapReader.DeserializeToScene(json)` — the exact code path load already exercises, so it inherits P1's golden guarantees. Hook points: the existing `GameEditorEvents` (`OnControlPointsChanged`, `OnRequestRebuildAllRooms`, settings changes) + explicit calls from tools; **coalesce drags** (snapshot on drag-start, not per frame). UI: undo/redo buttons + 3-finger-tap gesture.
2. **Action-based input facade.** `EditorActions` (bridge): `Confirm, Cancel, Delete, ToggleView, Save, Undo, Redo…` — each mappable to key, UI button, and touch gesture. `HDInputs` call sites route through it (refactor, mechanical). Immediate wins: **Esc/back-swipe cancels the active tool from anywhere**; Android back button behaves.
3. **Touch-native editing:** replace Alt-delete with select-point → contextual delete button (and long-press menu); replace right-click "option" with long-press; grow point/handle hit-targets to ≥44 pt on touch.
4. **Visible mode state:** persistent mode chip (Draw Room / Draw Door / Edit / Preview) + contextual hint line ("tap to place point — long-press to close room"). Maps 1:1 onto existing `AppController.States` + UI item draw-mode flags; display only, no state-machine surgery yet (that's P3).
5. **2D↔3D toggle as UI:** surface `ChangePlanMode` (Space) as a prominent button like the reference app's "2D" pill; smooth camera transition via existing TouchCameraLite plan/persp modes.
6. **Selection workflow:** unify selection (rooms, openings) through one `SelectionService` (bridge) driving the existing outline effect consistently; tap = select, tap-again = properties, tap-elsewhere = deselect; properties panel slides in (reuse existing `UIRoomItem`/`UIOpeningItem` panels — rehomed, not rewritten).
7. **Editing feedback:** live wall-length labels while drawing/dragging (world-space TMP, cm/inches setting — groundwork for P5's dimension chips); snap indicators (flash grid/axis line on snap, like the Suite); invalid-polygon preview in red (lift the polygon-simplicity check from the parked validator, R7).
8. **Quality-of-life sweep from P1 backlog:** double-tap to focus room (`CameraEvents.OnRequestObjectFocus` exists); save-state indicator (dirty dot); confirm-discard on exit.

#### P2 progress (2026-07-18) — feature milestones (roadmap resumed after the A-track)

- ✅ **Task 1 Undo/redo** — shipped and hardened far beyond the original snapshot design (identity-keyed reconcile restore, self-stabilizing history; see A5 notes in Part IV).
- ✅ **Task 5 2D↔3D toggle** — the existing toolbar camera `ActionButton` (`camAction=SwitchPerspective`) already provides it; confirmed, no new control needed.
- ✅ **Task 6 Selection workflow + Task 3 touch deletion** (`IBMROSBridge/Interaction/SelectionService.cs` + `SelectionActionBar.cs`): tap a room/door/window/outside area in 3D → raycast resolves the hit collider to the owning item via `IObjectDrawer.UI.ItemUniqueId` (A2 identity) → reversible `_BaseColor` highlight (MPB, re-applied across rebuilds) → a bottom-centre contextual bar offers Duplicate/Delete, routed through the A3 gateway (`FloorPlanEditor.DuplicateItem/DeleteItem`, one undo step each). Tap = short, low-movement pointer up not over UI, so it never fights TouchCameraLite drags. Selection self-clears if its item is deleted/undone. Headless-proven: a downward raycast onto the room resolves to its id; select-by-id, duplicate, delete all verified. *This is the architecture paying dividends — selection is ~two small bridge files because identity + gateway already existed.* Interactive polish (highlight look/feel, long-press for a fuller menu) pending a play-test.
- ⏳ Remaining P2: cancel-anywhere / Android-back (Task 2 action facade), visible mode chip + hint line (Task 4), live dimension labels + snap indicators (Task 7), QoL (double-tap focus, dirty-dot, confirm-discard) (Task 8).

**Deliverables.** Editor usable one-handed on a phone: draw, edit, cancel, delete, undo, toggle 2D/3D — no keyboard. Undo suite green against goldens.
**Dependencies.** P1 (autosave/serializer hooks, goldens). **Risks.** Undo hook coverage (a mutation nobody snapshotted) — mitigate by snapshotting on *any* `OnRequestRebuildAllRooms` as a catch-all; `ControlPointsController` fragility — touch it only at tagged hook points.
**Never postpone:** undo, cancel-anywhere, touch deletion. **Postponable:** gesture customization, keyboard remapping UI.

#### P2 progress (2026-07-18)

- ✅ **Task 1 — Undo/redo shipped (final architecture, re-derived from first principles same day):** **transaction-scoped state mementos behind a command-gateway API.** Source verification killed the pure-command alternative: Exoa does not announce all mutations (`OutsideController` rebuilds fire no global event; renames fire *nothing* — `UIBaseItem.OnChangeName` is an empty body), so command inverses could never be coverage-complete without rewriting vendor interaction code; state capture is correct by construction (same family as Blender/Unity-editor undo — appropriate for a single-user small-document editor; op-based only pays off for realtime collaboration, which P10 explicitly postpones). Components: `SnapshotHistory.cs` + `SnapshotCodec.cs` (pure C#, zero Unity deps, byte-budgeted ring — 50 steps / 4 MB — with gzip >4 KB, exact-string dup rejection via cached current, eviction never touches the cursor; **26 unit assertions run green under plain dotnet**) and `UndoRedoService.cs` (orchestrator: rebuild events raise change signals → 0.4 s settle → capture; 5 s backstop sweep covers the silent mutation paths; no capture while a finger is down, so a drag = one step; `BeginAction(label)`/`NotifyMutation(label)` gateway for furniture/AI/XR to make atomic labelled steps — the seed of P9's mutation gateway; `UndoLabel`/`RedoLabel` exposed for future toast UI). Restore replays the load path exactly, then `CancelInvoke("DelayedFocus")` on `AppController` kills the camera-refocus jolt (no Exoa edit needed). Keyboard shortcuts are `#if UNITY_EDITOR` only — compiled out of device builds per the mobile-first mandate. **Touch-first controls (primary interaction):** `UndoRedoHud.cs` self-bootstraps a runtime overlay canvas (no scene edits) with thumb-sized Undo/Redo buttons top-centre, safe-area aware, greyed via history state, hidden in PreviewBuilding/PlayMode; plus a 3-finger-tap undo gesture. Keyboard (Ctrl/Cmd+Z etc.) kept as editor-testing convenience only. `UndoRedoButton.cs` is the reusable wiring component if buttons are later rehomed into a designed toolbar. The HUD is deliberately one restyleable file, pending the P2 UI-framework evaluation (uGUI vs UI Toolkit — decided on merit before the big UX work). **Documented trade-off for future phases:** restore is a full document rebuild (the only correct apply mechanism Exoa has today); when P4 lands scoped/dirty rebuilds, `UndoRedoService.Restore` is the single place to teach about them. If documents ever grow to MBs (huge furnished scenes), revisit `SnapshotHistory` with per-item structural sharing — gzip makes this a non-issue at current sizes.
- **Remaining human steps:** play-mode smoke test (draw → edit → undo via HUD → redo → save); then run goldens to certify. Tasks 2–8 not started.

---

## Phase 3 — Add the Missing Suite-2026 Structural Features (3–4 weeks)

**Objective.** Bring the floor-plan feature set to Suite 2026 parity where it matters for interiors: rectangular-room tool, room splitting, single walls, archways, per-item actions — plus the state-machine cleanup those features force.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Rooms (arbitrary polygons), doors/windows/openings, outside areas, multi-floor menu, roofs. Item actions = display/duplicate/delete buttons per UI item |
| How it works | Every space type is a `FloorMapItem` with `normalizedPositions`; `UIBaseItem` subclasses per type; `AppController.States` coarse; adding a type = data enum + UI item + procedural generator (the `Outside` type proves the recipe) |
| Strengths | The `FloorMapItemType` + generator pattern is genuinely extensible — the Suite's own additions (`SingleWall`, `Archway`) are just new enum members in their schema *(DD §7)* |
| Weaknesses | No rectangle quick-draw (every room is point-by-point); no way to split a room; no free-standing walls (partition walls require fake thin rooms); archway = generic "Opening" without proper visuals; coarse states make tool modality implicit |
| Suite 2026 difference | `CreateRectangularRoom` API + tool; `FloorPlanItemAction {EditPoints, Split, Move, Remove, Paint}`; `SpaceType.SingleWall`; `OpeningType.Archway`; states `Floors→FloorPlan→Paint→Furnish→Preview` 📄 *(DD §8)* |
| **Verdict** | Features: **Extend** (new enum members, tools, and one generator, following the existing pattern). State machine: **Refactor** (split `Draw` into explicit tool-states; keep the enum, add members — the Suite's FAQ itself says states are data-driven and extensible *(DD §8)*). |
| Why | Every one of these features has a proven slot in the existing architecture; none needs engine changes. Replacement is indefensible here. |

### Tasks

1. **Rectangular-room tool:** two-tap (corner→corner) creating a 4-point `FloorMapItem`. Pure tool-layer; 80% of real rooms. ✅ **shipped 2026-07-18** (`IBMROSBridge/Interaction/RectRoomTool.cs`): a "Rect Room" button arms the tool; tap two grid-snapped corners → a complete room via `FloorPlanEditor.CreateRectRoomFromCorners` (one undo step, min-size guard rejects accidental taps); rubber-band outline previews before the 2nd tap; stays armed for consecutive rooms, Esc/button exits; taps are suppressed for SelectionService while armed. Ground point = camera ray ∩ y=0 plane; corners snap via `Grid.GetNearestPointOnGrid`. Entirely on the existing gateway + grid — no engine changes. Headless-proven: corner→room produces correct dimensions; degenerate pair rejected. Interactive feel (preview, tap cadence) pending play-test. *First P3 feature; landed cheaply because A3's `CreateRectRoom` already existed.*
2. **Room splitting:** draw a line across a room → polygon split (2D segment/polygon intersection — lift from parked `PolygonMath`, R7) → replace one `FloorMapItem` with two; openings re-bind automatically via existing proximity projection *(DD §4.2)*. One undo step.
3. **Single free-standing walls:** new `FloorMapItemType.SingleWall`: a 2-point (or polyline) item generating wall segments via the existing `CreateWallWithOpening` (it already takes p0/p1 — the generator exists, only the item type and drawing tool are new). Openings work in partition walls immediately, since binding is proximity-based.
4. **Archway type:** promote generic `Opening` to a proper archway (no door panel/glass, optional arc top later); mostly visual — `ProceduralOpening` already special-cases types.
5. **Per-item actions menu:** on selection (P2 `SelectionService`): Edit Points / Split / Move / Duplicate / Remove / Paint-stub — mirroring `FloorPlanItemAction`. Move = drag whole room (translate all points, one undo step).
6. **State-machine refactor:** `States` gains explicit members (`DrawRoom, DrawRectRoom, DrawWall, PlaceOpening, EditItem…`); tool classes subscribe to state, replacing implicit "which UI item is in draw mode" flags. Behavioral no-op guarded by goldens + manual matrix.
7. **Floors menu polish:** duplicate-floor (serialize level → new `uniqueId` — `FloorMapLevel.GenerateUniqueId` exists), reorder, per-floor visibility in 3D.

**Deliverables.** Feature-parity demo: split a room, add a partition wall with a door in it, archway between rooms — all undoable.
**Dependencies.** P2 (selection, undo, actions). **Risks.** Splitting concave rooms (test suite of nasty polygons); state refactor regressions (goldens + the P2 mode chip makes state visible, aiding QA).
**Never postpone:** rect tool, per-item actions. **Postponable:** arc-topped archways, roof interactions with split rooms (interiors don't care).

---

## Phase 4 — Improve Room Editing, Manipulation, and Rebuild Performance (2–3 weeks)

**Objective.** Make editing feel instant and robust as plans grow: dirty-room rebuilds, better point editing, room-to-room snapping, live validation.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Rebuild-all-rooms on any opening change (`OnRequestRebuildAllRooms` global) *(DD §3.3)*; per-room generators are already independent objects; 0.1 s throttle |
| How it works | Events fan out to every `RoomController`/`OpeningController`; each regenerates fully; GameObjects for openings destroyed/re-instantiated per rebuild |
| Strengths | Per-room independence means dirty-tracking slots in naturally; throttle pattern proven |
| Weaknesses | O(all rooms) on any edit; GameObject churn; on large plans (10+ rooms, many openings) mobile frame hitches during drags |
| Suite 2026 difference | Two-pass build with configurable throttle 📄; still largely rebuild-heavy — we can *exceed* the Suite here |
| **Verdict** | **Refactor** (scoped rebuilds inside existing event flow) + **Extend** (pooling) |
| Why | The event bus already carries enough context to scope rebuilds; no architectural change required |

### Tasks

1. **Dirty-room rebuilds:** extend rebuild events with scope (which room ids / which wall). An opening edit rebuilds only rooms whose polygon is within its influence (reuse the ≤0.2 m proximity test). Measure before/after on a 10-room fixture.
2. **Opening GameObject pooling:** stop destroy/instantiate per rebuild in `OpeningController.Rebuild`; reuse instances (also stops P5's catalog skins from re-downloading/re-fitting needlessly — coordinate with `CatalogOpeningManager`'s re-skin hook).
3. **Point-edit upgrades:** insert point on edge (tap edge midpoint handle), straighten/align segment, numeric wall-length entry in properties panel (type 3.2 m → point moves; the reference app's dimension-driven editing).
4. **Room-to-room snap:** while drawing/dragging near an existing room's edge, snap to it (shared-wall workflow); `ISnapRule`-style addition to the existing snap set — lift the rule pattern from the parked engine's `Snapping.cs` (R7).
5. **Live validation:** self-intersection and min-area checks (parked validator, R7) run during drag; red preview + refuse commit; guards P9's AI output later too.
6. **Perf pass:** profile a furnished-later worst case; budget: point-drag rebuild < 8 ms mid-Android on a 10-room plan.

**Deliverables.** Large-plan editing without hitches; numeric editing; snap-to-room; validation shipping.
**Dependencies.** P2–P3. **Risks.** Scoped-rebuild misses a dependent (exterior shell spans rooms — when in doubt it rebuilds; correctness beats speed).
**Never postpone:** dirty rebuilds, validation. **Postponable:** numeric entry for angles; pooling beyond openings.

---

## Phase 5 — Upgrade the Door and Window System (3–4 weeks)

**Objective.** Finish what `IBMROSBridge/CatalogOpenings` started: reference-app-grade doors/windows — catalog models in real holes, 3D drag/resize handles with dimension chips, instant type swap.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Parametric holes (data ≠ visual — the architecture's crown jewel *(DD §4)*); procedural visuals (`ProceduralOpening`); **`CatalogOpeningManager` (ours, working):** skins procedural openings with catalog models, bounds-fitted, self-calibrating, re-fits on resize, re-skins on rebuild; editing via 2D plan + UI sliders |
| How it works | Hole cut by Clipper difference from `{width,height,xPos,yPos}`; visual instantiated at anchor; our manager finds skinnable openings post-rebuild and overlays models |
| Strengths | Any model fits any hole with zero engine change — validated in practice by our bridge; resize machinery already rebuilds walls live |
| Weaknesses | No 3D direct manipulation (sliders only — the reference app's biggest lead *(DD §5.2)*); no per-opening model *choice* persisted; no catalog UI; procedural handle/glass still shows on some types; model normalization is heuristic |
| Suite 2026 difference | Still procedural-only visuals, no custom-model API 📄 *(DD §4.3)* — **we are already ahead of the Suite here**; their `DoorsWindows` category is decorative only |
| **Verdict** | **Extend** (persist model choice in schema; palette UI; gizmos) + **Refactor** (`CatalogOpeningManager` grows into the `IOpeningVisual` shape from the superseded roadmap: procedural fallback / catalog skin / always-procedural jamb liner) |
| Why | The hard part is done and proven in-repo; what remains is data plumbing and interaction. Replacement of `ProceduralOpening` is explicitly rejected — it stays as the zero-content fallback and the hole-parameter source of truth. |

### Tasks

1. **Persist model choice (R4 additive schema):** `FloorMapItem` gains optional `catalogOpeningId`, `variantIndex`, `flipX` (hinge side). Old saves: null → procedural visual, unchanged.
2. **`IOpeningVisual` consolidation:** formalize the manager into per-opening visual hosts: `ProceduralVisual` (existing meshes) | `CatalogGlbVisual` (existing skin logic) + **procedural jamb liner always rendered** — covers wall-thickness reveal so thin models still look embedded (the one idea worth lifting verbatim from the superseded roadmap).
3. **Catalog palette UI:** door/window browser (thumbnails via existing `ThumbnailGeneratorUtils` — GLB-friendly, per prior investigation) inside the existing UI framework; tap-to-swap on selected opening; swap = one undo step, hole re-parametrizes to model dims (clamped to wall).
4. **3D manipulation gizmos:** world-space handles on the selected opening — side handles (width), top (height), sill (windows, `ypos`), body-drag along wall (reuses existing re-snap machinery `ReSnapControlPoints`); **live dimension chips** (P2's labels, upgraded; cm/ft-in). Corner-crossing drag re-binds wall naturally (proximity binding) — test explicitly.
5. **Model normalization pass:** per-catalog-item metadata (pivot offset, facing, fit mode `Resize|FixedSize`, min/max dims) replacing pure heuristics; import-time bounds measurement; store in the catalog manifest (shared with P7 furniture pipeline).
6. **Window/door defaults from model:** placing a catalog item creates the opening at the model's native size (manufacturer-true), user-resizable within min/max.

**Deliverables (the thesis demo).** On device: place catalog door → hole cuts → drag along wall and across a corner → resize via handles with chips → swap single→French door instantly → undo it all. Reference-app parity achieved.
**Dependencies.** P2 (selection/undo), P4 (pooling coordination). **Risks.** Wild GLB pivots/hierarchies — metadata overrides; handle usability on small screens — fat targets + magnifier loupe if needed.
**Never postpone:** schema persistence, jamb liner, dimension chips. **Postponable:** swing-arc visualization, casing/trim styles, sliding-door tracks.

---

## Phase 6 — Upgrade (Build) the Furniture System (4–6 weeks)

**Objective.** The Interior module Exoa never shipped us — built *into the existing editor* on Exoa's own scaffolding: browse, ghost-place, snap, manipulate, persist.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Scaffolding only: `ModuleController` (grid-snap/tile flags, outline), `Join.cs` (snap-to-wall raycast sweep + normal alignment, polygon containment, module-to-module joints — the crown-jewel math, per prior investigation), thumbnail generator, module catalog JSON (`prefab, sku, price, title` *(DD §6.1)*), `SceneObject` serialization struct, `InteriorProjectV2` file format with per-floor `sceneObjects` — **all present, none wired to a placement controller** |
| How it works (design intent) | Interior module loaded prefabs from `Resources` by name, placed with joint/wall/grid snapping, saved `SceneObject{prefabName, transform, variant}` per floor |
| Strengths | The hard math (snapping, containment) and the data format already exist and match the Suite's model conceptually |
| Weaknesses | No placement controller, no UI, `prefabName`-string references (anti-pattern *(DD §6)*), Resources-based loading incompatible with runtime downloads |
| Suite 2026 difference | Full system: one-collider snapping, ghost preview, long-press drag, 15° rotation, variants, editor-registered settings DB 📄 — but **also** build-time-only content *(DD §6.2)* |
| **Verdict** | **Extend + Build**: build the placement controller and UI ourselves (nothing to refactor — it's absent), reusing `Join.cs`/outline/thumbnails as-is; **Replace** one design decision at birth: content references are `objectId` (catalog), never `prefabName` — since no legacy furniture saves exist yet, this costs nothing now and saves a migration later |
| Why | This was always going to be ours *(DD §6.2)*; Exoa's scaffolding cuts the work substantially; the Suite's UX (ghost, one-collider, variants) is the spec |

### Tasks

1. **`FurnitureItemType` in the plan:** furniture persists in the existing save file — extend `FloorMapV2` additively (R4): per-floor `sceneObjects: [{objectId, variantIndex, position, rotationY, metadata{}}]` (adapting the existing `SceneObject` struct + `InteriorLevel` pattern; single unified file like the Suite's lesson *(DD §7)*).
2. **Placement controller (the missing piece):** ghost preview follows floor raycast (existing room colliders) → tint valid/invalid → tap commits (undo step). Anchor types by category: `Floor | Wall | Ceiling | Surface`.
3. **Snapping:** wire `Join.cs` — wall-flush (sofas/wardrobes rotate to wall normal), room containment, module-to-module surface snap (lamp on table), grid toggle. Each exposed as a toggleable rule (P4's snap-rule pattern).
4. **Manipulation:** select (outline, `SelectionService`), long-press drag, rotate (45°/15° steps + free ring), elevation for wall/surface items; **no free scaling** — catalog furniture has real dimensions (variants express size options).
5. **Collision/overlap:** footprint BoxCollider from bounds (the Suite's one-collider lesson 📄) → AABB/OBB overlap = red ghost, refuse commit.
6. **Furnish mode:** new `States.Furnish` (P3 state machine) with its own palette UI — category tabs, thumbnails (generator reused), search-by-name v1.
7. **Bundled starter catalog:** 20–30 local prefabs/GLBs to exercise everything before P7 goes remote.

**Deliverables.** Furnish a floor plan on device; save/reload intact (old saves still load, R4); undo covers placement/move/delete.
**Dependencies.** P2–P5 (selection, undo, states, GLB fitting). **Risks.** `Join.cs` was built for the Interior module we never saw run — budget a week of taming; ghost-placement feel on touch — copy the Suite's long-press idiom.
**Never postpone:** objectId references, footprint collider, undo coverage. **Postponable:** module-to-module joints beyond surface snap, measurement tape, search beyond name-contains.

---

## Phase 7 — Integrate the IKEA Runtime Asset Pipeline (4–6 weeks)

**Objective.** Furniture (and P5 openings) served from a live remote catalog: runtime GLB streaming, caching, metadata, categories, search — the commercial heart.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | P6 furniture system with local catalog; glTFast validated on device (spike, R7); ros-pipeline IKEA catalog data (SKUs/categories) from prior work; Exoa's module JSON already carries `sku`/`price` — it was *designed* for shopping *(DD §6.1)* |
| How it works | `ICatalog`-shaped manifest from P5/P6; local content |
| Strengths | Clean seam: only the catalog source and model provider change; placement/snapping/persistence untouched |
| Weaknesses | No streaming/cache infra yet; model quality variance from real catalogs; licensing question open |
| Suite 2026 difference | Nothing — the Suite has no runtime import at all *(DD §6.2)*; its "Online Modules Library" is roadmap-ware 📄. **We exceed the Suite here.** |
| **Verdict** | **Extend** (swap catalog source + add streaming layer behind the P6 interfaces) |
| Why | P6 was designed for exactly this swap |

### Tasks

1. **Catalog service:** remote JSON manifest (CDN/API): `objectId, name, category, dims, SKU, price, variants[], glbUri, thumbUri, normalization{pivot, facing, fitMode}`; delta-updatable; versioned. Map from ros-pipeline data.
2. **Streaming + cache:** download manager (capped concurrency, retry/resume), disk LRU keyed by content hash, memory budget with unload; **placeholder proxy** (bounds box + thumbnail) so placement never waits on megabytes; background swap-in.
3. **Import normalization at runtime:** bounds → footprint collider; apply manifest normalization; polycount/texture budget check with reject-and-report.
4. **Offline behavior:** cached items usable offline; missing-item fallback proxy on load — **never lose user data over a 404**.
5. **Search & browse v2:** server-side category/text search when API exists; client filter fallback.
6. **Openings join the pipeline:** P5's door/window catalog moves onto the same manifest/streaming path (one pipeline, two consumers).
7. **Licensing checkpoint (non-code):** IKEA model sourcing/licensing resolved with product owner **before** public distribution — flagged since the due diligence; unchanged.

**Deliverables.** Cold-start: browse remote catalog → place item < 3 s on LTE; force-quit mid-download → reload intact; 100-item scene memory-stable.
**Dependencies.** P6. **Risks (highest of program).** Content licensing (product/legal, raise now); mobile memory (LRU + placeholders + CI-adjacent budget test); GLB variance (normalization + server QA later, P10).
**Never postpone:** placeholders, cache eviction, missing-item fallback. **Postponable:** semantic search, price/cart UX (P10), server-side decimation.

---

## Phase 8 — XR Improvements (4–6 weeks)

**Objective.** Walk through and edit the furnished plan on Quest; keep phone AR (OpenRoomPlan capture rig) aligned with the same content.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | Editor is screen-space desktop/touch; **but** the project already runs ARFoundation + XR Origin work (OpenRoomPlan capture: TrackedPoseDriver bindings, XR Origin offsets — recent commits); generated geometry has full colliders (teleport-ready, per prior investigation); `PlayMode` app state exists for loading a plan without editor UI |
| How it works | `FloorMapReader` in PlayMode scene loads any saved plan into pure 3D — this is the XR entry point already built |
| Strengths | Deterministic load-from-JSON means an XR scene needs no editor code at all; colliders/navmesh-ready floors |
| Weaknesses | All editing UI is screen-space canvas (unusable in XR); no XR interaction bindings for editor verbs; static-event singletons mean one building at a time (acceptable in XR anyway) |
| Suite 2026 difference | **No XR support at all, none planned** 📄 *(DD §9)* — we exceed the Suite |
| **Verdict** | **Extend** (new XR scene + interaction layer consuming existing load/edit paths); **no editor refactor for XR's sake** — XR calls the same events/undo service P2 built |
| Why | The PlayMode pattern is the seam Exoa left us; world-space UI is additive |

### Tasks

1. **Quest walkthrough scene (M-XR1):** OpenXR + XRI; load saved plan via `FloorMapReader` (PlayMode path); teleport on floor colliders + smooth-move; human-scale ↔ dollhouse toggle (dollhouse = plan view's XR analogue).
2. **XR furniture interaction:** ray + direct grab → routes through the same placement/undo services (P2/P6) — XR edits are undoable back on the phone.
3. **XR opening interaction (stretch):** grab P5's handles world-scale; dimension chips already world-space.
4. **Spatial UI:** wrist/palette menu (catalog thumbnails), world-space properties panel — reuse existing UI code layout after re-canvasing; **no screen-space canvas in XR**.
5. **Quest perf pass:** URP quality tier, probe cap (`ReflectionProbe` per room exists — refresh once on load, never per frame *(DD §3.2)*), 72 fps floor.
6. **Phone-AR view (leverages OpenRoomPlan rig):** place the designed room at world scale via AR anchor — the scan → design → view-in-place loop with P9.
7. **Vision Pro:** scoping memo only *(DD §9)*.

**Deliverables.** Design on phone → walk it on Quest → move a sofa by hand → see the change (and undo it) back on the phone. Same save file.
**Dependencies.** P5–P7 content; P2 undo/services. **Risks.** XR input feel (timebox; controllers before hands); URP tier divergence — one pipeline, quality tiers only.
**Never postpone:** route-through-services rule, world-space UI. **Postponable:** hand-tracking polish, co-presence, Vision Pro.

---

## Phase 9 — AI Integration (4–8 weeks platform + first features; ongoing)

**Objective.** AI as a *client* of the evolved editor: prompt→plan, auto-furnish, and scan→plan (OpenRoomPlan) — all emitting the same data and events users do.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | `FloorMapV2` — compact, versioned, declarative JSON; deterministic JSON→3D loader; P2 undo (AI edits revertible); P4 validation; OpenRoomPlan **actively producing room reconstructions** (TSDF, semantic accumulation, eval tooling — recent commits); SilverTau RoomPlanKit; parked engine's validator + schema-generation code (R7) |
| How it works | "Generate JSON → editor renders it, fully editable" — the property identified in the very first investigation as Exoa's hidden superpower |
| Strengths | The generation target (FloorMapV2) has existed and been stable for years; scan lane is further along than the vendor's ("Planned" 📄 *(DD §10)*) |
| Weaknesses | No programmatic mutation API (everything flows through UI controllers); no server-side validation; normalized-coordinate quirks (grid-relative positions) need documenting for the LLM |
| Suite 2026 difference | HeadlessBuildingAPI 📄 is their answer; AI itself is aspirational meta-text only *(DD §10)* |
| **Verdict** | **Extend**: build `FloorPlanAPI` — our HeadlessBuildingAPI equivalent as a *façade over existing code* (constructs `FloorMapItem` records, calls `FloorMapReader`/rebuild events); **no engine change** |
| Why | The Suite proved the façade shape; our loader already does the heavy lifting |

### Tasks

1. **`FloorPlanAPI` (bridge):** `CreateRoom(points) / CreateRectRoom(w,l,pos) / AddOpening(type,pos,w,h,ypos,catalogId) / PlaceFurniture(objectId,pos,rotY) / SplitRoom / Load(json) / ToJson()` — thin wrappers over existing data + events; every call = undoable command. This simultaneously becomes the integration-test API for all earlier phases.
2. **Schema contract:** generate JSON Schema for (extended) `FloorMapV2` from the C# types; publish coordinate-system notes; few-shot library of good plans.
3. **Server AI gateway:** app never holds LLM keys; endpoints `generatePlan(prompt)`, `furnish(plan, roomId, constraints)`, `critique(plan)`; **validator runs server-side** (port the parked validator — pure C#, R7); repair loop (validate → return violations → retry ≤2).
4. **Feature 1 — Prompt-to-plan:** LLM structured output against the schema → server validation → device loads via `FloorPlanAPI.Load` → **proposal UX**: rendered as a preview the user accepts (one undo step) or rejects — never silent mutation.
5. **Feature 2 — Auto-furnish:** input room polygon + openings (door-swing keep-clear) + catalog slice (dims/category/price); output placements; deterministic repair pass = P6 snapping/containment re-applied.
6. **Feature 3 — Scan-to-plan (the differentiator):** OpenRoomPlan/RoomPlanKit output (walls/doors/windows) → mechanical mapper → `FloorMapItem` records → editable plan. No ML needed for the mapper; it's a data transform — schedule early, it may ship *before* the LLM lanes.
7. **Conversational editing (stretch):** expose `FloorPlanAPI` methods as LLM tool calls ("make the bedroom 1 m wider") — undo makes experimentation safe.

**Deliverables.** Scan a real room → editable plan → auto-furnished with IKEA items → accepted into the user's project. Prompt-to-plan demo.
**Dependencies.** P4 (validation), P6/P7 (catalog), P2 (undo/proposal); OpenRoomPlan track. **Risks.** LLM spatial quality (schema-constrained output + repair loop + few-shots); normalized-coordinate confusion (mitigate: API accepts meters, converts internally — hide grid coords from the model entirely).
**Never postpone:** server-side validation, proposal UX, meters-based API. **Postponable:** layout optimization/scoring, style transfer, image(sketch)-to-plan CV lane.

---

## Phase 10 — Commercial-Grade Features (6+ weeks, then ongoing)

**Objective.** Single-device tool → product: accounts, cloud saves, sharing, catalog ops, telemetry, recovery.

### Subsystem analysis

| Question | Answer |
|---|---|
| What exists | `SaveSystem` with an **ONLINE mode stub already in the enum** *(DD §7)*; per-plan thumbnails; P1 backups/autosave; P7 remote catalog; command/undo stream (P2) as a telemetry source |
| Suite 2026 difference | Self-hosted Laravel web portal (auth, cloud storage, public templates) 📄 — validates the shape; single-user starter quality *(DD §13)* |
| **Verdict** | **Extend** `SaveSystem` (implement ONLINE mode against our backend) + **Build** backend (managed services; the Suite's portal is a shape reference, not code we run) |
| Why | The seam was left for us; documents are small JSON — sync is cheap |

### Tasks

1. **Accounts + cloud saves:** auth provider per company stack; `SaveSystem.Mode.ONLINE` implemented: upload/download plan JSON + thumbnail; offline-first (local truth, queued sync); conflict v1 = last-writer-wins + "keep both copies" (duplication beats merge UI).
2. **Sharing & templates:** read-only share link (server renders thumbnail/viewer later); template gallery = curated plan JSONs (the Suite's "Public Templates" idea).
3. **Catalog operations:** manifest delta updates, staged rollout, server QA (budgets, normalization — P7's checks move server-side), takedown handling.
4. **Versioning & recovery:** server keeps last N versions per plan; schema-version discipline (R4) now mandatory for every change; crash-recovery flow (P1 autosave) polished.
5. **Analytics:** event telemetry off the action/undo stream (privacy-filtered): funnel rooms→doors→furniture→save→share; crash reporting. **Start lightweight logging in P2 — you'll want the data by P6.**
6. **Release engineering:** per-platform pipelines (Android/iOS/Quest), feature flags, staged rollout.

**Deliverables.** Closed beta: sign in, design across devices, share a link, live catalog, dashboards.
**Dependencies.** Everything. **Risks.** Scope explosion — the list above is the v1 line; backend beyond solo sweet spot — managed services only.
**Postponed by design:** realtime co-editing, in-app purchase/cart, photoreal cloud rendering, web viewer.

---

## Part III — Program Notes

### Cross-cutting decisions (recorded once)

- **Input System migration:** stay on the legacy Input Manager behind the P2 action facade; migrate only if a platform forces it (Quest OpenXR works with both). Re-evaluate at P8.
- **Static events & singletons:** accepted (banner decision). Contained, not celebrated: new services are instanced; new code subscribes/unsubscribes symmetrically; the known domain-reload duplication issue *(DD §8)* is on the P1 backlog as a hygiene fix (clear delegates on scene load).
- **Materials/lighting:** existing painting + per-room reflection probes suffice through P7; a lighting/quality pass (probe refresh discipline, URP tiers, HDRI through windows) is folded into P8's perf work rather than a standalone phase — the superseded roadmap's Phase 6 shrinks to that.
- **Parked engine disposition (R7), explicit:** *lifted into service* — validator (P4/P9), polygon math (P3 splitting), snap-rule pattern (P4), golden harness (P1), glTFast spike (P7), schema generator (P9). *Stays parked* — its renderer, commands, document schema as a runtime. *Deleted* — nothing.
- **Definition of done, every phase:** goldens green · editor ships (R3) · old saves load (R4) · `// IBMROS:` diffs updated in the patch ledger (R5) · this document's phase section updated with actuals.

### Sequencing rationale in one paragraph

Stability first because everything after edits guarded code (P1). Undo + touch + cancel next because every subsequent feature multiplies interaction surface, and retrofitting undo hooks into ten features is dearer than five (P2). Structural parity before manipulation polish because splitting/single-walls change what "selection" and "actions" must handle (P3→P4). Doors before furniture because the catalog pipeline (schema, normalization, palette UX) debuts on the system that's already half-built and highest-value (P5), then furniture reuses all of it (P6) and goes remote (P7). XR before AI only because it's pure consumption of finished content while AI wants the validation/catalog stack complete (P8→P9) — they can swap if business needs flip. Commercial last because every one of its systems wraps interfaces the earlier phases stabilize (P10).

### The next 30 days

1. **P1 in full** — goldens, save safety, autosave, patch ledger. (Highest leverage per hour in the whole program.)
2. **P2 started** — undo service first (it's ~2 focused days on top of the existing serializer and changes how safely everything else can be built), then Esc/cancel + touch deletion + the 2D/3D button.
3. **In parallel (non-code):** IKEA licensing question to product; decide whether to buy Suite 2026 ($198, loyalty discount) purely as a pattern reference for P3/P6 UX — optional, never a dependency.

---

---

## Part IV — Architecture Ownership Track (added 2026-07-18, owner decision)

**Philosophy upgrade (binding):** the Exoa codebase is no longer a third-party plugin we work around — **it is our foundation and we own it.** Architectural weaknesses discovered during feature work do not become permanent workarounds; they become phases on this track. R1–R7 still apply (analysis first, tagged edits, ledger, goldens, ship after every phase), but Refactor and Replace are now legitimate first-class tools whenever they produce the stronger long-term architecture. Feature phases (Part II) and architecture phases (this track) interleave; each architecture phase is scheduled when the debt it removes starts taxing feature work.

**The debt register** (from source analysis; each item → a phase below): silent mutations & inconsistent event propagation (fixed in A1) · document truth living inside UI objects (`SerializeScene` reads `UIFloorsMenu`/`UIFloorMapMenu` item data — the deepest debt) · no single mutation gateway (edits scattered across controllers/UI) · static event hub + mutable singletons + per-call `FindObjectOfType` · rebuild-the-world granularity · `ControlPointsController` monolith (input+snapping+viz in 731 LOC) · desktop-idiom input hardwired · **`FLOORMAP_MODULE`/`BUILDING_MODULE` defines exist only for Android** — iPhone/Standalone builds silently compile the whole editor out (fix when editor is closed: add to iPhone + Standalone define lists in ProjectSettings).

### A1 — Observable Document ✅ (2026-07-18)

Every document mutation announces itself on **`IBMROS.Core.DocumentEvents`** (`Assets/IBMROSBridge/Core/DocumentEvents.cs`), raised at the mutation *source* with a kind + semantic label — not inferred from rebuild plumbing (which also fires on loads/undo restores). Tagged edits (ledgered): SpaceController (geometry/draw/settings — covers Room + Outside), OpeningController (move/place/edit), UIBaseItem (**rename — was a completely silent mutation, the root cause of undo's polling**; duplicate; delete), FloorMapSerializer (floor add/duplicate/remove), OutsideController (missing `OnRequestRebuildBuilding` parity fixed). Consumers rewired: UndoRedoService (DocumentEvents = primary signal with labels; **release builds no longer poll at all**; editor/dev builds run a 5 s **mutation-leak detector** that logs an error naming any A1-invariant violation while still capturing the change), AutosaveService (dirty on any document event — renames now autosave). `NotifyMutation` routes through the same pipe so all consumers hear external features. UIBuildingSettings changes remain observable via their synchronous rebuild events (labels arrive with A3). Verified: bridge compiles clean device+editor configs; **authoritative Unity batch compile of all A1/A2 changes: 0 errors (2026-07-18)**.

### A2 — Document Model Extraction (in progress; ~2–3 weeks, before/with P3)

The deepest debt: **the document's truth lives in UI objects** — serialization walks menu items. Extract a `FloorPlanDocument` (owns `FloorMapV2` state; UI becomes view/controller over it): serializer reads the model; UI mutates the model; the model raises DocumentEvents itself (A1 raises then move into it). Unlocks headless operation (P9's `FloorPlanAPI` becomes trivial), server-side validation, and honest unit tests. Incremental: introduce model alongside, migrate reads first, writes second, goldens green at every step.

**Step 1 ✅ (2026-07-18):** `IBMROS.Core.FloorPlanDocument` extracted — owns the `FloorMapV2` state and all document/floor-level operations (load, serialize, empty-project, add/duplicate/remove/lookup floor); `FloorMapSerializer` delegates (exposes `Document` — the A3 gateway seam) and keeps scene/UI orchestration only. Model raises its own `DocumentEvents.Floor` events (A1 raises relocated). Struct copy semantics reproduced exactly, **pinned by 20 dotnet behavioral assertions running against the real compiled `Assembly-CSharp.dll` + `Exoa.Json.dll`** (incl. the v1→v2 ladder, unicode round-trip, and `DuplicateFloor`'s shared spaces-list reference). Vendor quirk discovered and pinned, not fixed: `IsSceneEmpty` inspects `floors[0]` only — a blank first floor makes a populated document count "empty", which can skip `UISaving` save prompts; fix deliberately in step 2.
**Step 2a ✅** — per-item identity (see A-track A2 note). **Step 2b ✅ (2026-07-18):** the document became the **query model** — `FloorPlanDocument` gained `GetAllItemIds`/`GetItemById`, and read ops (`FloorPlanEditor.GetItemIds/GetItemJson`) query it via `SyncedDocument()` (SerializeScene refreshes model-from-UI, then query — zero drift). Pinned `IsEmpty` vendor bug **fixed** (checks all floors now, dotnet-tested). **Step 2c (remaining, needs interactive play-test):** flip `SerializeScene` to read the document instead of walking UI so the widget-as-store is fully retired — deferred because the slider-drag and floor-switch sync paths require a human to exercise; the query model + identity make it mostly mechanical when it comes.
**Original step-2 framing (kept for reference):** invert spaces ownership — UI widgets/CPC changes write through to document items (`BroadcastChange` is the chokepoint), `SerializeScene` stops reading UI. **Gate cleared 2026-07-18: goldens captured and verified 4/4 headlessly** (fixture format learned the hard way: plan coordinates live in Vector3 **x,y** — z unused — and opening `directions` are wall *tangents*; wrongly-axed fixtures collapse via `PointsAreInLine` into empty meshes).

**Live-testing fixes (2026-07-18, owner feedback):** Undo/Redo HUD moved to right edge vertically centred (top-centre collided with Exoa menus) · **color-shifting on undo/load fixed at source** — `ControlPointsController.Awake` derived room colors from `FindObjectsOfType<UIBaseItem>().Length`, which counts objects pending deferred destruction; now uses container sibling index (deterministic per document; persisting a color index in the document remains an A2 schema item) · platform defines fixed: `FLOORMAP_MODULE;BUILDING_MODULE;UNITY_PIPELINE_URP` added to iPhone + Standalone (were Android-only; iOS builds silently excluded the editor).

**Corrections after 2nd live-testing round (2026-07-18):**
- **2D/3D pill removed.** It reinvented an existing control: the toolbar camera button is an `ActionButton{cameraAction, camAction=SwitchPerspective}` — the "PERSPECTIVE" toggle already in the scene. (Earlier "ChangePlanMode is dead code" note was incomplete: it only checked the `HDInputs` Space-key path, not `ActionButton`.) Lesson reinforced: search the whole system, including data-driven components, before adding UI.
- **RestoreVeil removed — it made undo worse, not better.** Freezing a screenshot over the destroy-rebuild gap decoupled the displayed frame from a still-moving (inertial) camera, so on undo the view "shifted up then settled" when the veil dropped. Net worse than the small flicker it hid.
- **Camera jolt on undo fixed at the root:** `AppController.SuppressNextFocusOnLoad` flag — undo restore reuses the file-load path, which schedules a 0.5 s camera refocus; the flag makes `OnFileLoaded` skip it (robust replacement for the racy `CancelInvoke("DelayedFocus")`). Undo no longer moves the viewpoint.
- **Residual:** undo still full-clear+deserializes, so a geometry-changing undo has a brief rebuild flicker. The real fix is **A5 diff-based/scoped restore** keyed on A2 item identity (only rebuild items that actually changed; a rename-undo rebuilds nothing) — promoted to the next A-track priority.

### A3 — Mutation Gateway (v1 ✅ 2026-07-18; full phase with P3's per-item actions)

All edit operations become methods on one gateway (`FloorPlanEditor`): `CreateRoom / MovePoint / SetItemSettings / Rename / Delete / Split…` — UI, AI, and XR all call it; the gateway mutates the A2 model, raises labelled DocumentEvents, and wraps each call in an undo `BeginAction` scope (labels become fully semantic). UIBuildingSettings and remaining scattered mutations fold in here. This *is* the HeadlessBuildingAPI equivalent P9 needs — built once, used by everything.

**v1 shipped (`IBMROSBridge/Core/FloorPlanEditor.cs`):** meters-based static API (`CreateRoom(points) / CreateRectRoom(w,l,center) / CreateOutside / AddOpening(type,pos,wallTangent,w,h,ypos) / RenameItem(id) / DeleteItem(id) / ToJson / LoadJson`) — grid coords never leak to callers (P9 rule); every op is one labelled undo action; creation flows through the proven deserialize path. Addressing uses A2's new item identity. **Headless behavioral test shipped** (`ApiSmokeTest`, `IBMROS_GOLDEN_MODE=apitest`): builds a room + door programmatically in the real scene, asserts floor/walls/door-hole jamb geometry, rename→document persistence, undo/redo round-trip, delete. Remaining for full A3: MovePoint/SetItemSettings ops, UIBuildingSettings fold-in, UI call-sites migrate to the gateway (with A2 step 2b).

### A4 — Service Lifecycle & Statics Hygiene ✅ (2026-07-18)

Static delegates got the `SubsystemRegistration` reset pattern (`GameEditorEvents`, `CameraEvents`, `AppController` — fixes the documented duplicate-handler leak under disabled domain reload); rebuild-hot-path `FindObjectOfType<Grid>()` calls cached (`FloorController`/`ControlPointsController.GetGrid`). New services already followed symmetric subscribe/unsubscribe. Deferred to opportunistic follow-up: a formal scene-scoped service registry (worth it when a third consumer appears).

**A2 step 2a ✅ (same date):** `FloorMapItem.uniqueId` — additive schema (R4: old saves get ids lazily on load) + `UIBaseItem.ItemUniqueId` (adopt-or-generate) + duplicates never share identity. The addressing foundation for A2 step 2b (write-through), A3 (done — gateway uses it) and A5 (dirty scopes).

### A5 — Scoped Rebuild Pipeline (merges P4's perf tasks) — v1 ✅ (2026-07-18)

DocumentEvents changes carry affected-item scope → dirty-room rebuilds, opening GameObject pooling, `UndoRedoService.Restore` learns scoped apply (documented seam). P4's task list executes here with better foundations.

**v1 (scalars-only scoped restore) — superseded same day by v2 below.**

#### Undo-lifecycle bug + A5 v2 Reconcile Restore (2026-07-18, from live-testing report)

**Root cause of "undo only works once / redo dies after seconds" (owner report):** in this engine, `serialize(deserialize(S)) ≠ S` — verified in source: on load, `CreateControlPointBasedOnNormalizedPosition` never applies the stored `directions` (they stay zero until a later ReSnap recomputes them), and floats drift through the world↔normalized round trip. So after every restore, the scene re-serialized differently than the snapshot; seconds later the capture pipeline saw that as a user edit, **pushed a phantom step and truncated the redo tail** (redo death), and every next Undo just stepped onto phantom re-serializations of the same state (undo "stuck"). Openings made it near-certain — matching "breaks when I place doors/windows". **Fixes:** (1) `SnapshotHistory.ReplaceCurrent` + a post-restore **baseline resync** — once rebuilds settle, the history adopts the scene's *own* serialization as the current entry, so equality checks compare against reality forever after; (2) `Undo()` only commits an in-flight edit when a change signal actually arrived (the unconditional serialize-compare pushed phantoms on rapid undos). Regression-tested headlessly: redo must survive 6.5 idle seconds after an undo with an unchanged step count.

**Restore-strategy research (per owner directive — how the pros do structural undo):** Unreal Engine uses a *transaction buffer* — objects serialize themselves into the transaction and undo re-deserializes **only the objects in the transaction, in place**; Figma-class web editors keep an immutable document and **diff + reconcile** the scene like a virtual DOM keyed by node identity; Blender uses global-state undo but **reuses unchanged datablocks** on restore. The common principle: *identity-keyed diffing so unchanged things are never touched.* Our A2 item identity exists exactly for this.

**v2 shipped — `RestoreReconciler` (identity-keyed diff-and-reconcile):** the restore target is diffed against the live document by item id; then — scalar/name deltas write onto the live UI item (rename = zero rebuild); **geometry deltas move the item's existing control points in place** (the ReSnap mutation recipe) and fire that item's own change event (meshes regenerate synchronously — no GameObject churn); **removed items are deleted individually; added items are created individually** (the per-item load path). Unchanged items are never touched → structural undos (place/delete door, move wall points, create/remove room) no longer tear the scene down. Full clear+deserialize remains the fallback for: multi-floor documents, floor changes, building-settings changes, identity-less items, type morphs, live/document divergence, or any exception. Also shipped: `FloorPlanEditor.MoveItemPoints(id, pointsMeters)` (A3 op; same in-place recipe). Headless proof: apitest asserts the room GameObject **survives** a door-delete-undo and a geometry-undo, plus a full undo-walk to empty scene and redo-walk back.

**Follow-up fix — opening 90° rotation + redo-death, true root cause (2026-07-18, 2nd report):** the earlier `serialize(deserialize(S)) ≠ S` diagnosis was right about the *symptom* but the mechanism was deeper: `CreateControlPointBasedOnNormalizedPosition` **never set `ControlPoint.dir`**, so every recreated opening began at `dir = zero`, which `OpeningController.Rebuild` renders at the bare `Euler(0,90,0)` — the owner's "all the windows rotate 90°". The re-snap that later corrected `dir` also mutated the serialized state after a restore → phantom step → redo death. Fixes: (1) `CreateNewUIItem` applies the persisted `directions` on control-point creation (openings render right immediately; serialize is stable); (2) the reconciler treats `directions` as **derived** — diffs geometry by position only and fires `OnRequestRepositionOpenings` after structural changes so openings re-derive direction from the current walls (the full-load cascade); (3) the baseline resync now **polls until serialize stabilizes** under a `resyncing` guard that blocks the capture pipeline, so no phantom can be pushed no matter how long re-snap takes. Regression-tested headlessly: opening never stuck at 90° base (fresh + restored) and serialize stable across the 5 s leak-sweep after an opening restore.

**Deterministic item color ✅ (2026-07-18):** color is now `FNV-1a(uniqueId) % palette` (`IBMROS.Core.ItemColor`), applied in `CreateNewUIItem` before the first build — a given item is always the same color across rebuild/undo/redo/reconcile-recreate/reload. Root fix for the color-shift class (superseding the sibling-index band-aid) and it closes a latent bug where the A5 reconciler's recreate could re-roll a color. No schema field needed (derived from the persisted id); an explicit per-item color override belongs to P5/P6 paint.

**A5 v3 (remaining):** edit-time dirty-room rebuilds + opening GameObject pooling (P4's perf items); reconcile across floors (multi-floor documents currently full-restore). 

### A7 — Scoped Invalidation & Rebuild Pipeline (in progress; owner-mandated deep ownership)

**Weakness (whole subsystem).** EXOA invalidation is pure broadcast: an opening change fires `OnRequestRebuildAllRooms` (every `SpaceController` regenerates its meshes); a room change fires `OnRequestRebuildBuilding` + `OnRequestRepositionOpenings` (every opening re-snaps and re-renders); building-settings fire all three. No entity knows its dependents, so any edit invalidates the world — user-visible as "all windows flicker when I undo one," and O(all rooms × all openings) work per edit.

**Research → design.** CAD/BIM parametric engines, Unreal's transaction-scoped dirtying, and reactive/virtual-DOM reconcilers all replace broadcast invalidation with a **dependency graph**: an edit marks only its dependents dirty. The dependency edges already exist implicitly here — an opening binds to a wall within ~0.2 m (EXOA's own snap test), so *opening → the room(s) whose polygon it is near, + the building shell*; *room geometry → that room + its openings (re-snap) + shell + roof*; *settings → all*. Design principle adopted: **correctness by conservative over-approximation** (never miss a dependent — a proximity margin guarantees it), **performance by tight dependency queries**, both proven by a `scoped == full` regression (scoped geometry must byte-match a full rebuild) plus mesh-instance preservation (unaffected entities must not regenerate).

**Stage 1 ✅ (2026-07-18) — scoped opening invalidation.** `IBMROS.Core.ScopedRebuild.ForOpeningPositions` rebuilds only the rooms whose wall an opening touches (point-to-segment ≤ 0.35 m margin) plus the single building; `SpaceController.RequestRebuild` is the scoped entry. Rewired: `OpeningController.Rebuild` (opening edit/add), `UIBaseItem.Delete` (opening delete), and — transitively — the A5 reconciler's opening add/remove. An opening change no longer rebuilds other rooms or any other opening. Proven headless: (a) a door on room A leaves distant room B's floor **mesh instance unchanged**; (b) **scoped geometry == full-rebuild geometry** (byte-identical mesh stats). This root-causes the reported "all windows flicker on undo."

**Stage 2 ✅ (2026-07-18) — scoped room-geometry reposition + scoped room delete.** Finding while implementing: `RoomController.Rebuild` is already per-instance (a room edit only regenerates that room's mesh — no other room rebuilds), so the remaining broad invalidation on the room side was just two things: the **global opening reposition** (every opening re-snapped on any room path change) and **room delete** (broadcast all-rooms). Both scoped: the three `OnRequestRepositionOpenings` broadcast sites now call `ScopedRebuild.RepositionOpeningsNear(room)` (re-snap only openings on that room's walls, via `OpeningController.RequestReposition`); room/outside delete calls `ScopedRebuild.ForRoomRemoved(footprint)` (re-snap openings on the removed room + rebuild only adjacent rooms + building). Proven headless: creating/deleting a distant room does **not** re-snap or rebuild room A; scoped geometry == full-rebuild geometry for both.

**Stage 2b ✅ (2026-07-18) — explicit opening→host-room binding.** `IBMROS.Core.OpeningHostRegistry` records the authoritative opening→room binding at the moment a room actually claims an opening during its rebuild (EXOA's own 0.2 m test), keyed by stable A2 item ids. `ScopedRebuild.RepositionOpeningsNear` now repositions an opening whose *remembered host* is the changed room — exactly, so it follows its wall through any move — with the proximity margin kept only as a safety net for newly-added openings. `FloorPlanEditor.MoveItemPoints` on a room re-snaps its hosted openings so they follow the walls. This removes the pure-margin heuristic for the tracked case (the deterministic-dependency endpoint the mandate wanted). Persisting the binding to the save file (so it survives reload without a first rebuild) is a small future add; the runtime registry is authoritative in-session. **Two bugs caught by the first clean A7 verification and fixed:** (1) a real room-delete **ordering bug** — the shell rebuilt before the deleted room's end-of-frame `Destroy`, so it kept the removed contour; fixed with `RebuildScheduler` deferring the shell rebuild one frame (scoped==full now passes). (2) a test-only issue — the selection raycast ran from a top-of-world ray (hits the building roof) deep in a mutated sequence; moved to a clean scene with an interior ray. *Finding logged:* plan/top-down (2D) selection will hit the roof — revisit when 2D-plan tap-select lands.

**Stage 3** — settings invalidation stays intentionally global (it truly affects all) but moves onto the bus API for uniformity; unify the per-controller 0.1 s throttles into one scheduler. **Stage 4 (scale)** — replace the on-demand `FindObjectsByType` dependency scan with a registry + spatial index (grid/BVH) when room counts grow; today's linear scan is fine at room-planner scale.

### A6 — Interaction Layer Decomposition (with P2's input work)

`ControlPointsController` split along seams (input handling / snap rules / path visualization); action-based input facade (P2 task 2) lands as part of it; touch-native bindings become the primary map. Do not start before A2 — the monolith's tentacles into UI data are exactly what A2 removes.

---

*This is the living master roadmap. Each completed phase updates its section with actuals (dates, deviations, lessons). The previous from-scratch roadmap remains in the repo as an architecture reference; where this document says "lift from parked engine," that is where the lifted design is documented in depth.*
