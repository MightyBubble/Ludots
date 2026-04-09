using System;
using System.Collections.Generic;
using Ludots.Core.Map;

namespace Ludots.Adapter.UE5
{
    public readonly record struct HostBoundMapSessionSnapshot(
        string FocusedMapId,
        ExplicitHostMapBinding Binding,
        bool IsHostReady,
        bool HasPendingReturn,
        HostLevelNavigationSnapshot Navigation)
    {
        public static HostBoundMapSessionSnapshot Empty { get; } = new(
            string.Empty,
            ExplicitHostMapBinding.Empty,
            false,
            false,
            HostLevelNavigationSnapshot.Empty);

        public bool HasExplicitBinding => Binding.HasBinding;
    }

    public interface IHostBoundMapSessionService
    {
        HostBoundMapSessionSnapshot Snapshot { get; }

        HostBoundMapSessionSnapshot Reconcile(MapSession? focusedSession);

        HostBoundMapSessionSnapshot SetHostReady(bool isReady);

        HostBoundMapSessionSnapshot SetPendingReturn(bool hasPendingReturn);
    }

    public sealed class UE5HostBoundMapSessionService : IHostBoundMapSessionService
    {
        private readonly Func<IExplicitHostMapBindingResolver?> _resolverAccessor;
        private readonly Func<IHostLevelNavigator?> _navigatorAccessor;
        private readonly Action<HostBoundMapSessionSnapshot> _publishSnapshot;

        private HostBoundMapSessionSnapshot _snapshot;

        public UE5HostBoundMapSessionService(
            Func<IExplicitHostMapBindingResolver?> resolverAccessor,
            Func<IHostLevelNavigator?> navigatorAccessor,
            Action<HostBoundMapSessionSnapshot> publishSnapshot)
        {
            _resolverAccessor = resolverAccessor ?? throw new ArgumentNullException(nameof(resolverAccessor));
            _navigatorAccessor = navigatorAccessor ?? throw new ArgumentNullException(nameof(navigatorAccessor));
            _publishSnapshot = publishSnapshot ?? throw new ArgumentNullException(nameof(publishSnapshot));

            _snapshot = HostBoundMapSessionSnapshot.Empty;
            _publishSnapshot(_snapshot);
        }

        public HostBoundMapSessionSnapshot Snapshot => _snapshot;

        public HostBoundMapSessionSnapshot Reconcile(MapSession? focusedSession)
        {
            if (focusedSession == null)
            {
                return Update(HostBoundMapSessionSnapshot.Empty);
            }

            IExplicitHostMapBindingResolver? resolver = _resolverAccessor();
            if (resolver == null || !resolver.TryResolve(focusedSession, out ExplicitHostMapBinding binding) || !binding.HasBinding)
            {
                return Update(new HostBoundMapSessionSnapshot(
                    focusedSession.MapId.Value,
                    ExplicitHostMapBinding.Empty,
                    false,
                    false,
                    HostLevelNavigationSnapshot.Empty));
            }

            bool preserveFlags =
                string.Equals(_snapshot.FocusedMapId, focusedSession.MapId.Value, StringComparison.Ordinal) &&
                HasSameBinding(_snapshot.Binding, binding);

            HostLevelNavigationSnapshot navigation = _navigatorAccessor()?.Snapshot ?? HostLevelNavigationSnapshot.Empty;

            return Update(new HostBoundMapSessionSnapshot(
                focusedSession.MapId.Value,
                binding,
                preserveFlags && _snapshot.IsHostReady,
                preserveFlags && _snapshot.HasPendingReturn,
                navigation));
        }

        public HostBoundMapSessionSnapshot SetHostReady(bool isReady)
        {
            if (!_snapshot.HasExplicitBinding)
            {
                return _snapshot;
            }

            return Update(_snapshot with { IsHostReady = isReady });
        }

        public HostBoundMapSessionSnapshot SetPendingReturn(bool hasPendingReturn)
        {
            if (!_snapshot.HasExplicitBinding)
            {
                return _snapshot;
            }

            return Update(_snapshot with { HasPendingReturn = hasPendingReturn });
        }

        private HostBoundMapSessionSnapshot Update(HostBoundMapSessionSnapshot next)
        {
            _snapshot = next;
            _publishSnapshot(next);
            return next;
        }

        private static bool HasSameBinding(ExplicitHostMapBinding left, ExplicitHostMapBinding right)
        {
            return string.Equals(left.HostWorldName, right.HostWorldName, StringComparison.Ordinal) &&
                   string.Equals(left.LevelPath, right.LevelPath, StringComparison.Ordinal) &&
                   left.TransitionMode == right.TransitionMode &&
                   left.UseStreaming == right.UseStreaming &&
                   SequenceEquals(left.StreamingLevels, right.StreamingLevels) &&
                   DictionaryEquals(left.Metadata, right.Metadata);
        }

        private static bool SequenceEquals(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool DictionaryEquals(
            IReadOnlyDictionary<string, string>? left,
            IReadOnlyDictionary<string, string>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out string? value) ||
                    !string.Equals(pair.Value, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
