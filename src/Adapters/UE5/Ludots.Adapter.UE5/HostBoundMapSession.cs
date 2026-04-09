using System;
using Ludots.Core.Engine;
using Ludots.Core.Map;

namespace Ludots.Adapter.UE5
{
    public readonly record struct HostBoundMapSessionSnapshot(
        string FocusedMapId,
        ExplicitHostMapBinding Binding,
        MapLoadStatus LoadStatus,
        bool HasPendingReturn,
        HostLevelNavigationSnapshot Navigation)
    {
        public static HostBoundMapSessionSnapshot Empty { get; } = new(
            string.Empty,
            ExplicitHostMapBinding.Empty,
            MapLoadStatus.ImmediateSuccess,
            false,
            HostLevelNavigationSnapshot.Empty);

        public bool HasExplicitBinding => Binding.HasBinding;
        public bool IsReady => LoadStatus.Succeeded;
        public bool IsPending => LoadStatus.State == MapLoadCompletionState.Pending;
    }

    public interface IHostBoundMapSessionService
    {
        HostBoundMapSessionSnapshot Snapshot { get; }
    }

    public sealed class UE5HostBoundMapSessionService : IHostBoundMapSessionService, IMapLoadCompletionGate, IFocusedMapLoadStateSink
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

        public void OnFocusedMapChanged(in FocusedMapLoadState state)
        {
            if (state.Session == null)
            {
                Update(HostBoundMapSessionSnapshot.Empty);
                return;
            }

            IExplicitHostMapBindingResolver? resolver = _resolverAccessor();
            if (resolver == null || !resolver.TryResolve(state.Session, out ExplicitHostMapBinding binding) || !binding.HasBinding)
            {
                Update(new HostBoundMapSessionSnapshot(
                    state.Session.MapId.Value,
                    ExplicitHostMapBinding.Empty,
                    state.LoadStatus,
                    state.HasPendingReturn,
                    HostLevelNavigationSnapshot.Empty));
                return;
            }

            HostLevelNavigationSnapshot navigation = _navigatorAccessor()?.Snapshot ?? HostLevelNavigationSnapshot.Empty;

            Update(new HostBoundMapSessionSnapshot(
                state.Session.MapId.Value,
                binding,
                state.LoadStatus,
                state.HasPendingReturn,
                navigation));
        }

        public IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request)
        {
            IExplicitHostMapBindingResolver? resolver = _resolverAccessor();
            if (resolver == null || !resolver.TryResolve(request.Session, out ExplicitHostMapBinding binding) || !binding.HasBinding)
            {
                return null;
            }

            if (request.IsPush && binding.TransitionMode != HostLevelTransitionMode.PreviewMod)
            {
                return new CompletedPendingMapLoad(MapLoadCompletionResult.Failed(
                    $"Nested host-bound map '{request.MapId.Value}' must use '{HostLevelTransitionMode.PreviewMod}' so pop can return through the formal host lifecycle."));
            }

            IHostLevelNavigator? navigator = _navigatorAccessor();
            if (navigator == null)
            {
                return new CompletedPendingMapLoad(MapLoadCompletionResult.Failed(
                    $"Explicit host-bound map '{request.MapId.Value}' requires '{nameof(IHostLevelNavigator)}'."));
            }

            HostLevelNavigationResult navigationResult = navigator.Load(new HostLevelLoadRequest(
                request.MapId.Value,
                binding.LevelPath,
                binding.TransitionMode,
                binding.UseStreaming,
                binding.StreamingLevels,
                binding.Metadata));

            MapLoadCompletionResult completion = ToCompletionResult(binding, navigationResult.Success, navigationResult.Snapshot, navigationResult.ErrorMessage);
            return completion.State == MapLoadCompletionState.Pending
                ? new PendingHostBoundMapLoad(navigator, binding)
                : new CompletedPendingMapLoad(completion);
        }

        public IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request)
        {
            MapSession? closedSession = request.ClosedSession;
            if (closedSession == null)
            {
                return null;
            }

            IExplicitHostMapBindingResolver? resolver = _resolverAccessor();
            if (resolver == null || !resolver.TryResolve(closedSession, out ExplicitHostMapBinding closedBinding) || closedBinding.TransitionMode != HostLevelTransitionMode.PreviewMod)
            {
                return null;
            }

            IHostLevelNavigator? navigator = _navigatorAccessor();
            if (navigator == null)
            {
                return new CompletedPendingMapLoad(MapLoadCompletionResult.Failed(
                    $"Resuming map '{request.ResumedSession.MapId.Value}' requires '{nameof(IHostLevelNavigator)}'."));
            }

            bool hasResumedBinding = resolver.TryResolve(request.ResumedSession, out ExplicitHostMapBinding resumedBinding) && resumedBinding.HasBinding;
            HostLevelNavigationSnapshot current = navigator.Snapshot;
            HostLevelNavigationResult navigationResult =
                current.IsPreviewActive || current.State == HostLevelNavigationState.Returning
                    ? navigator.ExitPreview()
                    : HostLevelNavigationResult.Ok(current);

            MapLoadCompletionResult completion = ToResumeCompletionResult(
                hasResumedBinding,
                resumedBinding,
                navigationResult.Success,
                navigationResult.Snapshot,
                navigationResult.ErrorMessage);
            return completion.State == MapLoadCompletionState.Pending
                ? new PendingHostBoundMapResume(navigator, hasResumedBinding, resumedBinding)
                : new CompletedPendingMapLoad(completion);
        }

        private void Update(HostBoundMapSessionSnapshot next)
        {
            _snapshot = next;
            _publishSnapshot(next);
        }

        private static MapLoadCompletionResult ToCompletionResult(
            ExplicitHostMapBinding binding,
            bool loadSucceeded,
            HostLevelNavigationSnapshot snapshot,
            string errorMessage)
        {
            if (!loadSucceeded || snapshot.State == HostLevelNavigationState.Failed)
            {
                string resolvedError = !string.IsNullOrWhiteSpace(errorMessage)
                    ? errorMessage
                    : snapshot.LastError;
                return MapLoadCompletionResult.Failed(string.IsNullOrWhiteSpace(resolvedError)
                    ? $"Host navigation failed for '{binding.LevelPath}'."
                    : resolvedError);
            }

            if (snapshot.State != HostLevelNavigationState.Active)
            {
                return MapLoadCompletionResult.Pending();
            }

            if (!MatchesBinding(binding, snapshot))
            {
                return MapLoadCompletionResult.Pending();
            }

            return MapLoadCompletionResult.Ready();
        }

        private static bool MatchesBinding(ExplicitHostMapBinding binding, HostLevelNavigationSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(binding.LevelPath) &&
                !string.Equals(binding.LevelPath, snapshot.CurrentLevelPath, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(binding.HostWorldName) &&
                !string.Equals(binding.HostWorldName, snapshot.CurrentWorldName, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static MapLoadCompletionResult ToResumeCompletionResult(
            bool hasResumedBinding,
            ExplicitHostMapBinding resumedBinding,
            bool navigationSucceeded,
            HostLevelNavigationSnapshot snapshot,
            string errorMessage)
        {
            if (!navigationSucceeded || snapshot.State == HostLevelNavigationState.Failed)
            {
                string resolvedError = !string.IsNullOrWhiteSpace(errorMessage)
                    ? errorMessage
                    : snapshot.LastError;
                return MapLoadCompletionResult.Failed(string.IsNullOrWhiteSpace(resolvedError)
                    ? "Host return failed."
                    : resolvedError);
            }

            if (hasResumedBinding)
            {
                return snapshot.State == HostLevelNavigationState.Active && MatchesBinding(resumedBinding, snapshot)
                    ? MapLoadCompletionResult.Ready()
                    : MapLoadCompletionResult.Pending();
            }

            return snapshot.IsPreviewActive || snapshot.State == HostLevelNavigationState.Returning
                ? MapLoadCompletionResult.Pending()
                : MapLoadCompletionResult.Ready();
        }

        private sealed class CompletedPendingMapLoad : IPendingMapLoad
        {
            private readonly MapLoadCompletionResult _result;

            public CompletedPendingMapLoad(MapLoadCompletionResult result)
            {
                _result = result;
            }

            public MapLoadCompletionResult Poll() => _result;

            public void Cancel()
            {
            }
        }

        private sealed class PendingHostBoundMapLoad : IPendingMapLoad
        {
            private readonly IHostLevelNavigator _navigator;
            private readonly ExplicitHostMapBinding _binding;

            public PendingHostBoundMapLoad(IHostLevelNavigator navigator, ExplicitHostMapBinding binding)
            {
                _navigator = navigator;
                _binding = binding;
            }

            public MapLoadCompletionResult Poll()
            {
                return ToCompletionResult(_binding, loadSucceeded: true, _navigator.Snapshot, string.Empty);
            }

            public void Cancel()
            {
                _navigator.CancelPendingLoad();
            }
        }

        private sealed class PendingHostBoundMapResume : IPendingMapLoad
        {
            private readonly IHostLevelNavigator _navigator;
            private readonly bool _hasResumedBinding;
            private readonly ExplicitHostMapBinding _resumedBinding;

            public PendingHostBoundMapResume(
                IHostLevelNavigator navigator,
                bool hasResumedBinding,
                ExplicitHostMapBinding resumedBinding)
            {
                _navigator = navigator;
                _hasResumedBinding = hasResumedBinding;
                _resumedBinding = resumedBinding;
            }

            public MapLoadCompletionResult Poll()
            {
                return ToResumeCompletionResult(
                    _hasResumedBinding,
                    _resumedBinding,
                    navigationSucceeded: true,
                    _navigator.Snapshot,
                    string.Empty);
            }

            public void Cancel()
            {
            }
        }
    }
}
