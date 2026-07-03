using System.Numerics;
using Newtonsoft.Json;

namespace Atlas2
{
    /// <summary>
    ///     Class ContentInfo.
    /// </summary>
    public sealed class ContentInfo
    {
        public string Label { get; set; }
        public string Abbrev { get; set; }
        public float[] BackgroundColor { get; set; }
        public float[] FontColor { get; set; }
        public bool IsFlag { get; set; } = false;
        public bool Show { get; set; } = true;

        [JsonIgnore]
        public Vector4 BgColor => BackgroundColor is { Length: 4 } ? new Vector4(BackgroundColor[0], BackgroundColor[1], BackgroundColor[2], BackgroundColor[3]) : Vector4.One;

        [JsonIgnore]
        public Vector4 FtColor => FontColor is { Length: 4 } ? new Vector4(FontColor[0], FontColor[1], FontColor[2], FontColor[3]) : Vector4.One;
    }
}