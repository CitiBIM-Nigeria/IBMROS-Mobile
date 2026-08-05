#!/usr/bin/env python
"""
Offline depth bake — the `DepthExecution.OfflineDesktop` path from the Phase-1 spec (§13).

Runs Depth-Anything-V2-Metric-Indoor-Small over a recorded OpenRoomPlan session's RGB
frames and writes metric depth into  <session>/models/<modelId>/NNNNNN.bin  in the exact
same format the recorder uses for platform depth (int32 w, int32 h, w*h float32 metres).
The Unity Eval Tool and the headless harness then reconstruct from it with no changes.

TWO ORIENTATION FACTS, both established by measurement on sess_20260805_114510:

  1. Schema-v1 sessions recorded the JPEG 180 deg rotated relative to the ARCore depth
     map, and hence relative to the camera pose. Registration test over 34 frames,
     net-depth vs ARCore-depth Pearson r on a common grid:
         rot180 +0.73   mirrorH +0.40   mirrorV -0.04   identity -0.12
     Getting this wrong is silent and fatal: the net's depth is transposed onto the scene
     and every metric derived from it is noise. So the rotation is not hardcoded -- it
     comes from the manifest (see rgb_rotation_for), because v2 recorders fixed the
     problem at source and a stale constant here would reintroduce it backwards.

  2. After that rotation the frame is upright: in-image gravity derived from the camera
     pose points down in 100% of the session's 423 frames. No further rotation is applied,
     which matters because monocular depth nets lean on scene priors (floor low, ceiling
     high) and degrade on sideways input.

Output resolution is the RGB resolution, deliberately. The recorded intrinsics are stored
at that resolution, so the back-projector's scale factor is exactly 1 and its centre-crop
term is exactly 0 -- the net variant therefore avoids the one part of the geometry that is
inferred rather than verified (the 16:9-vs-4:3 crop model needed for ARCore's 160x90).
"""
import argparse, json, os, struct, sys, time
import numpy as np
import torch
from PIL import Image
from transformers import AutoImageProcessor, AutoModelForDepthEstimation

MODEL_ID = "depth-anything/Depth-Anything-V2-Metric-Indoor-Small-hf"
OUT_ID = "depth-anything-v2-metric-indoor-small"


def rgb_rotation_for(session):
    """
    How far to rotate the recorded JPEG so it sits in the depth map's frame.

    Read from the manifest rather than hardcoded, because the answer depends on which
    build recorded the session and getting it wrong is silent. Schema v2 recorders write
    RGB already aligned (`rgbAlignedWithDepth`); v1 wrote it 180 deg rotated, because
    `Transformation.MirrorY` mirrors across the y-axis (a HORIZONTAL flip) and the
    Texture2D encode path added a vertical one.

    A v1 session that predates the flag is assumed misaligned -- the conservative
    default, since that is what every session recorded before 2026-08-05 actually is.
    """
    try:
        with open(os.path.join(session, "manifest.json"), encoding="utf-8-sig") as f:
            m = json.load(f)
    except (OSError, ValueError):
        return 180, "no readable manifest -> assuming v1 (180 deg)"
    if m.get("rgbAlignedWithDepth"):
        return 0, f"manifest says RGB is aligned (schema {m.get('schemaVersion')})"
    return 180, (f"schema {m.get('schemaVersion', 'v1?')}, "
                 f"rgbTransform={m.get('rgbTransform', 'MirrorY (assumed)')} -> 180 deg")


def write_depth_bin(path, depth, w, h):
    """Byte-identical to SessionIO.WriteDepthBin."""
    with open(path, "wb") as f:
        f.write(struct.pack("<ii", w, h))
        f.write(np.ascontiguousarray(depth, dtype="<f4").tobytes())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("session")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--device", default=None)
    ap.add_argument("--rgb-rot", type=int, default=None,
                    help="override the manifest-derived RGB rotation (0 or 180)")
    args = ap.parse_args()

    # utf-8-sig, not utf-8: the first schema-v2 recorder wrote frames.jsonl with a BOM
    # (Encoding.UTF8 emits one). Fixed at source, but those sessions exist, and a reader
    # that rejects them for a leading EF BB BF is no use to anyone.
    with open(os.path.join(args.session, "frames.jsonl"), encoding="utf-8-sig") as fh:
        recs = [json.loads(l) for l in fh if l.strip()]
    if args.limit:
        recs = recs[: args.limit]

    rot, why = rgb_rotation_for(args.session)
    if args.rgb_rot is not None:
        rot, why = args.rgb_rot, "overridden on the command line"
    print(f"RGB rotation: {rot} deg  ({why})", flush=True)

    dev = args.device or ("mps" if torch.backends.mps.is_available() else "cpu")
    proc = AutoImageProcessor.from_pretrained(MODEL_ID)
    model = AutoModelForDepthEstimation.from_pretrained(MODEL_ID).eval().to(dev)

    outdir = os.path.join(args.session, "models", OUT_ID)
    os.makedirs(outdir, exist_ok=True)

    print(f"device={dev}  frames={len(recs)}  -> models/{OUT_ID}/", flush=True)
    lat, stats = [], []
    with torch.no_grad():
        for k, r in enumerate(recs):
            img = Image.open(os.path.join(args.session, r["rgbFile"])).convert("RGB")
            if rot:
                img = img.rotate(rot)  # 180 is its own inverse; no expand needed
            W, H = img.size

            t0 = time.time()
            x = proc(images=img, return_tensors="pt").to(dev)
            d = model(**x).predicted_depth
            d = torch.nn.functional.interpolate(
                d.unsqueeze(1), size=(H, W), mode="bicubic", align_corners=False
            )[0, 0]
            if dev == "mps":
                torch.mps.synchronize()
            lat.append(time.time() - t0)

            depth = d.float().cpu().numpy()
            write_depth_bin(os.path.join(outdir, f"{r['index']:06d}.bin"), depth, W, H)
            stats.append((float(depth.min()), float(np.median(depth)), float(depth.max())))

            if k % 50 == 0 or k == len(recs) - 1:
                print(f"  {k+1}/{len(recs)}  {lat[-1]*1000:.0f} ms  "
                      f"depth {stats[-1][0]:.2f}/{stats[-1][1]:.2f}/{stats[-1][2]:.2f} m",
                      flush=True)

    a = np.array(stats)
    print(f"\ndone. latency median {np.median(lat)*1000:.0f} ms  mean {np.mean(lat)*1000:.0f} ms")
    print(f"depth min {a[:,0].mean():.2f}  median {a[:,1].mean():.2f}  max {a[:,2].mean():.2f} m (session mean)")
    print(f"wrote {len(stats)} frames to {outdir}")


if __name__ == "__main__":
    sys.exit(main())
