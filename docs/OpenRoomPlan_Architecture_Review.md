# OpenRoomPlan — Architect's Review & Master Plan

> Critical review of the `OpenRoomPlan_Design_Report.pdf` (v1, July 2026) plus refined
> architecture and roadmap. This is a **living document** — revisit as research and
> on-device benchmarks land. Author role: lead architect / CV researcher.

## 0. Verdict up front

The design report is **strong and largely correct**. Its central thesis — *keep geometry
classical and on-device, keep semantics learned and lightweight, keep heavy 3D reasoning
optional/off-device* — is the right architecture, and it independently matches conclusions
reached in separate research (SpatialLM is batch/server-side, ARCore/ARKit VIO is the pose
source, don't put foundation SLAM on the phone, YOLO for furniture, Unity Inference Engine
naming/limits).

I endorse ~85% of it. This document records the **~15% I challenge**, the verification I ran,
and the refinements that follow. Nothing here blocks starting; several points change *what we
build first* and *what we must measure before trusting the premise*.

---

## 1. Verification results (independently checked)

| Claim in report | Status | Note |
|---|---|---|
| ARCore Depth API struggles on textureless/white walls | **Confirmed** | Google's own docs: "surfaces with few or no features, such as white walls, will be associated with imprecise depth." |
| ARPlaneManager vertical-plane detection unreliable on blank walls | **Confirmed** | Unity docs + field reports: textureless walls often fail to produce planes. |
| ARCore Scene Semantics does NOT label indoor walls/floors | **Confirmed** (report correctly omits it) | Scene Semantics is **outdoor-only** (sky/building/road…). Not a trap the report fell into. |
| SpatialLM = batch, stateless, CUDA, ~0.5–1B F32, server-side | **Confirmed** | No mobile export, no streaming, requires SLAM-reconstructed complete cloud. |
| NNAPI being frozen for LiteRT + vendor delegates | **Confirmed** | Deprecated from Android 15 (API 35). Report's TFLite-GPU/QNN fallback plan is correct. |
| Unity Inference Engine (ex-Sentis) has no true INT8 compute speedup | **Confirmed** | Storage quantization only; weights dequantize before the op. |
| MoGe-2 exists (metric monocular point maps) | **Confirmed** | Microsoft, NeurIPS 2025 — relevant newer option, see Challenge 2. |

No material factual errors found in the report. Citations that were spot-checked all resolve.

---

## 2. Critical challenges

### Challenge 1 — The textureless-wall problem is *the whole ballgame*, and it's under-weighted
The report lists "non-LiDAR depth is noisier" as its #1 High risk — correct, but it still reads
as one risk among many. It is not. It is **the single premise the entire project rests on**, and
my verification sharpens why: **both** on-device geometric methods fail on the *same* surfaces.

- ARCore **Depth API** (depth-from-motion) → imprecise on white walls (needs texture + parallax).
- AR Foundation **plane detection** → often no plane on white walls (needs feature points).

A typical room is dominated by large, flat, low-texture painted walls — exactly the worst case
for both. This means: **geometry-first is not an escape hatch from the depth problem.** The only
on-device thing that produces *plausible* geometry on a blank wall is a **learned monocular prior**
(it hallucinates a flat surface from monocular cues + training priors) plus a **Manhattan/Atlanta
solver** that extrapolates full planes from the textured *edges* (wall–floor, wall–wall corners do
have features). 

**Implication:** move depth-net quality from Phase 2 to a **Phase 0 go/no-go spike**. Before
committing 6 months, prove on 10–15 real rooms that fused (API + monocular-net) depth, snapped by
the Manhattan solver, reconstructs walls within an acceptable tolerance (propose: corner positions
within ~5–8 cm, wall angles within ~3°). If it can't, the non-LiDAR premise needs rethinking, not
more engineering.

### Challenge 2 — The monocular depth net is *load-bearing*, not a "fill layer"; benchmark MoGe-2
The report frames the depth net as a fill layer behind the platform API. Per Challenge 1, on
non-LiDAR devices the **net is the primary source of wall geometry**, not a supplement. That raises
the stakes on picking it well.

- Keep **Depth Anything V2-Small** (Apache-2.0, proven CoreML/ncnn/ONNX mobile export) as the
  on-device default. This is still the pragmatic choice.
- **Add MoGe-2 (Microsoft, NeurIPS 2025) to the Phase-0 benchmark.** It predicts *metric point maps*
  (geometry) directly rather than relative depth, and learns structural priors — a plausibly better
  fit for reconstructing planar room structure on textureless surfaces. Caveat: it is ViT-based and
  its mobile footprint is unproven; likely a **cloud-refine / future-flagship** option rather than
  mid-range on-device. Decision: benchmark it as the quality ceiling; ship DA-V2-S; keep MoGe-2 in
  the optional cloud pass alongside the report's DA3/Metric3D choices.

### Challenge 3 — Prefer 2D-detection-lifted-to-3D for furniture *first*; defer the distilled 3D detector
The report's Stage 6 plan — distill a PTv3/Sonata teacher into a 3DETR/VoteNet student over the TSDF
frustum — is the **highest-effort, highest-risk ML deliverable in the whole project** (training
infra, ScanNet/Structured3D/ARKitScenes data, weeks–months of research, still a teacher→student
accuracy drop). RoomPlan uses 3D detection because LiDAR hands it clean 3D for free; we don't have that.

**Challenge:** for a mobile-pragmatic v1, a **2D instance detector (YOLO-class) + depth back-projection
+ oriented-box fit** likely gets usable furniture cuboids far cheaper and more robustly than a distilled
3D voxel net — and we already validated YOLO runs in Unity Inference Engine. Fit the box from the
segmented mask's back-projected points (PCA for orientation, percentile extents for size), snap to the
floor plane and nearest wall. Defer the 3D sparse-conv distillation to a later "accuracy" phase, or drop
it entirely if 2D→3D proves good enough. This removes the biggest schedule risk from the critical path.

### Challenge 4 — Dense semantic segmentation is partly redundant with geometry; scope it down
The report runs dense stuff-segmentation (wall/floor/ceiling/door/window) *and* a geometric solver.
But geometry **already** classifies the big three for free: floor = low horizontal plane, ceiling =
high horizontal plane, wall = vertical plane. The unique, non-geometric value of the segmenter is:
1. **Openings (doors/windows)** — coplanar with walls, invisible to plane geometry.
2. **Furniture masks** — for Challenge 3's 2D→3D lift.

**Refinement:** don't pay for a full ADE20K dense segmenter to re-derive wall/floor/ceiling that RANSAC
gives deterministically. Scope the learned 2D models to (a) an **opening detector** (RoomPlan's own trick:
door/window as 2D detection on the projected wall plane) and (b) the **furniture segmenter**. This drops
a whole model class (SegFormer/TopFormer dense seg) off the hot path, saving NPU budget and a licensing
headache (SegFormer is non-commercial). Keep a *light* wall/floor confidence cue only if the solver needs
disambiguation.

### Challenge 5 — Missing workstream: evaluation harness + training-data story
The report is architecture-complete but has **no evaluation methodology** and a thin data story. For a CV
project this is a gap, not a detail. We need, from Phase 0:
- A **ground-truth set**: scan 15–20 real rooms with a LiDAR iPhone (RoomPlan output) or tape-measure
  floorplans as reference; define metrics (corner error cm, wall-angle error °, IoU of floorplan polygon,
  furniture box IoU).
- A **regression harness** that replays recorded sensor sessions (RGB+pose+depth) offline so we can
  iterate models without re-scanning. This also de-risks "estimates are unvalidated" (report's own Med risk).
- A **furniture-data plan** that dodges AGPL: training a YOLO-*architecture* detector on our own labeled
  data still often touches AGPL tooling — decide early between (a) a permissive detector (RT-DETR / EdgeTAM
  + classifier), (b) enterprise license, or (c) COCO-pretrained classes only (chair/couch/bed/table) for v1.

### Challenge 6 — Timeline realism
"RoomPlan parity by Phase 3 / week 18" is aggressive for a small team, driven almost entirely by the Stage-6
3D-detector distillation. Adopting Challenge 3 (2D→3D furniture first) is what makes the timeline credible —
it converts the riskiest research task into an optional later enhancement. Treat all week numbers as ~1.5×
optimistic until the Phase-0 spike validates the depth premise.

### Challenge 7 — Elevate object-centric relocalization for multi-room drift
The report parks Apple's "Rooms from Motion" (NeurIPS 2025) — using detected furniture cuboids as SLAM
primitives for drift-free, un-posed multi-room merging — in *future work*. Given multi-room drift is a
listed Med risk and a stated product goal (digital twin of a whole home), this is more central than
"future." Once furniture cuboids exist (Challenge 3), reusing them as relocalization anchors is a natural,
high-value design principle. Flag it as a Phase-4/5 design input, not someday-maybe.

---

## 3. Refined stack (deltas from the report only)

| Stage | Report's default | Refinement |
|---|---|---|
| Depth | DA-V2-S as fill behind API | **Primary geometry source on non-LiDAR**; benchmark MoGe-2 as ceiling; treat as Phase-0 go/no-go |
| 2D semantics | Dense stuff-seg (TopFormer) + furniture | **Drop dense stuff-seg**; keep only opening-detector + furniture segmenter (geometry covers wall/floor/ceiling) |
| 3D objects | Distilled 3DETR/VoteNet over TSDF | **2D-detect → depth back-project → oriented-box fit** for v1; distilled 3D detector deferred/optional |
| — | (none) | **Add eval harness + recorded-session replay + GT rooms** as a first-class workstream |
| Room reasoning | Classical Manhattan/Atlanta (+ optional RoomFormer) | Keep. Solver is *more* load-bearing given textureless walls — invest here early |
| Relocalization | VIO + ARWorldMap anchors | Add **furniture-cuboid anchors** (Rooms-from-Motion style) once cuboids exist |

Everything else in the report's stack table stands: AR Foundation `XRCpuImage` capture, ARCore/ARKit VIO
pose, voxel-hashed **semantic TSDF** on GPU compute, BEV pseudo-image wall/opening detection, parametric
scene graph → glTF/USDZ/JSON export, ONNX Runtime (CoreML/NNAPI EPs) + TFLite plugin + Inference Engine +
Burst, and the **multi-rate scheduler** (decouple 60 Hz render/pose from 1–10 Hz perception) — which is the
single best idea in the report and non-negotiable.

---

## 4. Refined master roadmap

Phase numbering keeps a working prototype at every step. Key change: a **Phase 0 depth spike** gates the
whole project, and furniture starts as 2D→3D.

### Phase 0 — Foundations + **depth go/no-go spike** (wks 1–4)
- Objective: Unity + AR Foundation project (Android+iOS); `XRCpuImage` capture, VIO pose, `AROcclusionManager`
  depth; GPU YUV→RGB; timing contract. **Plus:** record-and-replay harness + 15-room GT set + the depth
  benchmark (API vs DA-V2-S vs MoGe-2, fused + Manhattan-snapped) against GT.
- Why first: proves or kills the non-LiDAR premise before major investment.
- Deliverable: live AR app logging posed/timestamped RGB-D **+ a depth-accuracy report vs GT**.
- Success: fused depth reconstructs test-room walls within ~5–8 cm corner / ~3° angle after solver snap.
- Risk: **if this fails, stop and rescope** (LiDAR-only, or cloud reconstruction). This is the crux gate.

### Phase 1 — Geometry core, no ML (wks 4–8)
- GPU voxel-hashed semantic TSDF from posed depth; marching-cubes mesh; BEV rasterization; classical
  RANSAC → Manhattan/Atlanta wall solver; floor/ceiling from horizontal planes.
- Deliverable: real-time, LiDAR-free wall/floor reconstruction of a rectilinear room + 2D floorplan.
  **Shippable minimal product.**
- Depends on: Phase 0 depth passing.

### Phase 2 — Openings + furniture (2D→3D) (wks 8–13)
- Add the **opening detector** (door/window as 2D detection on projected wall planes, RoomPlan-style) and
  the **furniture 2D segmenter → depth back-projection → oriented-box fit** (Challenge 3). Integrate the
  BEV learned wall line-net as a *complement* to RANSAC.
- Deliverable: doors/windows + furniture cuboids on non-LiDAR devices; approaching RoomPlan-parity output.
- Note: this is where Challenge 4's scoping saves NPU budget.

### Phase 3 — Parametric contract + export (wks 13–17)
- Box-fusion, wall-snapping, parent relations, room-type sections, the **parametric scene graph**, and
  glTF/USDZ/JSON export. Editable-model output.
- Deliverable: full `CapturedRoom`-equivalent structured, editable twin.

### Phase 4 — Scheduler, thermals, multi-room, polish (wks 17–22)
- Multi-rate scheduler, motion-gating, keyframe selection, thermal back-pressure, double-buffered world
  model, scan-guidance MLP. Add **furniture-cuboid relocalization** for multi-room merging (Challenge 7).
- Deliverable: sustained 5-min scans within thermal budget on flagships; ~3 min mid-range; multi-room.

### Phase 5 — Optional cloud enrichment (wks 22–26)
- Opt-in server pass: MASt3R-SLAM/CUT3R global refine + SpatialLM structured completion + MoGe-2 high-fi
  depth. Privacy-gated, never required.
- Deliverable: premium "beautified" export + A/B quality lift.

### Phase 6 — Accuracy & hardening (ongoing)
- *Optional* distilled 3D object detector (the report's Stage 6) **only if** 2D→3D proves insufficient.
  Per-OEM NNAPI/QNN paths, device allow-list, device-farm benchmark harness, editable-model UI, package
  as Unity SDK. Track CUT3R/StreamVGGT/π³ distillation for a future single-model on-device SLAM+geometry.

---

## 5. Immediate next step

Run **Phase 0**, and specifically the **depth go/no-go spike**, before anything else. Everything downstream
is conditional on fused monocular depth being good enough on textureless walls. Build the record-and-replay
harness first so every later model iteration is measured against the same GT rooms.

## 6. Open questions to revisit
- Does MoGe-2 (or a distilled variant) ever fit a flagship NPU? If so it may collapse depth+structure.
- Do the streaming pointmap models (CUT3R/StreamVGGT/π³) reach mobile within the project horizon? They
  would replace VIO+depth-net+fusion with one model — the report's stated endgame.
- Furniture detector licensing: permissive detector vs enterprise license vs COCO-only v1 — decide before Phase 2.
