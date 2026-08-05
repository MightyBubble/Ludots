using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Orders
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TargetLayoutMode
    {
        None = 0,
        Grid = 1
    }

    public sealed class TargetLayoutProfileDefinition
    {
        public string Id { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TargetLayoutMode Mode { get; set; } = TargetLayoutMode.None;

        public int SpacingCm { get; set; }

        public List<string> OrderTypeKeys { get; set; } = new();
    }

    public sealed class TargetLayoutProfileConfig
    {
        public List<TargetLayoutProfileDefinition> TargetLayoutProfiles { get; set; } = null!;
    }
}
