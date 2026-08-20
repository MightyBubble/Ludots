using System.Text.Json.Serialization;

namespace RngCapabilityMod.Rng;

public sealed record DistributionConfig(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("stream")] string Stream,
    [property: JsonPropertyName("streamSeed")] uint? StreamSeed,
    [property: JsonPropertyName("entries")] DistributionEntryConfig[] Entries);

public sealed record DistributionEntryConfig(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("weight")] int Weight,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("locked")] bool Locked,
    [property: JsonPropertyName("modulation")] DistributionModulationConfig? Modulation);

public sealed record DistributionModulationConfig(
    [property: JsonPropertyName("minPermille")] int MinPermille,
    [property: JsonPropertyName("maxPermille")] int MaxPermille,
    [property: JsonPropertyName("invert")] bool Invert);
