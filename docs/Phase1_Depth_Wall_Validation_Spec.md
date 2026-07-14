# Phase 1 — Depth & Wall-Reconstruction Validation (Spec)

> The first real phase of OpenRoomPlan, not a throwaway spike. Everything built here — the
> record/replay harness, the depth providers, the plane solver, the eval rig — is a permanent
> foundation the full pipeline reuses. Companion to `OpenRoomPlan_Architecture_Review.md`.

## 1. Objective & hypothesis

**Hypothesis (falsifiable):** On a mid-range, non-LiDAR phone, fusing platform depth
(ARCore Depth API) with a lightweight monocular depth net (Depth Anything V2-Small), accumulating
it under VIO pose, and snapping with a RANSAC + Manhattan/Atlanta solver, reconstructs room walls
accurately enough for a RoomPlan-class parametric twin — **specifically walls within ~8 cm corner
error and ~3° angle error** — *including on textureless walls*.

**Why this gates everything:** verification (Google + Unity docs) confirms both on-device geometric
methods — depth-from-motion **and** plane detection — degrade on the *same* textureless painted walls
that dominate real rooms. The learned monocular prior + geometric solver are the only on-device
mechanisms that can rescue those surfaces. If that rescue is insufficient, no amount of downstream
engineering fixes it; we'd pivot to LiDAR-first or cloud reconstruction. So we measure this **before**
building the pipeline.

## 2. Go / No-Go decision framework

Judged on the **standard** room set (exclude the deliberately-adversarial rooms G/D from the pass bar;
report them separately).

| Tier | Median corner err | Wall angle err | Floorplan IoU | Valid closed room | Decision |
|---|---|---|---|---|---|
| 🟢 GREEN | ≤ 8 cm | ≤ 3° | ≥ 0.90 | ≥ 80% of rooms | Proceed with architecture as designed |
| 🟡 YELLOW | 8–15 cm | 3–6° | 0.80–0.90 | 60–80% | Proceed **with adjustment**: stronger capture guidance, user corner-correction UI, and/or lean harder on cloud refine |
| 🔴 RED | > 15 cm | > 6° | < 0.80 | < 60%, or textureless rooms systematically fail | Rethink premise: LiDAR-first, or mandatory cloud reconstruction (MASt3R-SLAM/VGGT) |

Tolerances are anchored to the product use-case (placing furniture against a wall): <8 cm is
imperceptible in placement, 8–15 cm is noticeable-but-workable with editing, >15 cm breaks the illusion.

## 3. Experimental design — the key idea: **the LiDAR iPhone is its own ground truth**

We already have a LiDAR iPhone (the RoomPlan PoC). Exploit it to remove ground-truth alignment error entirely:

- **Dual-path capture in one ARKit session.** On the LiDAR iPhone, simultaneously run:
  - **Estimation path** — RGB + VIO pose + DA-V2-S only, *LiDAR deliberately ignored*. This *emulates a
    non-LiDAR device.*
  - **GT path** — ARKit `sceneDepth` (LiDAR) dense depth + RoomPlan parametric walls.
  - Both share the exact same ARKit world coordinate frame → **zero registration error** between estimate
    and GT. We directly measure "how much worse is non-LiDAR than LiDAR" per pixel and per wall.
- **Android confirmation study.** ARCore has no LiDAR GT, so on Android use **tape/laser-measured
  floorplans** (corner-to-corner + wall lengths + heights) as GT for Level B, and confirm ARCore Depth
  API behaves in the same error envelope as the iPhone's emulated non-LiDAR path.

This design isolates the variable that matters (LiDAR vs monocular) without conflating it with
cross-device coordinate alignment.

## 4. Test-room matrix (target ~18 sessions)

| ID | Room | Stresses | Set |
|---|---|---|---|
| A | Small office, textured walls, good light | baseline (easy) | standard |
| B | **Blank/white walls, minimal texture** | the killer case | standard |
| C | Large open-plan, high ceiling | range + VIO drift | standard |
| D | Glass / windows / mirror present | reflective/transparent failure | adversarial |
| E | Low light / evening | exposure, net degradation | standard |
| F | Cluttered, furniture occluding walls | partial walls, completion | standard |
| G | Non-Manhattan / angled wall | solver assumption break | adversarial |
| — | Repeat A + B with slow vs fast scan | motion / parallax sensitivity | standard |

Each session 1–3 min. Capture on ≥1 LiDAR iPhone (dual-path) and ≥1 mid-range Android (SD 7-series class).
Include a flagship Android if available (per-tier read).

## 5. The pipeline under test (minimal, but real)

```
XRCpuImage (RGB, 256–384px)  ──┐
ARCameraManager intrinsics     ├─► DepthProvider(s) ─► ScaleAlign ─► DepthFusion ─┐
Camera pose (VIO, world)     ──┤        (variants)      (to VIO)     (conf-weighted) │
ARPointCloud (sparse VIO pts)──┘                                                     ▼
                                                          PointAccumulator (voxel grid / light TSDF)
                                                                             │
                                                          PlaneSolver: RANSAC ► Manhattan/Atlanta snap
                                                                             │
                                                          Floorplan polygon + wall planes
                                                                             │
                                                          EvalReporter ─► metrics vs GT (JSON/CSV)
```

### Depth variant matrix (the comparison rig)
| # | Depth source | Runs |
|---|---|---|
| V0 | Platform depth only (ARCore Depth API; none on non-LiDAR iOS) | on-device |
| V1 | DA-V2-S only, scale-aligned to VIO | on-device |
| V2 | **Fused: platform + DA-V2-S** (confidence-weighted) — the candidate default | on-device |
| V3 | MoGe-2 metric point map — **quality ceiling** | offline (desktop, over recordings) |
| V4 | LiDAR depth — reference upper bound + GT | iPhone Pro only |

Each variant × solver setting `{raw / RANSAC-only / RANSAC+Manhattan}` for Level B, to quantify **how
much the solver rescues raw depth** (this tells us whether to invest in depth or in the solver).

Because the replay harness re-runs any model over recorded sessions, **heavy models (MoGe-2, DA3) run
offline on desktop over the exact same frames** — we get the quality ceiling without mobile deployment.

## 6. Metrics (precise definitions)

### Level A — raw depth accuracy (where LiDAR GT exists)
Compare estimated depth map vs LiDAR depth, per valid pixel:
- **AbsRel** = mean(|d_est − d_gt| / d_gt)
- **RMSE** (meters)
- **δ<1.25** = % pixels with max(d_est/d_gt, d_gt/d_est) < 1.25
- **Broken down by** surface class (textureless wall / textured wall / floor / glass / furniture — via a
  coarse manual or segmented mask) and **range bin** (0–1, 1–3, 3–5, >5 m).
- **Scale-factor stability** — the per-frame VIO scale-align factor; high variance ⇒ unstable metric scale.

### Level B — reconstruction accuracy (the decision metrics)
- **Corner error** — median & mean Euclidean distance, matched reconstructed↔GT room corners (cm).
- **Wall angle error** — mean |Δ| between reconstructed and GT wall normals (deg).
- **Wall offset** — perpendicular distance GT wall plane ↔ reconstructed plane (cm).
- **Floorplan IoU** — 2D polygon overlap of reconstructed vs GT floor plan.
- **Dimension error** — |L_est−L_gt|/L_gt for room length/width/height (%).
- **Completeness** — % of GT wall area within τ cm (τ=10) of a reconstructed wall.
- **Robustness** — fraction of rooms yielding a valid closed rectilinear floorplan.

### Performance sanity (cheap to grab, not the focus)
- DA-V2-S latency (ms/frame) on target Android + iPhone; achievable perception Hz; peak RAM; thermal
  drift over a 3-min scan. Confirms the perception rate assumed in the report is real.

## 7. Harness & artifacts (all reusable in later phases)

- **`SpikeSessionRecorder`** (MonoBehaviour) — logs per frame: timestamp, intrinsics (for the downscaled
  res), 4×4 world pose, RGB (JPEG, 256–384px long edge), platform depth (EXR/float16 + confidence),
  sparse VIO points, and on LiDAR devices the LiDAR depth + a RoomPlan export. Session = folder:
  `frames/000123.jpg`, `depth/000123.exr`, `lidar/000123.exr`, `meta.jsonl`, `roomplan.json`.
- **`SpikeSessionPlayer`** — deterministically replays a session into the pipeline (offline iteration
  without re-scanning). *On Android, optionally use ARCore's native Recording & Playback API for
  full-fidelity depth-stream replay; the custom logger is the portable cross-platform path.*
- **`IDepthProvider`** implementations: `PlatformDepthProvider` (`AROcclusionManager`), `DANetDepthProvider`
  (Unity Inference Engine over DA-V2-S ONNX), `MoGeDepthProvider` (offline/desktop).
- **`ScaleAligner`** — least-squares scale+shift of net depth to VIO metric using sparse VIO points /
  confident platform-depth pixels as anchors; logs the factor.
- **`DepthFusion`** — confidence-weighted merge (platform where confident, net to fill).
- **`PointAccumulator`** — posed points into a hashed voxel grid (proto of the Phase-2 semantic TSDF).
- **`PlaneSolver`** — RANSAC plane extraction → Manhattan/Atlanta angle regularization → floor/ceiling
  from horizontal planes → wall polylines → floorplan polygon.
- **`EvalReporter`** + **offline Python eval** — aggregate stats, per-room/per-variant/per-surface tables,
  plots, and the final go/no-go call.

## 8. Timeline (~3–4 weeks; carries forward, not disposable)

- **Wk 1 — Capture foundation.** Add AR Foundation + ARCore + ARKit packages; XR Plug-in Management setup;
  `XRCpuImage` + intrinsics + pose + platform depth + (LiDAR) sceneDepth logging; recorder/player harness.
  *Deliverable: record & replay a session on both platforms.*
- **Wk 2 — Depth + solver.** DA-V2-S in Inference Engine (preprocess→infer→metric via scale-align);
  fusion; point accumulation; RANSAC + Manhattan solver; EvalReporter; offline Python eval.
  *Deliverable: pipeline runs over recorded sessions, outputs walls + metrics.*
- **Wk 3 — Data + runs.** Capture the ~18-room GT set (iPhone dual-path + Android tape-measure); run V0–V4
  × solver settings; run MoGe-2/DA3 offline for the ceiling. *Deliverable: full metrics matrix.*
- **Wk 4 — Analysis + decision.** Depth Accuracy Report; go/no-go; architecture adjustments. Buffer.

## 9. Deliverables
1. **Record/replay harness** + documented session format (permanent tooling).
2. **Minimal depth→planes reconstruction pipeline** (foundation for Phases 2–3).
3. **Ground-truth room dataset** (~18 sessions, iPhone dual-path + Android measured).
4. **Depth Accuracy Report** — Level A + Level B, per room / variant / surface / range, performance
   sanity, and the **go/no-go decision + recommended architecture adjustments**.

## 10. Risks *within the spike*
- **GT alignment error** → mitigated by the dual-path single-session iPhone method (§3).
- **Scale ambiguity in monocular depth** → measure scale-factor stability; if unstable, that itself is a finding.
- **Device variance** → test ≥1 mid-range + ≥1 flagship Android + LiDAR iPhone; report per-tier, not pooled.
- **DA-V2-S mobile export quirks in Inference Engine** (opset/quantization) → validate the ONNX import in Wk 1;
  fall back to ONNX Runtime + NNAPI/CoreML if Inference Engine underperforms.
- **Over-fitting to few rooms** → fixed room matrix chosen for failure-mode coverage, standard vs adversarial split.

## 11. Immediate setup step (blocking)
Add to `Packages/manifest.json`: `com.unity.xr.arfoundation` (6.x), `com.unity.xr.arcore`, `com.unity.xr.arkit`
(`com.unity.ai.inference` already present). Then configure XR Plug-in Management (ARCore loader on Android,
ARKit loader on iOS). This is a real project-config change — do it deliberately, then Wk-1 harness work begins.

---

# v2 — Locked scope, modular benchmark framework, model roster

*Refinement after review. Supersedes the ambition level of §4/§6 where they conflict: keep it lean,
answer the core question fast, but build the depth-model benchmark as a reusable plugin framework.*

## 12. Locked scope (what we actually build now)
1. **Capture/replay harness** (reusable forever).
2. **AR Foundation + ARCore + ARKit** integration.
3. **On-device candidate:** ARCore Depth + Depth-Anything-V2-Small + the **fused** combination (primary).
4. **Basic wall reconstruction:** RANSAC planes → Manhattan/Atlanta snap → floorplan.
5. **Small representative room set first** (~5–6 rooms covering the key failure modes; expand later only if needed).
6. **Go/No-Go decision** (thresholds in §2).
7. **Desktop depth-model benchmark** over the *same recorded sessions* — evaluate stronger models as
   optional cloud/offline refinement, **not** for on-device use.

Explicitly **de-scoped for now** (deferred, not cancelled): the full ~18-room matrix, exhaustive
per-surface/per-range Level-A tables, and any furniture/opening detection (that's Phase 2).

## 13. Modular depth-model benchmark framework (the reusable core)

Requirement: adding a *future* depth model must be trivial — implement one interface, register it, done.
No hardcoding for the initial three. Design:

- **`IDepthModel`** — the single plugin contract. `Info` (metadata: id, execution location, metric vs
  relative, depth vs point-map, license), `Initialize()`, `Infer(DepthFrameInput) → DepthFrameResult`,
  `Dispose()`. On-device and offline models implement the *same* interface.
- **`DepthExecution`** enum — `OnDeviceUnity` | `OfflineDesktop` | `Cloud`. The harness treats all
  uniformly; offline/cloud models' Unity-side adapter just reads precomputed depth maps produced by an
  external runner over the same session (so heavy models never need Unity/mobile).
- **`DepthModelRegistry`** — models self-register (id → factory). The benchmark iterates the registry ×
  recorded sessions, so "add a model" = add a file + one registration line.
- **Shared session input** — every model consumes the same `DepthFrameInput` (RGB + intrinsics + pose +
  timestamp) from the recorded session, guaranteeing apples-to-apples comparison.
- **Shared eval** — every model's `DepthFrameResult` stream flows through the *same* reconstruction
  pipeline + `EvalReporter`, so wall/corner/angle/IoU metrics are computed identically for all.
- **Offline runner** — a thin desktop harness (Python) reads a session folder, runs the heavy model
  (MoGe-2 / Depth Pro / DepthCrafter / …), writes depth EXRs back into the session under the model's id.
  Unity's `OfflineDepthModel` adapter loads those. This is how we benchmark SOTA without mobile export.

This means the "compare N depth models" task and the "validate the on-device candidate" task run through
**one** pipeline; the only difference is which `IDepthModel` feeds it.

## 14. Model roster & comparison matrix

Grouped by role. Minimum benchmark set = **DA-V2-S, MoGe-2, Depth Pro, Video Depth Anything (metric)**.
The rest are "add later via `IDepthModel`" — listed to prove the framework's extensibility.

**On-device candidates (must run on phone):**
- **Depth Anything V2-Small** — baseline, Apache-2.0, proven CoreML/ncnn/ONNX export. *Primary.*
- **ARCore Depth API / ARKit sceneDepth** (platform) — fused with the above = candidate default.

**Offline/cloud quality-ceiling (desktop, over recordings):**
- **MoGe-2** (Microsoft, NeurIPS 2025) — metric *point maps* + structural priors; strong wall-geometry fit.
- **Apple Depth Pro** (ICLR 2025) — sharp metric, no intrinsics needed; fixed 1536² = heavy, ~0.3 s/V100.
- **Depth Anything V3**, **UniDepth v2**, **Metric3D v2** — easy future plug-ins.

**Temporal-consistency specialists (for the temporal-stability axis; offline/cloud, video):**
- **Video Depth Anything (metric)** — CVPR 2025; DA-V2 lineage ⇒ cheap to add; the pragmatic temporal pick.
- **Online Video Depth Anything** — Oct 2025; streaming + low memory; most deployment-relevant, watch for mobile.
- **DepthCrafter** (diffusion) — most temporally consistent per some metrics but slow/low-res; upper-bound reference.
- **RollingDepth** — optimization-based global alignment; strong offline.

**Comparison axes (per model):** wall-reconstruction quality · corner accuracy · wall-angle accuracy ·
metric-scale accuracy · temporal stability (video consistency) · runtime · memory · Unity-integration
ease · on-device suitability · cloud/offline suitability. Report as a single scored table + short notes.

## 15. Code architecture (this project)
Isolated assemblies under `Assets/OpenRoomPlan/` (do **not** dump into `Assembly-CSharp` — the existing
global-namespace sprawl is a known pain point):
- `OpenRoomPlan.Core` (no external deps) — session schema, `IDepthModel` + registry, metric types,
  pipeline-stage interfaces. Pure C#, compiles today.
- `OpenRoomPlan.Capture` (refs AR Foundation) — recorder/player, platform depth provider. *After AR packages added.*
- `OpenRoomPlan.Depth` (refs Inference Engine) — DA-V2-S provider, scale-align, fusion.
- `OpenRoomPlan.Reconstruction` — accumulator, RANSAC + Manhattan solver, floorplan.
- `OpenRoomPlan.Eval` — metrics + reporter; `OpenRoomPlan.Editor` — offline runner glue, menu tools.

## 16. Prerequisite action (do via Package Manager, not hand-edited manifest)
Add by name in **Window ▸ Package Manager ▸ + ▸ Add package by name**: `com.unity.xr.arfoundation`,
`com.unity.xr.arcore`, `com.unity.xr.arkit`. Use the GUI (not a hand-edited version string) so the editor
resolves versions compatible with Unity 6000.4. Then **Project Settings ▸ XR Plug-in Management**: enable
**ARCore** (Android tab) and **ARKit** (iOS tab).

⚠️ **Interaction note:** installing `com.unity.xr.arkit` defines `UNITY_XR_ARKIT_LOADER_ENABLED`, which
**activates the RoomPlanUnityKit XR bridge scripts** (currently compiled out). They should still compile,
but this is expected behavior change — verify the RoomPlan iOS PoC still builds after adding AR packages.
Also: AR Foundation is inert without an `ARSession` in a scene, so the existing Main/Room/RoomScanning
scenes are unaffected at runtime; the main cost is a larger build and the XR loader initializing.
