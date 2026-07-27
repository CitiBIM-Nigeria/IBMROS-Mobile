# IBMROS Mobile — Room Designer
## Master Software Architecture & Implementation Roadmap

> ## ⚠️ STRATEGY REVISION — 2026-07-17 (supersedes "own the engine" below)
>
> **Owner decision (Samuel, explicit):** build **ON the Exoa FloorMapEditor** — work inside its scene, extending it with the features identified by the Suite-2026 research. Rationale: the editor UX that was slowing us down (2D↔3D camera perspective, mode escape, save UI, floors menu, materials) already exists there, polished; rebuilding it was re-inventing the wheel. The due diligence confirmed Exoa's opening system shares our architecture (parametric holes ≠ visuals), so the differentiating features graft on directly.
>
> **Disposition of work to date:** `Assets/IBMROS/` (engine, 64 pure tests, schema, command gateway, CI lanes) is **parked, not deleted** — it remains the reference implementation, the test bed for geometry math, and the designated future core if AI/XR needs ever force a migration. Do not build further standalone-editor UI on it.
>
> **Known Exoa gaps accepted knowingly:** no undo/redo, static events, deprecated upstream (we own and patch the source — wall-flip, fonts, URP materials already fixed by us).
>
> **New execution track (phases re-scoped onto FloorMapEditor):**
> 1. ✅ **Catalog opening visuals** (started 2026-07-17): `Assets/IBMROSBridge/CatalogOpenings/` — `CatalogOpeningManager` skins Exoa's procedural doors/windows with catalog 3D models, bounds-fitted into the same holes (self-calibrating: fits to the procedural mesh's own bounds; re-fits on resize; re-skins on rebuild). Bridge code lives outside `Assets/IBMROS/` because Exoa compiles into Assembly-CSharp, unreachable from asmdefs.
> 2. Per-opening model choice + catalog palette UI inside Exoa's UI (door types like the reference app).
> 3. IKEA furniture placement in the editor (runtime GLB via glTFast + Exoa's `Join.cs` snapping math).
> 4. Undo (snapshot pattern over Exoa's serializer — their JSON save is the snapshot primitive).
> 5. XR walkthrough of saved floor maps; then AI (prompt → FloorMapV2 JSON — their format is the generation target now).

**Version:** 1.0 · **Date:** 2026-07-17
**Author role:** Lead Architect blueprint — the document a senior Unity developer joining tomorrow reads first.
**Strategy (decided):** **Own the engine, reuse the math.** IBMROS builds its own room-design engine. The legacy EXOA plugin already in this repo is a *working reference implementation*, not a foundation. Home Designer Suite 2026 is *optionally* purchased as a second reference — never a runtime dependency.
**Calibration:** solo developer (AI-assisted), 6–12 month horizon. All estimates are solo engineer-weeks.
**Companion document:** `docs/EXOA_HomeDesigner_Technical_DueDiligence.md` (v2) — all claims there labeled ✅/📄 are treated as established facts here and cited as *(DD §n)*.

---

## Part I — Vision, Strategy, and Doctrine

### 1. Product vision (restated as engineering targets)

Users of IBMROS Mobile will: draw any floor plan in 2D → get an editable 3D room instantly → place doors/windows from a catalog that cut real openings in walls → furnish with runtime-downloaded IKEA models → walk through in XR. Later: AI generates, furnishes, optimizes, and reconstructs rooms from prompts, photos, and scans.

The reference application (screenshots reviewed) defines the interaction bar: doors *embedded in* walls with live drag/resize handles and dimension chips (4′8″ × 9′2″), instant catalog swap of door types, 2D↔3D toggle, walk mode. We reach parity with it at **Milestone M3** (end of Phase 4).

### 2. Why "own engine" — the decision, defended

The v2 due-diligence report recommended buying Suite 2026. You overruled toward ownership. As architect I agree, on these grounds — and I note where the trade hurts:

**For ownership:**
1. **The hard part is smaller than it looks.** The entire structural core of the legacy plugin — everything that makes rooms, walls, and openings — is ~2,000 LOC across `ProceduralRoom/Space/Opening/Building` + math utils *(DD §1)*. The magic is not EXOA's code; it is the *technique*: 2D polygon offsetting (Clipper) + 2D boolean hole subtraction + tessellation (LibTess) + parametric opening data *(DD §4.1)*. Both libraries are free, open source, and better in their current versions (Clipper2) than the forks EXOA bundles.
2. **We have a debugged reference in-repo.** Every geometry edge case EXOA solved (winding normalization, the −X wall-rotation flip we personally fixed, miter joins, opening clamping) is readable C# we own a license to. Porting a known-correct algorithm is a fraction of discovering it.
3. **Our three product layers — catalog, XR, AI — are outside the Suite's scope anyway** *(DD §15)*. Buying it buys the one layer we can now build ourselves in weeks.
4. **Vendor risk is real:** single developer, v1.x, deprecated-with-paid-migration track record, editor license-activation DLLs that break CI, per-seat Extension Asset terms, a bundled Newtonsoft fork that conflicts with our existing `com.unity.nuget.newtonsoft-json` *(DD §14)*.
5. **A pure-C# engine we own runs headless** — in edit-mode tests, in CI, on a server next to the AI — something neither EXOA generation can do (their generators are MonoBehaviours).

**The honest cost:** ownership delays the first end-to-end demo by roughly 6–9 weeks versus building on the Suite, and we re-own every future geometry bug. Mitigations: golden-file geometry tests from week one (Phase 2), and the two reference implementations to diff against. **Optional $198 spend:** buying the Suite purely as reference source is likely worth one debugging week alone; decide at Phase 2 start. If bought, its code never ships — reference reading only.

**What we explicitly reject:** extending the legacy plugin in place (deprecated upstream; static-event/singleton shell would fight every phase from XR onward *(DD §8.7)*), and 3D CSG engines (wrong tool — parametric 2D data is the proven design; it's what EXOA, the Suite, and by all evidence the reference app use).

### 3. Architectural doctrine (rules that outlive every phase)

These are the non-negotiables, each earned from a specific research finding:

- **D1 — The document is the product; meshes are a cache.** All state lives in one serializable `BuildingDocument`. Geometry, colliders, UI, XR views are derived, disposable, and rebuildable from it at any time. (Proven by EXOA's snapshot-undo and our AI plans; *DD §7, §11*.)
- **D2 — One mutation gateway.** Every change — 2D UI, 3D gizmo, XR hand, AI tool call, network sync — is a `Command` applied through a single `IDesignSession.Apply()`. Undo, validation, dirty-tracking, autosave, analytics, and multiplayer all hang off this one chokepoint. Retrofitting this later is the single most expensive mistake we could make.
- **D3 — Engine is pure C#; Unity is a view.** `IBMROS.RoomEngine` (geometry + document + validation) has **zero** `UnityEngine.Object` references (math structs only). MonoBehaviours live in `IBMROS.RoomView` and only *present*. This is the direct fix for EXOA's biggest architectural flaw *(DD §8.7)*.
- **D4 — No static events, no mutable singletons.** Instanced services, constructor-injected (VContainer). EXOA's own docs admit statics survive domain reload and force single-active-building *(DD §8)*.
- **D5 — Holes are data, never models.** An opening is `{type, width, height, positionOnWall, sillHeight}`. Visuals (procedural or downloaded GLB) are attached presentations. This is the fact that makes the whole catalog vision viable *(DD §4)*.
- **D6 — IDs everywhere, strings nowhere.** GUIDs for rooms/walls/openings/furniture instances; catalog `objectId` for content. EXOA's `prefabName`-string serialization is the anti-pattern *(DD §6)*.
- **D7 — Schema is versioned from commit one.** `"schemaVersion": 1` plus a migration ladder, copying EXOA's one genuinely excellent serialization pattern (v1→v2→v3 converters; *DD §7*).
- **D8 — Realtime lighting only.** Runtime-generated meshes cannot be lightmapped (Unity constraint, not a choice; *DD §3.2*). Budget accordingly from Phase 2, communicate it in every art conversation.

### 4. Build / Reuse / Replace matrix (summary; details in phases)

| Subsystem | Verdict | Source of truth |
|---|---|---|
| Geometry technique (offset/clip/tessellate) | **Reuse technique, rebuild code** on Clipper2 + LibTessDotNet | Phase 2 |
| Legacy `MathUtils`, wall/opening algorithms | **Port as reference** (we own the license; diff-test against it) | Phase 2 |
| Data model | **Build** — superset of the concepts in `UnifiedBuildingData v3` | Phase 1 |
| Command/undo/eventing/DI | **Build** (nothing worth reusing in either EXOA generation) | Phase 1 |
| 2D editor UX patterns (grid snap, path snap, throttle) | **Reuse patterns**, rebuild code | Phase 3 |
| `TouchCameraLite` orbit/pan/pinch camera | **Reuse as-is** (self-contained, already licensed, mobile-proven) | Phase 3 |
| Opening visuals | **Build** `IOpeningVisual` (procedural fallback + GLB catalog) | Phase 4 |
| Furniture placement/snapping | **Build**, using legacy `Join.cs` math as reference | Phase 5 |
| Thumbnail generator, outline/selection effect | **Reuse as-is** from legacy asset | Phase 5 |
| Runtime GLB import | **Adopt** glTFast (Unity-maintained) | Phase 4/5 |
| ProBuilder rooms | **Retire** for rooms entirely *(DD §5.1)* | now |
| Roofs, exterior shell, outside spaces | **Cut from v1** (see §5 below) | Phase 9+ |
| Scan-to-room | **Reuse in-repo assets** (OpenRoomPlan, SilverTau RoomPlanKit) | Phase 8 |

### 5. Scope cuts I am imposing (challenge-my-own-recommendations, part 1)

EXOA is a *house* designer; IBMROS is a *room/interior* designer. Copying their scope would be overengineering:

- **No roofs in v1.** Flat/hipped/gabled roof generation (370 LOC + straight-skeleton math) serves exterior views we don't sell. The ceiling is our lid. Revisit only if product adds exterior mode.
- **No exterior building shell in v1.** EXOA builds a unioned outer wall around all rooms *(DD §3.1)*. For interiors, per-room walls suffice; the data model keeps the door open (walls know inner/outer faces).
- **Single active building/session in v1.** EXOA's limitation, but for us a deliberate simplification — the doc-based engine makes multi-session trivial later; the UI for it is not worth building now.
- **Multi-floor: data model yes (day one), UI later** (Phase 3 tail). Retrofitting floors into a schema is painful; retrofitting a floor-picker UI is trivial.
- **No curved walls in v1.** Neither EXOA generation has them; the reference app doesn't show them; segment approximation exists as an escape hatch. Design the wall data as polylines so arcs can be added as a wall-segment subtype later.
- **Imperial/metric chips**: trivial, but *do* ship them in Phase 4 — they're half the perceived quality of the reference app.

### 6. Phase map, dependency spine, and milestones

```
P0 Foundations ─► P1 Core Architecture ─► P2 Room Engine ─► P3 Runtime Editing ─► P4 Doors & Windows
                                                                    │                     │
                                                                    ▼                     ▼
                                              P5 Furniture ◄────────┘         M3: reference-app parity
                                                   │
                     P6 Materials & Environment ◄──┤   (P6 overlaps P5)
                                                   ▼
                                              P7 XR  ─►  P8 AI  ─►  P9 Production
                                              (P8 schema work starts in P1; P8/P7 can swap order)
```

| Milestone | Definition of done | After phase | Cumulative estimate |
|---|---|---|---|
| **M1 — Room on device** | Draw polygon in 2D on phone → 3D room with walls/floor/ceiling | P2 | ~7–11 wks |
| **M2 — Editable apartment** | Multi-room editing, undo, save/load, materials-ready meshes | P3 | ~11–17 wks |
| **M3 — The Door Demo** (reference-app parity) | Catalog GLB door placed, hole cut, drag/resize/swap in 3D with dimension chips | P4 | ~14–21 wks |
| **M4 — Furnished room** | IKEA GLB downloaded at runtime, snapped, saved, reloaded | P5 | ~19–29 wks |
| **M5 — XR walkthrough** | Quest walkthrough + grab-to-move furniture | P7 | ~25–37 wks |
| **M6 — Prompt-to-room** | Text prompt → valid document → 3D room | P8 (MVP slice) | +2 wks after M2 possible; full P8 later |

A deliberate option: **M6-lite can be pulled forward** to any point after P2, because AI generation is just "produce a document" (D1). Doing a prompt-to-room hack week after M2 is cheap and hugely motivating; noted in Phase 8.

---

## Part II — The Phases

Template per phase: **Objective · Why now (sequencing rationale) · Systems to build · Deliverables · Dependencies · Risks · Architecture · Reuse vs custom · Complexity · Postponable / Never postpone.**

---

## Phase 0 — Foundations, Spikes, and Guardrails (1–2 weeks)

**Objective.** De-risk every bet this roadmap makes before writing production code.

**Why now.** Each later phase assumes a spike result. A failed spike here changes the plan cheaply; discovering the same failure in Phase 5 changes it expensively.

**Systems / activities.**
1. **Repo hygiene:** create `Assets/IBMROS/` root with asmdefs: `IBMROS.RoomEngine` (no Unity refs — enforce with an asmdef that only references System/Mathematics), `IBMROS.RoomView`, `IBMROS.App`, `IBMROS.Editor`, `IBMROS.Tests`. Quarantine `Assets/Exoa/**` behind a `LEGACY_REFERENCE` define or move to a `Reference~` folder (excluded from compile) so it can be read but never linked.
2. **Spike A — Clipper2 + LibTessDotNet in pure C#:** offset a concave polygon inward, subtract two rectangles, tessellate — assert vertex output in an NUnit edit-mode test. (LibTessDotNet is already in-repo inside ProceduralToolkit; take a clean copy.)
3. **Spike B — glTFast import on device:** download one IKEA GLB at runtime on Android/iOS, instantiate, measure bounds. Confirms the entire catalog thesis and surfaces Newtonsoft/dependency conflicts *now*.
4. **Spike C — golden-file test harness:** serialize generated mesh stats (vert/tri counts, bounds, checksums) for a fixture room; compare against the legacy EXOA output for the same points. This harness is Phase 2's safety net.
5. **CI:** GitHub Actions (or equivalent) running edit-mode tests on every push. The pure-C# engine makes this fast.
6. **Decision gate:** buy Suite 2026 as reference source? (Recommendation: yes if $198 is immaterial; skip if not — the legacy source covers ~85% of the reference value.)

**Deliverables.** Compiling asmdef skeleton; 3 green spikes; CI badge; written go/no-go notes.
**Dependencies.** None.
**Risks.** glTFast + existing project packages (ARFoundation, WebP, HotReload) version friction — that's *why* Spike B is now.
**Complexity.** Low.
**Never postpone:** the asmdef firewall (D3) and CI. **Postponable:** nothing — this phase *is* the un-postponable stuff.

### Phase 0 — Status log (2026-07-17): ~90% COMPLETE

**Done:**
- ✅ **P0.1 asmdef skeleton** — `Assets/IBMROS/` with `IBMROS.RoomEngine` (`noEngineReferences: true`, `autoReferenced: false`), `IBMROS.RoomView`, `IBMROS.App`, `IBMROS.Editor` (editor-only), `IBMROS.Tests.EditMode`, plus `IBMROS.ThirdParty.Clipper2` and `IBMROS.ThirdParty.LibTessDotNet` (both engine-ref-free).
- ✅ **Vendored libraries** (pinned, unmodified, provenance READMEs + licenses in-folder): Clipper2 **2.0.1** (tag `Clipper2_2.0.1`; note: 2.x now ships its own triangulator — vendored but unused; LibTess stays primary so golden comparisons against the legacy reference stay meaningful), LibTessDotNet (master, `LibTessDotNet` namespace — no collision with Exoa's `ProceduralToolkit.LibTessDotNet.Double`).
- ✅ **Spike A GREEN (twice)** — offset concave L-room inward, subtract door+window rects, tessellate; area conserved to 1e-3. Validated first headlessly (`dotnet`, **LangVersion 9** = Unity's level) then ported to NUnit: `Tests/EditMode/Spikes/GeometrySpikeTests.cs` (4 tests incl. golden fingerprint). API note: Clipper2 2.x uses `Clipper.MakePath(double[]) → PathD` (no `MakePathD`).
- ✅ **Spike B (editor lane) GREEN by construction** — glTFast **6.14.1 already installed and proven** (repo's `wardrobe.glb` imports through it; Draco 5.4.3 present; **no Newtonsoft/package conflicts** — NuGetForUnity holds only AWS SDKs). Runtime-API test added: `GltfRuntimeImportSpikeTests.cs` (GltfImport → instantiate → measure bounds). **On-device lane remains** as the phase-exit checklist item.
- ✅ **Spike C harness** — `GoldenAssert` (bootstrap-on-first-run; regenerate via `IBMROS_REGEN_GOLDENS=1`; Inconclusive on regen so it can't masquerade as a pass). First fixture: SpikeA wall fingerprint.
- ✅ **CI** — `.github/workflows/ci.yml`: job 1 `engine-syntax` (license-free; compiles engine+libs at C#9/netstandard2.1 with zero Unity refs — the D3 firewall is machine-enforced; **verified green locally**); job 2 `unity-tests` (game-ci, opt-in via `ENABLE_UNITY_CI` repo var once license secrets are set). Local guard: `dotnet build Tooling/EngineSyntaxCheck`.

**Deviations from plan (reasoned):**
- **Exoa physical quarantine deferred.** The FloorMapEditor scene is the active reference playground; the firewall is already structural (all IBMROS code lives in explicit-reference asmdefs; Exoa's un-asmdef'd code compiles into `Assembly-CSharp`, which asmdef assemblies cannot reference by construction; `autoReferenced:false` also blocks the reverse). Physical move to `Reference~` happens at M1 when the playground retires.
- **Golden fixtures start from our engine, not legacy output.** Direct EXOA comparison needs its MonoBehaviour generators in play mode; deferred to Phase 2 wall-builder bring-up as a one-off manual validation.

**Open items to close Phase 0:** run EditMode tests in Unity (first run bootstraps 1 golden, second run full green — expected 6 tests); Spike B on-device pass (Android); set `ENABLE_UNITY_CI` + license secrets on GitHub; **decision gate: buy Suite 2026 as reference source (owner call, $198, non-blocking)**.

---

## Phase 1 — Core Architecture: Document, Commands, Services (2–3 weeks)

**Objective.** The skeleton every other phase hangs off: the `BuildingDocument` schema, the command gateway, serialization with versioning, DI, and the event fabric.

**Why now / why before geometry.** Geometry generated from an ad-hoc data structure calcifies that structure. EXOA's own history proves the schema is the long-lived asset (their v1→v2→v3 migrations) and the app shell is the disposable part *(DD §7, §8.7)*. Undo, AI, cloud sync, and XR all consume the *document and commands*, not meshes — so document + commands come first.

**Systems to build.**

1. **`BuildingDocument` (schema v1).** Design as a superset of the *concepts* (not the wire format) of EXOA's `UnifiedBuildingData v3` *(DD §7)*:

```
BuildingDocument { schemaVersion, buildingId, name, units,
                   settings { wallHeight, wallThickness, doorHeightDefault },
                   floors: [ Floor { id, elevationIndex,
                       rooms:    [ Room { id, name, points: [Vec2...],   // CCW, meters
                                          heightOverride?, style: RoomStyle } ],
                       openings: [ Opening { id, type: Door|Window|Archway,
                                          width, height, sillHeight,
                                          anchor: { pos: Vec2, dir: Vec2 }, // world-plane, like EXOA worldPos/direction
                                          visual: { catalogId?, variantIndex, flipX, flipY } } ],
                       furniture:[ Placement { id, catalogId, variantIndex,
                                          pos: Vec3, rotY, scale, metadata{} } ] } ],
                   extensions: {} }   // forward-compat escape hatch
```
   Decisions baked in: openings live at *floor* level and bind to walls by proximity-projection at build time (EXOA's proven 0.2 m pattern *(DD §4.2)* — keeps door-dragging across wall joints seamless); furniture references `catalogId` (D6), never prefab names; `extensions` dict on every entity for AI/IKEA metadata without schema churn.
2. **Serialization:** Newtonsoft (Unity package already in-project), converters for math structs, `SchemaMigrator` ladder (D7), save to `persistentDataPath/Projects/{id}.json` + autosave slot. Golden sample documents as test fixtures.
3. **Command gateway (D2):** `ICommand { Apply(doc); }` records — `AddRoom, MoveRoomPoint, InsertRoomPoint, DeleteRoom, AddOpening, MoveOpening, ResizeOpening, SwapOpeningVisual, PlaceFurniture, MoveFurniture, ...` — executed via `DesignSession.Apply(cmd)`. Undo v1 = **bounded document snapshots** (EXOA-validated: 50 JSON snapshots *(DD §7)*; documents are KBs — simple beats clever). `DesignSession` emits `DocumentChanged(ChangeSet)` with dirty entity ids.
4. **DI + events (D4):** VContainer composition root; instanced C# events on services; zero statics.
5. **Validation service v1:** polygon simplicity (no self-intersection), min area, opening-fits-on-a-wall, opening-overlap. Pure C# — the same library later gates AI output server-side (Phase 8).

**Deliverables.** `IBMROS.RoomEngine` with document+commands+validation at >80% unit coverage; round-trip save/load; undo/redo on a headless document (no rendering yet — that's the point).
**Dependencies.** P0 asmdefs.
**Risks.** Over-modeling the schema. Rule: every field must be consumed by a phase in this roadmap or live in `extensions`.
**Reuse vs custom.** Custom everything; EXOA contributes schema *concepts* and the snapshot-undo pattern only.
**Complexity.** Medium.
**Never postpone:** command gateway, schema versioning, validation-as-library. **Postponable:** command *coalescing* (drag = one undo step) until Phase 3 makes it observable; cloud abstraction (an `IProjectStore` interface now, file-system impl only).

### Phase 1 — Status log (2026-07-17): ✅ COMPLETE — 31/31 tests green headless

**Close-out increment (same day):**
- ✅ **`IProjectStore` + `FileProjectStore`** (`Engine/Persistence/`) — decision: persistence lives in the *engine* (pure C#; Unity only supplies the root path at composition), so the same code serves tests, the app, and the future AI server. **Async interface from day one** (cloud impl in P9 is then non-breaking). **Atomic writes** (temp+move — a crash can never corrupt a project, D1). Listing skips corrupt/foreign files (browser never breaks). Autosave slot excluded from listings; corrupt autosave degrades to "no autosave".
- ✅ **`AutosaveService`** — hangs off `DocumentChanged` (D2: sees every mutation source), saves every N commands, fire-and-forget with `PendingSave` awaitable + `AutosaveFailed` event; `ConfigureAwait(false)` discipline throughout the store (no Unity sync-context capture).
- ✅ **Wire format locked**: decision — **camelCase members, dictionary keys verbatim** (JSON convention; matches DD/AI examples; Extensions metadata untouched). Enforced by `WireFormatGoldenTests` against a fully deterministic fixture (`GoldenFiles/BuildingDocument_v1.wire.json`, fixed GUIDs). Wire drift now fails CI; intentional changes require a schema-version bump + migration rung. `PureGoldenAssert` (runner-agnostic; repo-root probe) added alongside the Unity-only `GoldenAssert`.
- ✅ **Direct validator tests** (duplicate-id across kinds, tiny/two-point rooms, CW-warning-not-error, door-sill, zero-direction, absurd dims, negative height override) + **store/autosave lifecycle tests**. Total: **31 pure tests**, all green in dotnet (565 ms) and compiled identically by Unity's runner.

*(Original scope note kept below for history.)*

#### (superseded) CORE COMPLETE (~70%), 13/13 tests green headless

**Done (all verified by `dotnet test Tooling/EngineTests` — 13/13, 125 ms — plus the netstandard2.1/C#9 build guard; ~1,400 engine LOC, zero Unity references):**
- ✅ **Schema v1** (`Documents/`): `BuildingDocument` → `Floor` → `Room` (CCW `Vec2` polygon, `RoomStyle` with per-edge wall-material overrides) / `Opening` (D5: parametric hole + separate `OpeningVisual`; floor-level, proximity-bound at build time) / `FurniturePlacement` (catalogId, D6). GUID ids everywhere; `Extensions` dict on every entity; canonical meters; `DisplayUnits` per-project.
- ✅ **Own math structs** (`Vec2`/`Vec3`, doubles) — decision: match Clipper2 precision + clean stable JSON (AI target) + zero deps; view converts to floats at mesh emission. `PolygonMath`: signed area, CCW normalize, O(n²) simplicity test, segment intersection, point-on-segment projection (the opening-binding primitive), point-in-polygon.
- ✅ **Serialization + migration ladder (D7):** Newtonsoft, invariant culture, compact `{x,y}` converters; `SchemaMigrator` (missing version → clear error; future version → "update the app"; step-ladder pattern in place). `Clone` = round-trip (the snapshot primitive).
- ✅ **Command gateway (D2):** `DesignSession.Apply` is **transactional** — snapshot → mutate → validate → rollback on any error; no invalid document can escape. Snapshot undo/redo capped at 50 (EXOA-validated); `DocumentChanged(ChangeSet)` with dirty-entity ids (dirty-room rebuilds prepaid for P2). Instanced, injectable, zero statics (D4).
- ✅ **Command set:** AddFloor/DeleteFloor · AddRoom (normalizes winding at write) /AddRectangularRoom/MoveRoomPoint/InsertRoomPoint/DeleteRoomPoint/TranslateRoom/DeleteRoom/SetRoomStyle · AddOpening/MoveOpening/ResizeOpening/SwapOpeningVisual (hole-vs-visual separation honored)/DeleteOpening · PlaceFurniture/MoveFurniture/SetFurnitureVariant/RemoveFurniture.
- ✅ **Validator v1** (pure library, future server-side AI gate): polygon simplicity/min-area/winding, opening dims/sill/wall-height envelope, duplicate ids. Errors roll back; warnings travel with success.
- ✅ **Dual-runner tests:** pure engine tests live in `Tests/EditMode/Engine/**` — compiled by Unity's Test Runner AND by `Tooling/EngineTests` (dotnet, license-free, in CI). Round-trip, deep-clone independence, schema guards, winding normalization, bowtie rejection + untouched document, unknown-id rejection, undo/redo exactness, bounded history, redo invalidation, changeset dirty ids, opening lifecycle, opening-exceeds-wall rejection.

**Decisions:** no C# records/init (Unity `IsExternalInit` risk); mutable POCOs + snapshots over immutable documents (snapshots already deliver the benefit); **DI container (VContainer) deferred** until Phase 2 gives it a composition root worth composing — the engine is constructor-injected plain C# and needs no container.

**Remaining to close Phase 1:** `IProjectStore` interface + file-system impl + autosave slot (App layer — touches `persistentDataPath`); direct validator unit tests (duplicate-id, door-sill paths); golden sample documents as fixtures; Unity-side test-runner pass (should be automatic — same files).

---

## Phase 2 — Procedural Room Engine (4–6 weeks) → **M1**

**Objective.** `BuildingDocument` in → clean, textured, collidable 3D room out. Pure functions, headless-testable, mobile-fast.

**Why now.** First phase with visible output, and everything after edits *through* it. Built strictly against Phase 1's document so the geometry layer never grows its own state.

**Systems to build — in this order** (each step is testable without the next):

1. **Geometry math module:** winding normalization (port of `GetClockwise`), point-on-segment projection (port of `DistancePointLine`/`ProjectPointLine` — the opening-binding math *(DD §4.2)*), polygon area/containment, planar UV mapping (port of `GenerateUVs` — world-scaled texel density, EXOA-proven).
2. **Polygon ops facade** over Clipper2: `OffsetInward(points, t)`, `Subtract(subject, holes)` returning polygons-with-holes. Isolate the library behind our API (if Clipper2 ever disappoints, one file changes).
3. **Floor + ceiling builder:** tessellate contour (LibTess), ceiling = floor translated up + flipped (EXOA's exact trick — keep it, it's correct and free) *(DD §2.1)*.
4. **Wall builder:** per-edge wall segments in 2D wall-local space → rectangle minus opening rects (Clipper difference) → tessellate → extrude hole outlines for jambs/reveals → **Y-axis-constrained rotation** into place (carry EXOA's −X flip fix explicitly into a regression test — we paid for that lesson). One mesh per wall segment (enables per-wall painting and small rebuilds, *DD §3.1*).
5. **Opening binding:** project floor-level openings onto wall edges (≤0.2 m), compute `xPos`, clamp to wall extents (port EXOA's clamping), doors forced full-height-from-floor, windows honor `sillHeight` *(DD §4.1)*.
6. **Colliders:** MeshCollider on floor/ceiling; **box colliders per wall segment** (improvement over EXOA's MeshColliders — cheaper to cook on rebuild, and XR raycasts/physics prefer primitives; wall segments are boxes-with-holes, but a full-extent box + per-opening trigger boxes covers every raycast use we have). Fall back to MeshCollider only if opening-precise wall physics proves necessary.
7. **View layer (`IBMROS.RoomView`):** `BuildingRenderer` maps document ids → GameObjects; consumes `DocumentChanged(ChangeSet)`; **rebuilds only dirty rooms** (improvement over legacy rebuild-all *(DD §3.3)*); 100 ms throttle with queued-rebuild flag (EXOA-validated pattern *(DD §2.1)*); object-pool wall/floor GameObjects to kill churn.

**Deliverables (M1).** Fixture documents render correctly on device; golden-file tests green against legacy-EXOA reference outputs; rebuild of a 6-point room with 2 openings < 5 ms on mid-tier Android (measure, don't assume); zero per-frame allocations at idle.

### Phase 2 — Status log (2026-07-17): ENGINE CORE COMPLETE (~75%), 44/44 tests green headless

**Done (engine, pure C#, all headless-verified):**
- ✅ **`MeshData`** — engine mesh currency (doubles; view narrows to floats). **Winding convention locked**: Unity front faces satisfy `cross(v1−v0,v2−v0)·n < 0` (derived from Unity's clockwise rule); every surface asserts it in tests, so invisible/flipped faces can't ship silently. Explicit normals everywhere; `RecalculateNormals` banned downstream.
- ✅ **`PolygonOps`** — the one file that knows Clipper2 (meters→integer-mm scaling, mitered inward offset, NonZero subtract). **`Tessellation`** — the one file that knows LibTess (contours→mesh through caller-supplied toWorld/toUV lambdas, per-triangle winding fix-up).
- ✅ **`OpeningBinder`** — legacy-validated ≤0.2 m proximity binding; nearest-edge wins; corner clamping to width/2; wider-than-wall → skip+warning; **shared-wall openings bind in both rooms** (tested — that's a doorway); doors forced floor-to-height with the −1 mm undershoot trick; windows honor sill.
- ✅ **`RoomGeometryBuilder`** — pure function (Room, openings, settings)→(floor, ceiling, per-edge wall segments with holes cut + jamb/reveal extrusions). **Decision D-P2a: drawn polygon = finished interior face** (no inward offset in v1; dimensions are interior dimensions; single-sided walls give free see-through dollhouse view; thickness appears in jambs). **Decision D-P2b: explicit basis vectors, no rotations** — the legacy FromToRotation −X flip is unrepresentable; regression test proves the exact −X wall anyway. Height overrides honored; defensive CCW normalization on build.
- ✅ **Tests: 13 new geometry tests** (44 total) — area conservation (wall = rect − hole + jambs, hand-computed match to 4 decimals), winding-vs-normal on every surface, normals-point-at-centroid, concave L tessellation, −X regression, binder behavior matrix, golden fingerprint `RoomGeometry_LRoom_DoorWindow` (floor/ceiling 30 m² exact; wall areas verified by hand).

**Done (view + demo, Unity-verifies-on-focus):**
- ✅ **`MeshDataConversion`** (double→float at the boundary only) · **`BuildingRenderer`** (binds a DesignSession; per-frame changeset coalescing; **dirty-room-only rebuilds** on the hot path, full rebuild on structure change; explicit Mesh destruction — no leaks; URP Lit defaults) · **`RoomEngineDemoBootstrap`** (M1 demo: builds L-room+door+window through the real command gateway; on-screen buttons for point-drag/add-room/undo/redo).

**Deviations (documented):** walls use MeshColliders in v1 (axis-aligned boxes can't align to diagonal walls without rotated carrier GOs — roadmap's own fallback clause; revisit in P7 Quest pass). Goldens are self-referenced, not legacy-diffed (one-off manual EXOA comparison still pending). Rebuild throttle deferred to P3 where continuous drags exist (frame-coalescing in place).

**Remaining for M1:** Unity-side visual verification (demo bootstrap scene), on-device Android build + rebuild-time measurement (<5 ms target), legacy-output comparison one-off.
**Dependencies.** P1 document/events.
**Risks.** Clipper2 offset corner behavior differing from bundled ClipperLib (miter limits) — the golden harness catches it; tessellation of degenerate polygons — validation service rejects before geometry sees them.
**Reuse vs custom.** Technique + specific ported routines from legacy (owned license); code all new, on maintained libraries.
**Complexity.** High (the phase where geometry bugs live).
**Never postpone:** golden tests, dirty-room rebuilds, the pure-C# boundary. **Postponable:** jamb/reveal fancy profiling, UV2 anything (D8 — never needed), roof/exterior (cut, §5).

---

## Phase 3 — Runtime Editing (4–6 weeks) → **M2**

**Objective.** Users create and modify: draw rooms, drag points, manage multiple rooms and floors, undo, save — on touch, first-class.

**Why now.** Editing is meaningless without the engine (P2) and dangerous without commands/undo (P1). It precedes doors/furniture because both of those *are* editing interactions — this phase builds the interaction substrate they reuse (picking, gizmos, drag sessions, snapping).

**Systems to build.**
1. **Editor state machine:** explicit, instanced (not EXOA's global enum): `Browse / DrawRoom / EditRoom / PlaceOpening / EditOpening / PlaceFurniture / Walk`. Each state = a class owning its input handlers; transitions via the session, UI reflects state.
2. **2D plan view:** top-down orthographic camera over the *same* 3D scene (EXOA's approach — one scene, two cameras — cheaper than a separate 2D renderer and guarantees 2D/3D parity). Sync'd toggle like the reference app.
3. **Drawing tools:** tap-to-place control points; **grid snap + path/axis snap + snap-to-existing-points** (port the three snapping behaviors from `ControlPointsController` *(DD §2.1)* — the UX feel of the legacy tool is genuinely good; rebuild without its 731-line monolith by making each snap rule an `ISnapRule`). Rectangle-room quick tool (two taps) — the 80% case, and the Suite validated demand for it (`CreateRectangularRoom`).
4. **Edit tools:** drag point (one coalesced undo step), insert point on edge, delete point, drag whole room, delete/duplicate room. **Touch-native from day one** — tap-and-hold context menu, handle hit-targets ≥ 44 pt; the legacy plugin's Alt-click/right-click gaps *(prior investigation)* are the cautionary tale.
5. **Constraints & validation live:** invalid drags (self-intersection, min area) preview in red and refuse to commit — powered by the P1 validator.
6. **Multi-room:** adjacent rooms share nothing structurally (each room owns its walls — EXOA's model, correct for interiors); optional room-edge snap when drawing against an existing room.
7. **Multi-floor (tail of phase):** floor picker UI, add/duplicate/delete floor; view stacks floors at `elevationIndex × wallHeight` (schema supported this since P1).
8. **Undo/redo UI + autosave** every N commands to the autosave slot.
9. **Camera:** integrate `TouchCameraLite` (reuse as-is, licensed, mobile-proven) with focus-on-room.

**Deliverables (M2).** A stranger can draw a 3-room apartment with a hole for a doorway on a phone in <2 minutes, undo anything, kill the app, and reload it intact.

### Phase 3 — Status log (2026-07-17): SLICE 1+2 IN (~40%), 55/55 tests green headless

**Done — pure substrate (tested):**
- ✅ **Gesture coalescing** in `DesignSession`: `BeginGesture/EndGesture/CancelGesture` — a drag's many applies become **one undo step**; cancel restores the exact pre-gesture document instantly; undo/redo blocked mid-gesture; empty gestures add no history; a *rejected* apply mid-gesture keeps the last valid state and the gesture still commits cleanly (all tested).
- ✅ **Snap system** (`Engine/Editing/Snapping.cs`): `ISnapRule` chain with explicit priority — PointSnapRule (magnet to existing vertices) → AxisSnapRule (h/v alignment with previous point, tighter axis wins, free coordinate still grid-snaps) → GridSnapRule (fallback; 0 disables). The legacy tool's UX feel without its 731-line monolith. 11 new tests (55 total).

**Done — Unity interaction layer (user-verifies):**
- ✅ **`PlanEditorController`** (`App/`): self-bootstrapping plan editor — top-down ortho camera; **DrawRoom** (tap to place snapped points, LineRenderer preview, tap-first-point/Enter closes → `AddRoom`, Escape cancels); **EditPoints** (nearest-vertex pick, gesture-coalesced drag with live 3D regeneration, Escape aborts drag); Browse; OnGUI toolbar with undo/redo. Pointer→plan via y=0 plane intersection (no physics needed for editing).

**Decisions/deviations (documented):** legacy `Input` API for v1 (project runs "Both"; InputSystem action map = polish item); modes as enum-with-methods until P4/P5 grow the mode count (state-class extraction is mechanical); handles are simple spheres pending real UI.

**Remaining for M2:** insert/delete point tools; delete/duplicate room; touch-native context actions (tap-hold); live invalid-preview (red ghost) — currently invalid commits are rejected+logged; save/load UI over `IProjectStore`; multi-floor picker; TouchCameraLite integration + 2D/3D toggle; rebuild throttle during drags (currently per-frame coalescing only).

**Increment 2 (2026-07-17, after first on-screen user test):** user verified the editor runs (room drawn on screen) and reported the IMGUI toolbar unusably small on his display. Fixes+additions:
- ✅ **DPI-scaled IMGUI** via GUI.matrix (IMGUI's own hit-testing is matrix-aware — documented; the custom toolbar screen-rect check is scaled manually). `Screen.dpi` is unreliable → auto-scale is **logged once and Inspector-overridable** (`uiScaleOverride`) so the real value gets reported, not assumed. Larger base styles (48px buttons, 17–20pt text).
- ✅ **Insert-point-on-edge**: clicking a wall edge in EditPoints inserts a vertex and immediately drags it — insert+drag is ONE gesture (undo removes the point entirely).
- ✅ **Delete under cursor**: Delete/Backspace over a corner removes the vertex (engine guards ≥3 points); over a room interior removes the room.
- ✅ **Save / Load-latest** buttons over `FileProjectStore` (`persistentDataPath/Projects`); status line surfaces results.
- ✅ **Scene auto-light** (user's screenshot showed an unlit flat scene).
- **Process note:** user's standing rule recorded — *never assume; research and test; state what's unverified* (also saved to persistent memory).

**Increment 3 (2026-07-17, after second user test — 3 bugs reported, all root-caused):**
- ✅ **Mesh-offset bug** — renderer GO inherited the controller GameObject's (non-origin) position while engine meshes are absolute document coords; handles set world positions → meshes and handles diverged. Fix as invariant: **document space = world space** — `BuildingRenderer.Bind` forces identity transform (with warning); all editor visuals under a world-origin `IBMROS_Visuals` root.
- ✅ **Missed-close → ROOM_SELF_INTERSECTING** — 0.2 m world-space tolerance ≈ 14 px at his zoom. Fix: **all tolerances are screen-pixels** converted per zoom (`2·orthoSize/Screen.height` m/px): close 44 px, pick 30 px, snap 24 px; plus min-separation guard on placement; plus **engine-level `AddRoom` consecutive-dedupe (incl. wrap)** with a regression test that reproduces his exact failure (57/57 green). Close affordance: draft line turns green + snaps onto the first point when a click will close.
- ✅ **Toolbar too wide** — width 320→230, type 20/17→16/14, scale capped at 28% of screen width, floor 0.5×.
**Dependencies.** P1, P2.
**Risks.** Interaction feel eats unbounded polish time — timebox: feel-parity with legacy EXOA is the bar, reference-app polish waits for P4; state-machine sprawl — states are classes, no state exceeds ~200 LOC.
**Reuse vs custom.** Custom editor; ported snap math; reused camera.
**Complexity.** High (most UI-dense phase).
**Never postpone:** touch-native input, live validation, command coalescing. **Postponable:** room splitting (Suite parity — post-M4), free-standing single walls (schema supports; tool later), floor duplication.

---

## Phase 4 — Doors & Windows (3–4 weeks) → **M3, the thesis demo**

**Objective.** Parametric openings with catalog-model visuals: place, drag along walls, resize with handles + dimension chips, swap models instantly — arbitrary manufacturer GLBs.

**Why now.** The engine already cuts holes (P2) and the editor already drags things (P3); this phase is where both prove the product thesis — it is deliberately the *smallest* phase sitting on the *largest* amount of prior leverage.

**Systems to build.**
1. **Opening tools:** place door/window from palette → ghost preview slides along nearest wall (proximity projection from P2.5) → tap commits `AddOpening`. Drag existing opening along its wall; drag *across a corner* re-binds to the adjacent wall (falls out of proximity binding naturally — test it explicitly).
2. **3D manipulation gizmos** (the reference-app differentiator *(DD §5.2)*): side handles → width; top handle → height; sill handle (windows) → `sillHeight`; live **dimension chips** in cm/inches during drag; wall re-cuts live via the throttled rebuild. All handle drags emit coalesced commands — undo/XR/AI get this for free (D2).
3. **`IOpeningVisual` architecture (D5) — the recommended custom-door design:**

```
Opening (data)  ──anchor+dims──►  OpeningVisualHost (view)
                                     ├─ ProceduralVisual   : frame+panel+glass generated to fit (port
                                     │                       ProceduralOpening as the zero-content fallback)
                                     └─ CatalogGlbVisual   : downloaded model, normalized at import:
                                          • pivot re-based to bottom-center-hinge
                                          • +Z = outward, width→X, height→Y (auto from bounds + heuristics,
                                            manual override flags stored in catalog metadata)
                                          • measured bounds ⇒ DEFAULT opening dims (hole matches model)
                                          • "fit mode" per model: Resize(non-uniform scale within
                                            manufacturer min/max) | FixedSize(hole conforms to model)
                                     └─ JambLiner (procedural): always generated — covers wall-thickness
                                          reveal so ANY model, however thin, looks embedded
```
   The **import-normalization step** (bounds, pivot, orientation, thumbnail) is one pipeline shared with furniture (P5) — build it once here. Result: *any* GLB door works with zero per-model hand work when conventions hold, and a metadata override when they don't. This is the architecture the due diligence promised *(DD §4.4)*, upgraded with the JambLiner insight: the procedural reveal decouples visual quality from model quality.
4. **Catalog palette v1:** local JSON manifest (id, type, dims, min/max, thumbnail, GLB URI) — the schema that P5's real catalog service will serve; swap = `SwapOpeningVisual` command, hole re-parametrizes to the new model's dims (animated, like the reference app).

**Deliverables (M3).** The demo video: IKEA-style GLB door dropped into a wall, hole cut, dragged, resized with chips, swapped to a French door — on device. This is the artifact that proves the whole program.

### Phase 4 — Status log (2026-07-17): SLICE 1 IN (~45%), 64/64 tests green headless

**Done — engine (tested):**
- ✅ **`WallLocator`** — nearest-wall query (point-on-wall + direction + inward normal + distance-along-edge); the primitive behind placement ghosts and opening drags. Cross-corner re-binding falls out of "nearest changes" (tested); handles CW/CCW rooms defensively.
- ✅ **`OpeningVisualBuilder`** — the procedural FALLBACK visual (D5): door = frame strips (jamb/head) + two-sided panel at mid-thickness; window = 4-strip frame (incl. sill) + glass pane; archway = frame only. Built in the wall's explicit basis (same no-rotation construction as walls). Tests: part areas, winding convention, depth/extent envelope (never pokes through the wall or floats into the room). One test-arithmetic bug caught by the harness itself (expected 16 tris for 4 quads — it's 8).
- ✅ 7 new tests → **64 total**.

**Done — Unity layer (user-verifies):**
- ✅ `BuildingRenderer`: renders opening visuals with frame/panel/glass materials; **shared-wall doors deduplicate to one visual**; visuals rebuilt wholesale after any change (few + tiny meshes); geometry cache per room powers it.
- ✅ `PlanEditorController`: **Place Door / Place Window** modes (cyan ghost slides + clamps along the nearest wall; tap commits with settings defaults); **opening drag** in Edit Points (openings pick before vertices; re-binds across corners while dragging); **dimension chips** (world-anchored `W × H m` labels during ghost + drag); cyan anchor handles; Delete over an opening removes it.

**Remaining for M3:** width/height resize handles on openings; `CatalogGlbVisual` (glTFast model in the hole; import-normalization: pivot/orientation/bounds); catalog palette v1 (local JSON manifest) + `SwapOpeningVisual` UI; imperial units on chips; 2D↔3D camera toggle to actually admire the doors (pulled from P3 backlog — next increment).
**Dependencies.** P2 (holes), P3 (interaction), Spike B (glTFast).
**Risks.** Wild GLBs (pivot chaos, multi-mesh hierarchies, huge textures) — contained by the normalization step + override metadata; doors wider than their wall segment — P1 validator refuses, UI clamps.
**Reuse vs custom.** Port `ProceduralOpening` as fallback visual; all else custom.
**Complexity.** Medium.
**Never postpone:** `IOpeningVisual` abstraction, import normalization, dimension chips. **Postponable:** swing-arc display, opening trim/casing styles, archways (data type exists; tool later).

---

## Phase 5 — Furniture System: the IKEA pipeline (5–8 weeks) → **M4**

**Objective.** Runtime-downloaded catalog furniture: browse/search → stream GLB → place with snapping/collision → manipulate → variants → persist.

**Why now.** Needs rooms to stand in (P2), interaction substrate (P3), and the GLB import pipeline (P4). It's the biggest phase because it's the product's commercial heart — and it was *always* on us: neither EXOA generation supports runtime import *(DD §6.2)*.

**Systems to build.**
1. **Catalog service:** `ICatalogSource` → v1: bundled/CDN JSON manifest (id, name, category, dims, price, SKU/article no., variant list, GLB URI, thumbnail URI); designed for a server API later (P9). Map from the existing ros-pipeline catalog (SKUs/categories already exist per prior work). Search v1 = client-side name/category filter; server search is P9.
2. **Asset streaming:** download manager (concurrency-capped, resumable), disk LRU cache keyed by content hash, memory budget with unload-on-pressure; glTFast deferred instantiation off the main thread where possible. Placeholder proxy (bounds box + thumbnail) shown while streaming — placement shouldn't wait for megabytes.
3. **Module wrapper** (per instance): footprint BoxCollider from normalized bounds (drives everything, validated by both EXOA generations *(DD §6.1)*), category-driven **anchor type**: `Floor | Wall | Ceiling | Surface(on-top-of)`.
4. **Placement & snapping:** ghost preview follows raycast (floor/wall per anchor type); snap rules as `ISnapRule` chain (reuse P3 infrastructure): wall-back alignment (sofas/wardrobes flush + rotate to wall normal — port the `Join.cs` sweep/normal math as reference), room containment (P1 polygon test), module-to-module surface snap (lamp on table), grid snap toggle. Overlap = footprint AABB/OBB test → red ghost, refuse commit.
5. **Manipulation:** select (reuse legacy outline effect as-is), drag (coalesced), rotate (45°/15° steps + free), elevation for wall/surface items. **No free scaling of catalog furniture** — real products have real sizes; that's the point of an IKEA planner. Variants (`variantIndex` → material/mesh swap, EXOA's `ModuleVariants` concept *(DD §6.1)*) instead of scale.
6. **Persistence:** `Placement` records already in schema (P1); reload = catalog resolve + stream + place; missing-catalog-item fallback proxy (never lose user data over a 404 — D1).

**Deliverables (M4).** Browse ≥50 real IKEA items, furnish a room on device, force-quit mid-stream, reload intact; cold placement < 3 s on LTE for a typical item; memory stable furnishing 100 items.
**Dependencies.** P2–P4; catalog data from ros-pipeline.
**Risks.** *Highest-risk phase.* IKEA model sourcing/licensing is a product/legal question — flag now, not code-time; GLB quality variance (polycount, textures) → import pipeline gains budget checks + optional decimation server-side (P9); mobile memory — the LRU + placeholder design is the mitigation, budget tests in CI.
**Reuse vs custom.** Custom pipeline; `Join.cs` math + outline + thumbnail generator reused; glTFast adopted.
**Complexity.** High.
**Never postpone:** streaming placeholders, footprint-driven snapping, missing-item fallback. **Postponable:** module-to-module joints beyond surface-snap, in-app measurement tape, price/cart UX (P9), semantic search (P8/P9).

---

## Phase 6 — Materials & Environment (2–3 weeks, overlaps P5)

**Objective.** Paint walls/floors/ceilings per surface; believable realtime lighting on mobile.

**Why now.** Needs P2's per-wall meshes and P3's selection; independent of furniture internals, so it runs parallel to late P5.

**Systems to build.**
1. **Material catalog + paint tool:** per-wall-segment / floor / ceiling `TextureSetting {materialId, tiling}` in `RoomStyle` (P1 schema); tap-to-paint mode; world-scaled UVs from P2 make tiling correct automatically.
2. **Lighting rig (D8):** URP; one realtime directional + fixed ambient; **per-room ReflectionProbe re-rendered once per edit-settle, never per frame** (EXOA's probe-per-room idea *(DD §3.2)*, with the refresh discipline they lack); Adaptive Probe Volumes where Unity 6 mobile support allows; optional URP SSAO on high-tier devices.
3. **Environment:** HDRI skybox set visible through windows/openings; simple day/night slider (directional rotation + skybox swap).
4. **Mobile budget:** URP asset tiers (low/mid/high), target 60 fps mid-tier Android in a furnished 3-room scene; frame-time test scene in CI-adjacent manual checklist.

**Deliverables.** Painted, lit, 60 fps furnished apartment; probe update cost < 2 ms amortized.
**Dependencies.** P2, P3; P5 for furnished-scene profiling.
**Risks.** Expectation management — no baked-GI look (D8). Counter with probes + SSAO + good HDRIs; validate with a visual-bar screenshot review against the reference app.
**Reuse vs custom.** All custom (thin phase); EXOA contributes the probe-per-room pattern.
**Complexity.** Low–Medium.
**Never postpone:** probe refresh discipline, URP tiering. **Postponable:** day/night, decals/rugs system, custom material import.

---

## Phase 7 — XR Experience (4–6 weeks) → **M5**

**Objective.** Quest walkthrough + interaction; Vision Pro scoped, not built.

**Why now.** After M4 there is something worth being *inside*. Everything XR touches goes through commands (D2), so XR is a new *view+input*, not new logic — the payoff of P1's discipline. (Swappable with P8 if business needs AI sooner; they don't conflict.)

**Systems to build.**
1. **Platform:** OpenXR + XR Interaction Toolkit, Quest 3 first. Separate `IBMROS.XR` asmdef; same engine, same session.
2. **Locomotion:** teleport (floor meshes are teleport surfaces — P2 colliders just work *(prior investigation)*) + smooth-move option + human-scale/dollhouse toggle (dollhouse = the 2D editor's XR analogue; surprisingly cheap and high-value).
3. **Interaction:** ray + direct grab on furniture → emits the same `MoveFurniture` commands (undo works in XR for free); door/window handle gizmos rendered world-scale; hand tracking via XRI where Quest provides it, controllers first-class.
4. **Spatial UI:** wrist/palette menu for catalog (thumbnail grid), world-space dimension chips (P4's, re-styled). **No screen-space canvas survives into XR.**
5. **Comfort/perf:** Quest budget pass — wall/floor materials to mobile-lit variants, probe count cap, fixed foveated rendering; 72 fps floor.
6. **Vision Pro:** scoping memo only (PolySpatial shader audit, input mapping); building it is post-P9 unless product forces it *(DD §9)*.

**Deliverables (M5).** Design on phone → open on Quest → walk through, move furniture by hand, undo — same save file (cloud or sideload).
**Dependencies.** P2–P6; P1's command gateway is the load-bearing wall here.
**Risks.** Input-feel iteration time (XR always takes longer than estimated); mobile-URP vs Quest-URP material divergence — keep one URP pipeline, tier by quality settings.
**Reuse vs custom.** XRI adopted; all interaction bindings custom (thin, by design).
**Complexity.** Medium–High.
**Never postpone:** command-mediated interactions, world-space UI. **Postponable:** hand-tracking polish, multiplayer co-presence (P9+), Vision Pro entirely.

---

## Phase 8 — AI Systems (4–8 weeks for the platform + first features; ongoing after)

**Objective.** Make AI a *client* of the architecture, then ship the first three features: prompt-to-room, auto-furnish, and scan-to-room.

**Why the architecture was ready since P1 (design intent, restated):** an AI feature is anything that *reads* `GetDocument()` and *writes* `Apply(command)` or proposes a full document. That contract was D1+D2. Nothing in this phase modifies the engine.

**Systems to build.**
1. **AI gateway service (server-side):** app never holds LLM keys. Endpoints: `generateRoom(prompt) → BuildingDocument`, `furnish(document, roomId, constraints) → Placement[]`, `critique(document) → suggestions`. The **P1 validator runs server-side** (it's pure C# — .NET service or transpiled) so the app only ever receives valid documents.
2. **Structured output contract:** publish the JSON Schema of `BuildingDocument` (generated from the C# types — write the generator, don't hand-maintain); LLM structured-output mode against it; repair loop (validate → feed violations back → retry ≤2) — the pattern that makes prompt-to-room reliable rather than lucky.
3. **Tool-call surface:** expose the P1 command set as LLM tools (`add_room(points)`, `add_opening(...)`, `place_furniture(catalogId, pos, rot)` …) for *conversational editing* ("make the bedroom 1 m wider" → commands → undoable like any user edit). This mirrors what the Suite's HeadlessBuildingAPI would have given us *(DD §10)* — except ours is the same gateway users already exercise, so it is tested by every manual session.
4. **Feature 1 — Prompt-to-room** (pull-forward candidate after M2): prompt → document → render. Deterministic post-pass: snap points to grid, normalize winding, clamp dims.
5. **Feature 2 — Auto-furnish / recommendations:** input = room polygon + openings (keep-clear zones from door swings) + catalog slice (dims/categories/price); output = `Placement[]`; deterministic repair: containment clamp, overlap resolve, wall-flush snap (P5's rules re-run server-side or on-device). Recommendations = embeddings over catalog metadata (server, P9 infra).
6. **Feature 3 — Scan/image-to-room:** two lanes, both in-repo already: (a) **LiDAR scan** — SilverTau RoomPlanKit / OpenRoomPlan output (walls, doors, windows as boxes/planes) → mechanical mapper → `BuildingDocument` (no ML needed — this is days, and we're *ahead of the vendor here*, whose scanning is "Planned" *(DD §10)*); (b) **floor-plan image/sketch** — vision model → wall polylines + labels → document; CV quality is the risk, ship behind a "beta" flag.
7. **AI UX:** every AI result arrives as a *proposal layer* (ghost preview) the user accepts (batch-commit) or rejects — never silent mutation. Acceptance = one undo step.

**Deliverables.** Prompt-to-room demo (M6); auto-furnish of a scanned real room end-to-end (the killer demo: scan bedroom → IKEA-furnished twin in minutes).
**Dependencies.** P1 (contract), P5 (catalog), scan kits; server infra (light — one service).
**Risks.** LLM spatial-reasoning quality → mitigate with the repair loop + few-shot library of good documents; cost/latency → cache by prompt-hash, small models for furnish-scoring.
**Reuse vs custom.** Reuse the entire product as the AI's toolset — that was the plan. Scan kits reused.
**Complexity.** Medium (platform) — the engine work was prepaid.
**Never postpone:** validator-on-server, proposal-layer UX. **Postponable:** layout optimization/scoring, style transfer, image-to-room CV lane.

---

## Phase 9 — Production Features (6–10 weeks, then ongoing)

**Objective.** Single-device demo → commercial multi-user product.

**Why last.** Every system here wraps stable interfaces from earlier phases (`IProjectStore`, `ICatalogSource`, command stream). Building them earlier would have meant building them twice. (Exception flagged below: telemetry starts earlier.)

**Systems to build.**
1. **Accounts & cloud saves:** auth provider (choose per company stack); `IProjectStore` gains a cloud impl — sync = document upload + `schemaVersion` check; per-project thumbnails (reuse legacy thumbnail generator).
2. **Offline-first sync:** local is source of truth; queued sync; conflict policy v1 = last-writer-wins + "keep both copies" (documents are cheap — duplication beats merge UI); real merge (command-log rebase — the command stream makes CRDT-ish merging *possible* later) only if collaboration demands it.
3. **Collaboration (scoped):** v1 = share read-only link (server renders from document — the pure engine pays off again) + template gallery (curated documents). Realtime co-editing is a separate program; the command architecture is the prerequisite we now have — do not start it before product proves demand.
4. **Catalog operations:** server catalog API with delta updates, staged rollout, model QA pipeline (poly/texture budgets, auto-normalization from P4 — now server-side), takedown handling.
5. **Versioning & recovery:** document history (last N versions server-side), autosave crash recovery (P3), migration ladder discipline (P1) now mandatory for every schema change.
6. **Analytics & quality:** command-stream telemetry (privacy-filtered) — funnel: rooms drawn → doors placed → furniture placed → saved → shared; crash reporting; **start lightweight event logging in P3, not here** — you'll want the data by then.
7. **Release engineering:** build pipeline per platform (Android/iOS/Quest), feature flags, staged rollout.

**Deliverables.** Closed beta: accounts, cloud saves, shared links, live catalog, crash-free-rate and funnel dashboards.
**Dependencies.** All phases; company backend decisions.
**Risks.** Scope explosion — this phase absorbs infinite work; the list above is the v1 line. Backend choices outside solo-dev sweet spot → prefer managed services.
**Complexity.** High (breadth, not depth).
**Never postpone (within phase):** schema-versioned sync, crash recovery. **Postponed by design:** realtime co-editing, in-app purchases/cart, photoreal cloud rendering *(DD §13)*, web viewer (the pure-C# engine makes a server-side renderer feasible when wanted).

---

## Part III — Program-Level Material

### 10. Cross-cutting engineering practices

- **Testing pyramid:** engine = pure unit + golden-file tests (fast, CI); view = play-mode smoke tests per phase deliverable; on-device checklist per milestone. The D3 split is what makes the pyramid cheap — protect it in code review.
- **Performance budgets (tracked from P2, enforced from P6):** room rebuild < 5 ms; furnished 3-room scene 60 fps mid-Android / 72 fps Quest; cold furniture placement < 3 s LTE; zero steady-state GC allocs in Browse/Walk.
- **Definition of done, every phase:** tests green · on-device demo recorded · schema migrations written if schema touched · due-diligence doc updated if a finding changed.

### 11. Consolidated risk register (top 8)

| # | Risk | Phase | Mitigation |
|---|---|---|---|
| 1 | IKEA content licensing/sourcing | P5 | Product/legal question — raise immediately, in parallel with P0 |
| 2 | Geometry edge cases (our bugs now) | P2 | Golden tests vs legacy reference; port known fixes; optional Suite purchase as 2nd reference |
| 3 | Mobile memory under streamed catalog | P5 | LRU cache, placeholders, budget tests |
| 4 | Solo-dev estimate slip | all | Milestone-based scope cuts pre-agreed (§5); M3 is the only immovable demo |
| 5 | GLB quality variance | P4/P5 | Import normalization + metadata overrides + server QA later |
| 6 | XR input feel | P7 | Timebox; controllers before hands; XRI defaults first |
| 7 | LLM spatial output quality | P8 | Schema-constrained output + validator repair loop + proposal UX |
| 8 | Package conflicts (glTFast/Newtonsoft/ARF) | P0 | Spike B exists precisely for this |

### 12. Challenging my own plan (part 2 — where I might be wrong)

- **Am I under-selling the Suite?** If M3 slips past ~month 5, the $198 Suite-as-foundation path would have been faster to demo. Counter: the demo isn't the product; catalog+XR+AI dominate total effort either way, and they're identical in both plans. The ownership premium (~6–9 wks) buys out vendor risk permanently. I hold the recommendation — but P2's golden harness is the tripwire: if porting the geometry core exceeds 8 weeks, stop and reopen the Suite decision.
- **Is the command gateway overengineering for a solo dev?** It's the heaviest P1 discipline. No: undo alone justifies it (EXOA's most-missed feature), and it's ~days of code — the cost is discipline, not volume.
- **Is snapshot-undo too crude?** Possibly, once furniture scenes get large (full document copy per command). It's deliberately v1; the command stream lets us switch to inverse-command undo without touching callers. Don't optimize before it measurably hurts.
- **Deliberately simplified vs EXOA:** no roofs/exterior/outside spaces (§5), box colliders over MeshColliders for walls, no free furniture scaling, no curved walls. Each has a written escape hatch.
- **Deliberately harder than EXOA:** pure-C# engine split, DI, dirty-room rebuilds, id-based references. Each is prepaid cost for XR (P7), AI (P8), and server-side reuse (P8/P9) — the three places EXOA's shortcuts would have charged compound interest.

### 13. What to build next (this month)

1. **Week 1–2 — Phase 0 complete:** asmdef firewall, three spikes green, CI running, Suite-purchase decision made.
2. **Week 3–5 — Phase 1 complete:** document schema + commands + validator + save/load, all headless, all tested.
3. **Week 6 — first geometry:** floors/ceilings rendering from fixture documents; golden harness comparing against the legacy plugin's output for identical inputs.
4. **Then:** walls and the road to M1.

The single most valuable artifact in the next 90 days is **M3 — the Door Demo** (catalog GLB door, live-cut opening, drag/resize/swap with dimension chips). Every architectural choice above is sequenced to reach it with the minimum code that doesn't have to be thrown away afterward.

---

*End of blueprint. Maintain this document: each phase's completion PR should update its section with actuals (dates, deviations, lessons) so the roadmap remains the living source of truth for anyone joining the project.*
