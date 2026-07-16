# Phase 2 (start) — On-device depth: DA-V2-S provider, scale-align, fusion

Implements the depth stage the design report opens Phase 2 with — *"add the quantized
Depth-Anything-V2-Small depth net and the depth-fusion (API + net, scale-aligned)"* — and the
`OpenRoomPlan.Depth` assembly listed in the Phase-1 spec §15. This is the piece that rescues
textureless walls, which the review names as the whole ballgame.

## What was added

New assembly **`Assets/OpenRoomPlan/Runtime/Depth/`** (`OpenRoomPlan.Depth`, refs
`OpenRoomPlan.Core` + `Unity.InferenceEngine`):

| File | Role |
|---|---|
| `DANetDepthProvider.cs` | `IDepthModel` over a DA-V2-S ONNX via Unity Inference Engine. CPU preprocess (bilinear resize + ImageNet normalize → NCHW) from the borrowed `DepthFrameInput.rgb`, so it runs live on-device **and** offline over recorded sessions. Config-driven (`DANetConfig`). |
| `ScaleAligner.cs` | Least-squares affine fit of relative/disparity output → metres, against either a reference metric depth map (platform/LiDAR) or projected VIO anchors. Disparity nets are fit in disparity space (`a·disp+b ≈ 1/metric`). |
| `DepthFusion.cs` | Confidence-weighted merge: platform depth where confident, net fills the holes (blank walls, long range) = the **V2 fused candidate default**. |
| `DepthProviders.cs` | Self-registers DA-V2-S into `DepthModelRegistry` (id `depth-anything-v2-small`). |

Plus **`Assets/OpenRoomPlan/Editor/DepthBakeWindow.cs`** (menu **OpenRoomPlan ▸ Bake Depth (DA-V2-S)**)
so the net can be exercised offline with no device, reusing the existing Eval Tool.

Core: `DepthFrameResult` gained `isInverseValues` so raw disparity flows through alignment correctly.

## The one manual step — drop in the model

The `.onnx` is a large binary with a licensing choice, so it is **not** committed. To wire it up:

1. Get **Depth-Anything-V2-Small** as ONNX (Apache-2.0 — the license the report clears for shipping).
   The stock export is 518×518, affine-invariant disparity output.
2. Put it under any `Resources` folder so Unity imports it as a `ModelAsset`, at the path
   `DANetConfig.resourcePath` expects by default:
   `Assets/OpenRoomPlan/Resources/OpenRoomPlan/Models/depth-anything-v2-small.onnx`
3. If you use a **metric-fine-tuned indoor** variant instead, set `outputIsMetric = true` and
   `outputIsInverse = false` in the config (then no scale-align is needed).
4. If your export uses a different input size, set `inputWidth/inputHeight` to match (fixed-shape ONNX
   will reject other sizes).

## How to run it (offline, no phone)

1. Record a session on a device (existing **Create Capture Scene** flow), pull it into
   `persistentDataPath/OpenRoomPlan/Sessions/`.
2. **OpenRoomPlan ▸ Bake Depth (DA-V2-S)** → pick the session → *Align net to* = Platform depth
   (Android) or LiDAR GT (iPhone) → **Bake**. Writes metres into `models/depth-anything-v2-small/`.
3. **OpenRoomPlan ▸ Eval Tool** → Depth source = **Model**, id = `depth-anything-v2-small` →
   **Reconstruct** (optionally *+ overlay LiDAR GT* to eyeball corner/dimension error).

This routes the net through the **same** RANSAC → Manhattan reconstructor that scores platform and
LiDAR depth — apples-to-apples, which is the whole point of the harness.

## Done in follow-up (compile-verified 17:02)

- **V2 fusion bake mode** — the bake tool has a *Fuse with platform depth* toggle that runs
  `DepthFusion.Fuse` (with recorded ARCore confidence) and writes the fused candidate to
  `models/depth-anything-v2-small-fused/` (V2), separate from net-only `models/depth-anything-v2-small/` (V1).
- **VIO-point logging** — the recorder now writes sparse `ARPointCloud` world positions to `points/NNNNNN.bin`
  (`FrameRecord.pointsFile`); the player populates `metricAnchorsWorld`, so `ScaleAligner.AlignToAnchors`
  (the non-LiDAR-iOS path) is now exercisable offline via the bake tool's *VIO anchors* align source.

## Not yet done

- **Perf validation on device** — the code compiles cleanly (verified in Unity 6000.4.6f1: all OpenRoomPlan
  assemblies incl. `OpenRoomPlan.Depth` + `.Editor` rebuilt with zero errors). Still need on-device DA-V2-S
  latency once the model is dropped in (report's Table 11 assumes 6–10 Hz mid-range).
