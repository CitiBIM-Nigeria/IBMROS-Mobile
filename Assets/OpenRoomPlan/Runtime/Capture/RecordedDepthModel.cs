using System.IO;
using OpenRoomPlan.Core;

namespace OpenRoomPlan.Capture
{
    /// <summary>
    /// Serves depth that already exists on disk as an <see cref="IDepthModel"/>, so recorded platform
    /// depth AND offline/cloud model outputs plug into the SAME benchmark pipeline as the live on-device
    /// nets. modelId "platform" reads depth/; any other id reads models/&lt;id&gt;/ (written by the desktop
    /// offline runner, e.g. MoGe-2 / Depth Pro / Video-Depth-Anything).
    /// </summary>
    public sealed class RecordedDepthModel : IDepthModel
    {
        readonly string _sessionId;
        readonly string _modelId;
        readonly DepthModelInfo _info;

        public RecordedDepthModel(string sessionId, string modelId, DepthModelInfo info)
        {
            _sessionId = sessionId;
            _modelId = modelId;
            _info = info;
        }

        public DepthModelInfo Info => _info;
        public void Initialize() { }
        public void Dispose() { }

        public DepthFrameResult Infer(in DepthFrameInput input)
        {
            string path = _modelId == "platform"
                ? Path.Combine(SessionIO.SessionDir(_sessionId), "depth", $"{input.frameIndex:D6}.bin")
                : SessionIO.ModelDepthPath(_sessionId, _modelId, input.frameIndex);

            if (!File.Exists(path)) return DepthFrameResult.Empty;

            var depth = SessionIO.ReadDepthBin(path, out int w, out int h);
            return new DepthFrameResult
            {
                width = w,
                height = h,
                depthMeters = depth,
                confidence = null,
                isMetric = _info.output != DepthOutputKind.RelativeDepth,
                timestampNs = input.timestampNs,
            };
        }

        /// <summary>Convenience: the recorded ARCore/ARKit platform depth stream as a model.</summary>
        public static RecordedDepthModel Platform(string sessionId) => new(
            sessionId, "platform",
            new DepthModelInfo
            {
                id = "platform",
                displayName = "Platform depth (ARCore/ARKit)",
                execution = DepthExecution.OnDeviceUnity,
                output = DepthOutputKind.MetricDepth,
                license = "Platform",
                onDeviceViable = true,
                notes = "Motion-stereo / ToF; weak on textureless walls.",
            });
    }
}
