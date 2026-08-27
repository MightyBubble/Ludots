using System;
using System.Collections.Generic;

namespace NarrativeFrontendMod.Runtime;

public enum NarrativeFrontendAnchor
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    LeftCenter = 3,
    Center = 4,
    RightCenter = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
}

public enum NarrativeFrontendSurfaceKind
{
    ObjectiveTracker = 0,
    DialogueBubble = 1,
    OverlayDialogue = 2,
    SubtitleBubble = 3,
    ChoiceList = 4,
    NotificationStack = 5,
    HistoryJournal = 6,
    EventCard = 7,
    StatusPanel = 8,
    PromptRibbon = 9,
    ThreatBanner = 10,
    RelationshipNotebook = 11,
    InspectPanel = 12,
    FlowReview = 13,
    TransmissionOverlay = 14,
    StandingPortrait = 15,
}

public sealed record NarrativeFrontendSurfaceItem(
    string Label,
    string Value = "",
    string Caption = "",
    string AccentHex = "",
    bool Active = false,
    bool Muted = false,
    float Progress01 = -1f,
    string Shortcut = "");

public sealed record NarrativeFrontendSurfaceModel(
    string SurfaceId,
    NarrativeFrontendSurfaceKind Kind,
    NarrativeFrontendAnchor Anchor,
    string Title,
    string Subtitle = "",
    string Body = "",
    string Footer = "",
    IReadOnlyList<NarrativeFrontendSurfaceItem>? Items = null,
    float Width = 360f,
    float OffsetX = 0f,
    float OffsetY = 0f,
    int ZIndex = 40,
    bool Visible = true,
    bool DimBackdrop = false,
    bool WaitForInput = false,
    bool Skippable = false,
    float Progress01 = -1f,
    float CountdownSeconds = 0f,
    string AccentHex = "",
    string BackgroundHex = "",
    string BorderHex = "",
    string ForegroundHex = "",
    string MutedHex = "",
    string PortraitSrc = "",
    float PortraitSize = 96f,
    string FrameImageSrc = "");

public sealed record NarrativeFrontendPageState(
    string OwnerId,
    string Signature,
    bool Visible,
    string BackdropHex = "",
    IReadOnlyList<NarrativeFrontendSurfaceModel>? Surfaces = null);

public sealed record NarrativeFrontendRenderState(
    int Revision,
    bool HasVisibleContent,
    string BackdropHex,
    IReadOnlyList<NarrativeFrontendSurfaceModel> Surfaces)
{
    public static readonly NarrativeFrontendRenderState Empty =
        new(0, false, string.Empty, Array.Empty<NarrativeFrontendSurfaceModel>());
}
