using System;

namespace OpenRoomPlan.Core
{
    /// <summary>Level-A metrics: raw depth vs LiDAR GT (only where GT exists). See spec §6.</summary>
    [Serializable]
    public struct DepthAccuracyMetrics
    {
        public float absRel;        // mean(|d_est - d_gt| / d_gt)
        public float rmseMeters;    // sqrt(mean((d_est - d_gt)^2))
        public float delta1;        // % pixels with max(r, 1/r) < 1.25
        public float scaleFactorStdDev; // stability of per-frame VIO scale-align factor
        public int validPixelCount;
    }

    /// <summary>Level-B metrics: reconstructed room vs GT. These drive the Go/No-Go call (spec §2).</summary>
    [Serializable]
    public struct ReconstructionMetrics
    {
        public float medianCornerErrorMeters;
        public float meanCornerErrorMeters;
        public float meanWallAngleErrorDeg;
        public float meanWallOffsetMeters;
        public float floorplanIoU;          // 0..1
        public float dimensionErrorPct;     // mean |L_est - L_gt| / L_gt over L/W/H
        public float wallCompletenessPct;   // % GT wall area within tolerance of a reconstructed wall
        public bool producedValidRoom;      // closed rectilinear floorplan?
    }

    /// <summary>Cheap performance sanity captured alongside quality (spec §6).</summary>
    [Serializable]
    public struct PerfMetrics
    {
        public float inferMsMean;
        public float inferMsP95;
        public float peakRamMB;
        public float thermalDriftMsOver3Min; // latency growth as SoC heats; proxy for throttling
    }

    /// <summary>
    /// One row of the comparison matrix (spec §14) for a single model, averaged over the room set.
    /// Scores (0..5) are our synthesized judgement for readability alongside the raw numbers.
    /// </summary>
    [Serializable]
    public struct ModelScorecard
    {
        public string modelId;
        public DepthExecution execution;

        // Quality (aggregated across rooms)
        public ReconstructionMetrics reconstruction;
        public DepthAccuracyMetrics depthAccuracy;
        public float metricScaleErrorPct;
        public float temporalStability;   // lower = steadier across a video (scale drift proxy)

        // Practicality
        public PerfMetrics perf;
        public int scoreUnityIntegration; // 0..5
        public int scoreOnDeviceSuitability; // 0..5
        public int scoreCloudOfflineSuitability; // 0..5

        public string verdict;            // one-line role recommendation
    }
}
