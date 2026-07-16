# Phase 1 — How to run the capture app

You don't build any scene or UI by hand. One menu click generates everything.

## 1. Generate the capture scene (one click)
In the Unity top menu bar: **OpenRoomPlan ▸ Create Capture Scene**.

This creates and saves `Assets/MyStuffs/Scenes/OpenRoomPlanCapture.unity` with:
- a full AR rig (AR Session + XR Origin + AR Camera with camera/occlusion/point-cloud managers),
- the `CaptureSessionRecorder`,
- a **UI Toolkit** record screen (Record / Stop buttons + status), reusing your existing
  `Assets/UI Toolkit/PanelSettings.asset` + theme,
- and it adds the scene to Build Settings.

A dialog confirms when it's done.

## 2a. Try it in the Editor (no phone needed — tests the UI + flow)
Open `OpenRoomPlanCapture.unity` and press **Play**. AR Foundation's **XR Simulation** loads a virtual
room in the Editor (navigate: hold right-mouse + WASD). Tap **Record**, move around, tap **Stop**.

> Note: XR Simulation exercises the UI, camera pose, and RGB path, but it does **not** provide real
> environment depth — so depth files will be sparse/empty in the Editor. Real depth needs a device (2b).

## 2b. Run on a real phone (real capture)
1. Switch platform (`File ▸ Build Settings`) to Android or iOS.
2. `Project Settings ▸ XR Plug-in Management` → enable **ARCore** (Android) / **ARKit** (iOS).
3. **Build & Run** to the device. Grant the camera permission.
4. Tap **Record**, walk slowly around the room keeping walls in view, tap **Stop**.

## 3. Where recordings go
`Application.persistentDataPath/OpenRoomPlan/Sessions/<sessionId>/` on the device, containing
`frames/` (RGB), `depth/` (Android motion-stereo depth) or `lidar/` (iPhone Pro GT), `manifest.json`,
`frames.jsonl`. The Console logs the exact path and the depth image format on the first recorded frame.

Pull a session folder off the device (Xcode devices window / Android `adb pull` or Device File Explorer)
for the offline benchmark and eval steps that follow.

## 4. What's next
- **Eval tool** (Editor) — DONE: `OpenRoomPlan ▸ Eval Tool` loads a recorded session → runs the
  reconstructor → point cloud + room + dimensions, optional LiDAR-GT overlay. With the GT overlay it now
  prints the **Level-B decision metrics** (median/mean corner error, wall angle error, wall offset,
  floorplan IoU, dimension error, wall-match completeness) and the per-room **Go/No-Go tier**
  (GREEN ≤8 cm & ≤3° & IoU ≥0.90 / YELLOW / RED) straight from the spec §2 table (`RoomEval.cs`).
  An **Accumulator** dropdown selects RawPointCloud (Phase-1 behaviour) or **Tsdf** — a voxel-hashed
  semantic TSDF (`TsdfVolume.cs`) that fuses depth as a weighted running average, denoising
  motion-stereo/net depth before the solver. Same session + both accumulators = a direct read on how
  much fusion buys. The TSDF also carries a per-voxel `SemanticClass` channel and a BEV density
  rasterizer, ready for Phase-2b segmentation and the wall line-net.
- **Depth Anything V2-Small** provider — DONE (code): `OpenRoomPlan.Depth` assembly + `OpenRoomPlan ▸
  Bake Depth (DA-V2-S)`. Still needs the ONNX model dropped into a `Resources` folder — see
  [Phase2_DepthProvider.md](Phase2_DepthProvider.md).
