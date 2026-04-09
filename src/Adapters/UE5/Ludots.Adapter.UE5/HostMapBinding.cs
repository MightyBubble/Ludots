using System.Collections.Generic;
using Ludots.Core.Map;

namespace Ludots.Adapter.UE5
{
    public readonly record struct ExplicitHostMapBinding(
        string HostWorldName,
        string LevelPath,
        HostLevelTransitionMode TransitionMode,
        bool UseStreaming,
        IReadOnlyList<string>? StreamingLevels,
        IReadOnlyDictionary<string, string>? Metadata)
    {
        public static ExplicitHostMapBinding Empty { get; } = new(
            string.Empty,
            string.Empty,
            HostLevelTransitionMode.None,
            false,
            null,
            null);

        public bool HasBinding =>
            !string.IsNullOrWhiteSpace(HostWorldName) ||
            !string.IsNullOrWhiteSpace(LevelPath);
    }

    public interface IExplicitHostMapBindingResolver
    {
        bool TryResolve(MapSession focusedSession, out ExplicitHostMapBinding binding);
    }
}
