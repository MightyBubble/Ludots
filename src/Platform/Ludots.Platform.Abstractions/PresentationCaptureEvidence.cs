namespace Ludots.Platform.Abstractions;

public sealed class PresentationCaptureEvidenceDocument
{
    public int SchemaVersion { get; init; } = 2;
    public string Milestone { get; init; } = string.Empty;
    public int MilestoneOrder { get; init; }
    public uint MilestoneRevision { get; init; }
    public int HostFrame { get; init; }
    public int CameraTargetXCm { get; init; }
    public int CameraTargetYCm { get; init; }
    public int ViewportWidthPx { get; init; }
    public int ViewportHeightPx { get; init; }
    public PresentationCaptureInstanceEvidence[] Instances { get; init; } =
        Array.Empty<PresentationCaptureInstanceEvidence>();
}

public sealed class PresentationCaptureInstanceEvidence
{
    public int OwnerStableId { get; init; }
    public int VisualStableId { get; init; }
    public int TemplateId { get; init; }
    public string Template { get; init; } = string.Empty;
    public int WorldXCm { get; init; }
    public int WorldYCm { get; init; }
    public float ScreenLeftPx { get; init; }
    public float ScreenTopPx { get; init; }
    public float ScreenRightPx { get; init; }
    public float ScreenBottomPx { get; init; }
    public float ShortEdgePx { get; init; }
    public float AreaPx2 { get; init; }
}
