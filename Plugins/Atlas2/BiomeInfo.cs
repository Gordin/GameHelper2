using System.Numerics;
using Newtonsoft.Json;

namespace Atlas2
{
    public sealed class BiomeInfo
    {
        public string Label { get; set; }
        public float[] BorderColor { get; set; }
        public bool Show { get; set; } = true;

        [JsonIgnore]
        public Vector4 BdColor => BorderColor is { Length: 4 } ? new Vector4(BorderColor[0], BorderColor[1], BorderColor[2], BorderColor[3]) : Vector4.One;
    }
}
