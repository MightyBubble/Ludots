using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Identity of one active story presentation stream (dialogue or sequence).
    /// Frontend may hold this handle; content is always the string bag on <see cref="StoryPresentationFrame"/>.
    /// </summary>
    public readonly record struct StoryPresentationStreamHandle(string StreamId, uint Generation)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(StreamId) && Generation != 0;
    }

    public enum StoryPresentationStreamKind : byte
    {
        Dialogue = 0,
        Sequence = 1,
    }

    /// <summary>
    /// One choice row for the frontend — labels only. Graph/next stay in DialogueRuntime.
    /// </summary>
    public sealed record StoryPresentationChoice(
        string ChoiceId,
        string Text,
        string Shortcut);

    /// <summary>
    /// One screen surface as strings + imageId. No absolute paths, no Graph ids.
    /// </summary>
    public sealed record StoryPresentationSurface(
        string SurfaceKey,
        string SurfaceKind,
        string Anchor,
        string Title,
        string Body = "",
        string Subtitle = "",
        string Footer = "",
        string ImageId = "",
        float ImageSize = 96f,
        float Width = 720f,
        float OffsetX = 0f,
        float OffsetY = 0f,
        int ZIndex = 40,
        bool WaitForInput = false,
        bool Skippable = false,
        bool DimBackdrop = false,
        float Progress01 = -1f,
        float CountdownSeconds = 0f,
        string AccentHex = "",
        string BackgroundHex = "",
        string BorderHex = "",
        string ForegroundHex = "",
        string MutedHex = "",
        IReadOnlyList<StoryPresentationChoice>? Choices = null,
        IReadOnlyList<Ludots.Core.Presentation.Hud.PresentationTextRun>? BodyRuns = null);

    /// <summary>
    /// Presentation frame published to the story frontend. Content is strings + imageIds only.
    /// </summary>
    public sealed record StoryPresentationFrame(
        StoryPresentationStreamHandle Handle,
        StoryPresentationStreamKind StreamKind,
        string StreamId,
        string BackdropHex,
        IReadOnlyList<StoryPresentationSurface> Surfaces)
    {
        public static StoryPresentationFrame Empty { get; } = new(
            default,
            StoryPresentationStreamKind.Dialogue,
            string.Empty,
            string.Empty,
            Array.Empty<StoryPresentationSurface>());
    }
}
