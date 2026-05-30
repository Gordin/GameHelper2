namespace PreloadAlert
{
    using System.Numerics;
    using Newtonsoft.Json;

    internal struct PreloadInfo
    {
        public string DisplayName;
        public Vector4 Color;
        public bool LogToDisk;
        public bool Enabled;

        [JsonIgnore]
        public int Priority;

        [JsonConstructor]
        public PreloadInfo(string name, Vector4 color, bool log, bool enabled)
        {
            this.DisplayName = name;
            this.Color = color;
            this.LogToDisk = log;
            this.Enabled = enabled;
            this.Priority = 0;
        }

        public PreloadInfo()
        {
            this.DisplayName = string.Empty;
            this.Color = Vector4.One;
            this.LogToDisk = false;
            this.Enabled = true;
            this.Priority = 0;
        }
    }
}
