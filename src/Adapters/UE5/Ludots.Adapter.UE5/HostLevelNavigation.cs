using System.Collections.Generic;

namespace Ludots.Adapter.UE5
{
    public enum HostLevelTransitionMode
    {
        None = 0,
        PreviewMod = 1,
        DirectOpenLevel = 2,
    }

    public enum HostLevelNavigationState
    {
        Idle = 0,
        Opening = 1,
        Active = 2,
        Returning = 3,
        Failed = 4,
    }

    public readonly record struct HostLevelNavigationSnapshot(
        HostLevelTransitionMode Mode,
        HostLevelNavigationState State,
        string RequestedLevelPath,
        string CurrentLevelPath,
        string CurrentWorldName,
        string LastError)
    {
        public static HostLevelNavigationSnapshot Empty { get; } = new(
            HostLevelTransitionMode.None,
            HostLevelNavigationState.Idle,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        public bool IsPreviewActive => Mode == HostLevelTransitionMode.PreviewMod && State == HostLevelNavigationState.Active;
    }

    public readonly record struct HostLevelLoadRequest(
        string SourceMapId,
        string LevelPath,
        HostLevelTransitionMode TransitionMode,
        bool UseStreaming,
        IReadOnlyList<string>? StreamingLevels,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public bool HasLevelPath => !string.IsNullOrWhiteSpace(LevelPath);
    }

    public readonly record struct HostLevelNavigationResult(
        bool Success,
        HostLevelNavigationSnapshot Snapshot,
        string ErrorMessage)
    {
        public static HostLevelNavigationResult Ok(HostLevelNavigationSnapshot snapshot)
            => new(true, snapshot, string.Empty);

        public static HostLevelNavigationResult Fail(HostLevelNavigationSnapshot snapshot, string errorMessage)
            => new(false, snapshot, errorMessage ?? string.Empty);
    }

    public interface IHostLevelNavigator
    {
        HostLevelNavigationSnapshot Snapshot { get; }

        HostLevelNavigationResult Load(in HostLevelLoadRequest request);

        HostLevelNavigationResult ExitPreview();
    }
}
