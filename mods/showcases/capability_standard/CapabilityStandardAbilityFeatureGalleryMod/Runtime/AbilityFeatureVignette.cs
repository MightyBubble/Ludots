using System.Text.Json;

namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureVignette
{
    public string Feature { get; init; } = "";
    public string Family { get; init; } = "";
    public string Title { get; init; } = "";
    public string Beat { get; init; } = "";
    public string DetailTemplate { get; init; } = "";
    public string[] AssertDetailContains { get; init; } = [];
    public string AbilityId { get; init; } = "";
    public string[] ExtraActors { get; init; } = [];
    public bool NeedsProgression { get; init; }
    public string[] CasterAbilities { get; init; } = [];
    public string? FormSetId { get; init; }
    public AbilityFeatureScriptStep[] Script { get; init; } = [];
    public AbilityFeatureExpect Expect { get; init; } = new();

    public static AbilityFeatureVignette Load(string assetsRoot, string feature)
    {
        string path = Path.Combine(assetsRoot, "Vignettes", feature + ".json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Ability feature vignette missing: {path}");
        }

        AbilityFeatureVignette? loaded = JsonSerializer.Deserialize<AbilityFeatureVignette>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (loaded == null || !string.Equals(loaded.Feature, feature, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Vignette {path} feature must equal file stem '{feature}'.");
        }

        if (string.IsNullOrWhiteSpace(loaded.Title) || string.IsNullOrWhiteSpace(loaded.Beat))
        {
            throw new InvalidOperationException($"Vignette {feature} requires title and beat.");
        }

        if (loaded.Script.Length == 0)
        {
            throw new InvalidOperationException($"Vignette {feature} requires a script.");
        }

        return loaded;
    }
}

public sealed class AbilityFeatureScriptStep
{
    public int AtFrame { get; init; }
    public string Op { get; init; } = "";
    public int Slot { get; init; }
    public string? Target { get; init; }
    public string? SaveAs { get; init; }
    public string? Tag { get; init; }
    public string? Entity { get; init; }
    public string[] Targets { get; init; } = [];
    public string? CasterHasTag { get; init; }
    public string? TargetHasTag { get; init; }
}

public sealed class AbilityFeatureExpect
{
    public float? TargetHealthDelta { get; init; }
    public float? Target2HealthDelta { get; init; }
    public float? WoundedHealthDelta { get; init; }
    public float? CasterHealthDelta { get; init; }
    public float? TargetHealthMax { get; init; }
    public string? CasterHasTag { get; init; }
    public string? CasterLacksTag { get; init; }
    public string? TargetHasTag { get; init; }
    public string? TargetLacksTag { get; init; }
    public string? FirstCast { get; init; }
    public string? SecondCast { get; init; }
    public bool? WaitedForGate { get; init; }
    public bool? Interrupted { get; init; }
    public string? EventTag { get; init; }
    public int? EventCountMin { get; init; }
    public int? VisibleBeforeCount { get; init; }
    public int? VisibleAfterCount { get; init; }
    public string? Slot0After { get; init; }
    public bool? TriggerGraphFired { get; init; }
}
