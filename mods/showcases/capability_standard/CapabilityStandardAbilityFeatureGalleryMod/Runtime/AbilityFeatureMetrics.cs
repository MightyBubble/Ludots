namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureMetrics
{
    public string ShowcaseId { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Frame { get; set; }
    public int EventCount { get; set; }
    public bool WaitedForGate { get; set; }
    public bool Interrupted { get; set; }
    public bool TriggerGraphFired { get; set; }
    public string FirstCast { get; set; } = "";
    public string SecondCast { get; set; } = "";
    public int VisibleBeforeCount { get; set; }
    public int VisibleAfterCount { get; set; }
    public string Slot0After { get; set; } = "";
    public float CasterBefore { get; set; }
    public float CasterAfter { get; set; }
    public float TargetBefore { get; set; }
    public float TargetAfter { get; set; }
    public float Target2Before { get; set; }
    public float Target2After { get; set; }
    public float WoundedBefore { get; set; }
    public float WoundedAfter { get; set; }
}
