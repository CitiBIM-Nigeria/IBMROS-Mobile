using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenRoomPlan.Core
{
    /// <summary>Where a depth model actually executes.</summary>
    public enum DepthExecution
    {
        OnDeviceUnity,   // runs in Unity on the phone (Inference Engine / ONNX Runtime)
        OfflineDesktop,  // runs in an external desktop process; Unity reads precomputed EXRs
        Cloud            // runs server-side; results fetched/precomputed
    }

    /// <summary>What a model outputs, natively.</summary>
    public enum DepthOutputKind
    {
        RelativeDepth,   // affine-invariant / unknown scale (needs ScaleAligner)
        MetricDepth,     // absolute meters
        MetricPointMap   // per-pixel 3D points in meters (e.g. MoGe-2)
    }

    /// <summary>Static description of a benchmarked model. Drives the comparison matrix.</summary>
    [Serializable]
    public struct DepthModelInfo
    {
        public string id;                 // stable slug, e.g. "depth-anything-v2-small"
        public string displayName;
        public DepthExecution execution;
        public DepthOutputKind output;
        public string license;            // "Apache-2.0", "CC-BY-NC-4.0", ...
        public bool onDeviceViable;       // our judgement, for the matrix
        public string notes;
    }

    /// <summary>One frame fed to a model. Pixel buffers are borrowed; do not retain past Infer().</summary>
    public struct DepthFrameInput
    {
        public int frameIndex;            // index within its session (-1 for live/one-off)
        public int width;
        public int height;
        public Color32[] rgb;             // row-major, length = width*height
        public CameraIntrinsics intrinsics;
        public Pose cameraPose;
        public long timestampNs;

        // Sparse metric anchors (VIO points or confident platform-depth samples) for scale alignment.
        public IReadOnlyList<Vector3> metricAnchorsWorld;
    }

    /// <summary>A model's per-frame result, normalized so the pipeline treats every model identically.</summary>
    public struct DepthFrameResult
    {
        public int width;
        public int height;
        public float[] depthMeters;       // row-major; NaN = invalid pixel
        public float[] confidence;        // optional, 0..1, null if unavailable
        public bool isMetric;             // false => ScaleAligner must run before fusion
        // When !isMetric, whether the raw values are inverse (disparity-like, larger = nearer) rather than
        // depth-like. DA-V2's affine-invariant output is disparity, so alignment must happen in that space
        // (fit a*disp+b ≈ 1/metric, then depth = 1/(a*disp+b)). Ignored when isMetric is true.
        public bool isInverseValues;
        public long timestampNs;

        public static DepthFrameResult Empty => new DepthFrameResult { depthMeters = null };
        public bool HasData => depthMeters != null && depthMeters.Length == width * height;
    }

    /// <summary>
    /// The single plugin contract for every depth model, on-device or offline.
    /// Add a new model = implement this + register it. Nothing else in the benchmark changes.
    /// </summary>
    public interface IDepthModel : IDisposable
    {
        DepthModelInfo Info { get; }
        void Initialize();
        DepthFrameResult Infer(in DepthFrameInput input);
    }

    /// <summary>
    /// Discovery point for models. Register a factory once; the benchmark iterates the registry
    /// × recorded sessions through one shared reconstruction + eval pipeline.
    /// </summary>
    public static class DepthModelRegistry
    {
        private static readonly Dictionary<string, Func<IDepthModel>> Factories = new();

        public static void Register(string id, Func<IDepthModel> factory)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("model id required");
            Factories[id] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static bool TryCreate(string id, out IDepthModel model)
        {
            model = null;
            if (!Factories.TryGetValue(id, out var f)) return false;
            model = f();
            return true;
        }

        public static IEnumerable<string> RegisteredIds => Factories.Keys;
    }
}
