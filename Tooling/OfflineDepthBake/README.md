# Offline depth bake

The Phase-1 spec's **offline runner** (§13): read a recorded session, run a depth model too
heavy or too awkward to deploy on-device, write the result back into the session under the
model's id. The Unity Eval Tool and the headless harness then reconstruct from it unchanged,
so every model is scored through the *same* pipeline — which is what makes the comparison
apples-to-apples rather than five bespoke experiments.

```bash
python3 -m venv --system-site-packages .venv && ./.venv/bin/pip install -r requirements.txt
./.venv/bin/python bake_net_depth.py /path/to/sess_YYYYMMDD_HHMMSS
```

Writes `<session>/models/depth-anything-v2-metric-indoor-small/NNNNNN.bin`, byte-identical in
format to `SessionIO.WriteDepthBin` (int32 width, int32 height, `w*h` float32 metres).

## Model

`depth-anything/Depth-Anything-V2-Metric-Indoor-Small-hf` — 24.8 M params, Apache-2.0,
metric indoor (20 m cap), 518x518 input.

The **metric** variant is used rather than the relative one the spec originally named. It
emits metres directly, so `ScaleAligner` is not in the loop at all — which removes the
spec's own risk item "scale ambiguity in monocular depth". Measured against ARCore depth on
sess_20260805_114510 the median ratio is **1.029**, i.e. the net's absolute scale is already
right to ~3% without any alignment. `IDepthModel` models this as `DepthOutputKind.MetricDepth`.

Note the HuggingFace **PyTorch** checkpoint is used, not the Keras/`kerasformers` port of the
same weights (`model.weights.h5`). Same model, but Unity's Inference Engine wants ONNX and
PyTorch is the sane export path; Keras 3 with custom layers is not.

## The 180-degree rotation

The recorded JPEG is **180 degrees rotated** relative to the ARCore depth map, and therefore
relative to the camera pose. The script rotates it back before inference. This was not
guessed — it was measured, comparing net depth against ARCore depth over 34 frames:

| transform applied to net depth | mean Pearson r | frames r > 0.5 |
| --- | --- | --- |
| **rot180** | **+0.7305** | 88% |
| mirrorH | +0.4033 | 44% |
| mirrorV | −0.0402 | 9% |
| identity | −0.1181 | 3% |

Skipping it is silent and fatal: the net's depth gets transposed onto the scene and every
metric derived from it is noise. Before the fix, whole-session net-vs-ARCore pixel
correlation was **negative** (−0.12); after it, **+0.46**.

The underlying cause is in the recorder (`WriteRgbJpg` uses `Transformation.MirrorY`, then
`Texture2D` + `EncodeToJPG` applies its own row reversal). Once the recorder writes RGB in the
same frame as the depth, set `RGB_ROT_DEG = 0` here — and re-verify with the table above
rather than trusting either of us.

## On-device viability

28 ms/frame median on an M-series Mac at 518x518 (MPS). That is *not* a phone number; treat
on-device latency as a separate measurement, as the spec does. It does mean the offline bake
over a 400-frame session takes ~15 s, so re-baking is cheap.
