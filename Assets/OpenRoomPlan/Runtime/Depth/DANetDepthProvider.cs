using System;
using Unity.InferenceEngine;
using UnityEngine;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Depth
{
    /// <summary>
    /// Tunable knobs for a Depth-Anything-V2-style monocular net running in the Unity Inference Engine.
    /// Defaults target the stock DA-V2-Small ONNX (518x518, ImageNet normalization, affine-invariant
    /// disparity output). For the metric-fine-tuned indoor variant, set <see cref="outputIsMetric"/> true
    /// and <see cref="outputIsInverse"/> false. Kept data-driven so a re-exported / different net is a
    /// config change, not a code change — matching the modular benchmark contract (spec §13).
    /// </summary>
    [Serializable]
    public sealed class DANetConfig
    {
        [Tooltip("Resources path of the imported ONNX ModelAsset (no extension). Drop the .onnx under any " +
                 "Assets/**/Resources/ folder; Unity imports it as a ModelAsset addressable by this path.")]
        public string resourcePath = "OpenRoomPlan/Models/depth-anything-v2-small";

        [Tooltip("Network input size. Must match the ONNX if it was exported with a fixed shape (stock DA-V2 = 518).")]
        public int inputWidth = 518;
        public int inputHeight = 518;

        [Tooltip("ImageNet channel mean/std used at training. RGB order.")]
        public Vector3 mean = new Vector3(0.485f, 0.456f, 0.406f);
        public Vector3 std = new Vector3(0.229f, 0.224f, 0.225f);

        [Tooltip("Stock DA-V2 emits affine-invariant DISPARITY (larger = nearer). Leave true unless using a metric net.")]
        public bool outputIsInverse = true;

        [Tooltip("Metric-fine-tuned variants emit depth in meters directly (no ScaleAligner needed).")]
        public bool outputIsMetric = false;

        public BackendType backend = BackendType.GPUCompute;

        public string modelId = "depth-anything-v2-small";
        public string displayName = "Depth Anything V2-Small";
        public string license = "Apache-2.0";
    }

    /// <summary>
    /// On-device monocular depth via Unity Inference Engine (ex-Sentis, com.unity.ai.inference).
    /// Preprocessing is done on the CPU from the borrowed <see cref="DepthFrameInput.rgb"/> buffer so the
    /// same provider works live on-device and offline over recorded sessions, with no Texture dependency.
    ///
    /// Output contract: for the stock (relative) net this returns the RAW model values with
    /// <c>isMetric = false</c> and <c>isInverseValues = true</c>; <see cref="ScaleAligner"/> converts to
    /// metres against a reference (platform/LiDAR depth or VIO anchors). A metric net returns metres with
    /// <c>isMetric = true</c> and can feed the reconstructor directly.
    /// </summary>
    public sealed class DANetDepthProvider : IDepthModel
    {
        readonly DANetConfig _cfg;
        Model _model;
        Worker _worker;
        float[] _chw;          // reused NCHW input buffer
        bool _initialized;

        public DANetDepthProvider(DANetConfig cfg = null) => _cfg = cfg ?? new DANetConfig();

        public DepthModelInfo Info => new DepthModelInfo
        {
            id = _cfg.modelId,
            displayName = _cfg.displayName,
            execution = DepthExecution.OnDeviceUnity,
            output = _cfg.outputIsMetric ? DepthOutputKind.MetricDepth : DepthOutputKind.RelativeDepth,
            license = _cfg.license,
            onDeviceViable = true,
            notes = "Inference Engine (ONNX). Primary geometry source on non-LiDAR walls.",
        };

        public void Initialize()
        {
            if (_initialized) return;

            var asset = Resources.Load<ModelAsset>(_cfg.resourcePath);
            if (asset == null)
            {
                Debug.LogError(
                    $"[OpenRoomPlan] DA-V2-S model not found at Resources/{_cfg.resourcePath}. " +
                    "Import the Depth-Anything-V2-Small .onnx into a Resources folder (see docs/Phase2_DepthProvider.md). " +
                    "Infer() will return Empty until then.");
                return;
            }

            _model = ModelLoader.Load(asset);
            _worker = new Worker(_model, _cfg.backend);
            _chw = new float[3 * _cfg.inputHeight * _cfg.inputWidth];
            _initialized = true;
        }

        public DepthFrameResult Infer(in DepthFrameInput input)
        {
            if (!_initialized) Initialize();
            if (_worker == null || input.rgb == null || input.width <= 0 || input.height <= 0)
                return DepthFrameResult.Empty;

            Preprocess(input.rgb, input.width, input.height);

            using var tensor = new Tensor<float>(
                new TensorShape(1, 3, _cfg.inputHeight, _cfg.inputWidth), _chw);
            _worker.Schedule(tensor);

            if (_worker.PeekOutput() is not Tensor<float> outT)
                return DepthFrameResult.Empty;

            // Output is [1,1,H,W] or [1,H,W]; take the trailing two dims as the depth grid.
            var shp = outT.shape;
            int r = shp.rank;
            int oh = r >= 2 ? shp[r - 2] : _cfg.inputHeight;
            int ow = r >= 1 ? shp[r - 1] : _cfg.inputWidth;
            float[] raw = outT.DownloadToArray();
            if (raw == null || raw.Length < ow * oh) return DepthFrameResult.Empty;

            // Keep raw values; ScaleAligner does the metric conversion in the correct space.
            // For a metric net, sanitize non-positive/garbage samples to NaN so backprojection skips them.
            var depth = new float[ow * oh];
            for (int i = 0; i < ow * oh; i++)
            {
                float v = raw[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { depth[i] = float.NaN; continue; }
                if (_cfg.outputIsMetric) depth[i] = v > 0f ? v : float.NaN;
                else depth[i] = v; // raw disparity/relative; aligned downstream
            }

            return new DepthFrameResult
            {
                width = ow,
                height = oh,
                depthMeters = depth,
                confidence = null,
                isMetric = _cfg.outputIsMetric,
                isInverseValues = !_cfg.outputIsMetric && _cfg.outputIsInverse,
                timestampNs = input.timestampNs,
            };
        }

        /// <summary>Bilinear resize RGB → normalized NCHW float buffer (channel-first, RGB order).</summary>
        void Preprocess(Color32[] rgb, int srcW, int srcH)
        {
            int dw = _cfg.inputWidth, dh = _cfg.inputHeight;
            int plane = dw * dh;
            float sx = srcW / (float)dw, sy = srcH / (float)dh;

            for (int y = 0; y < dh; y++)
            {
                float fy = (y + 0.5f) * sy - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, srcH - 1);
                int y1 = Mathf.Min(y0 + 1, srcH - 1);
                float wy = fy - y0;

                for (int x = 0; x < dw; x++)
                {
                    float fx = (x + 0.5f) * sx - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, srcW - 1);
                    int x1 = Mathf.Min(x0 + 1, srcW - 1);
                    float wx = fx - x0;

                    Color32 c00 = rgb[y0 * srcW + x0], c10 = rgb[y0 * srcW + x1];
                    Color32 c01 = rgb[y1 * srcW + x0], c11 = rgb[y1 * srcW + x1];

                    float rr = Bilerp(c00.r, c10.r, c01.r, c11.r, wx, wy) / 255f;
                    float gg = Bilerp(c00.g, c10.g, c01.g, c11.g, wx, wy) / 255f;
                    float bb = Bilerp(c00.b, c10.b, c01.b, c11.b, wx, wy) / 255f;

                    int p = y * dw + x;
                    _chw[0 * plane + p] = (rr - _cfg.mean.x) / _cfg.std.x;
                    _chw[1 * plane + p] = (gg - _cfg.mean.y) / _cfg.std.y;
                    _chw[2 * plane + p] = (bb - _cfg.mean.z) / _cfg.std.z;
                }
            }
        }

        static float Bilerp(byte a, byte b, byte c, byte d, float wx, float wy)
        {
            float top = a + (b - a) * wx;
            float bot = c + (d - c) * wx;
            return top + (bot - top) * wy;
        }

        public void Dispose()
        {
            _worker?.Dispose();
            _worker = null;
            _model = null;
            _initialized = false;
        }
    }
}
