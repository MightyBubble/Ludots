using Ludots.Core.Config;
using Ludots.Core.Map;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Engine
{
    public interface IMapLoadCompletionGate
    {
        IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request);
        IPendingMapLoad BeginPendingResume(in MapResumeCompletionRequest request);
    }

    public interface IPendingMapLoad
    {
        MapLoadCompletionResult Poll();
        void Cancel();
    }

    public readonly record struct MapLoadCompletionRequest(
        GameEngine Engine,
        MapId MapId,
        MapConfig MapConfig,
        MapSession Session,
        bool IsPush,
        MapPresentationAssetManifest? PresentationAssets = null);

    public readonly record struct MapResumeCompletionRequest(
        GameEngine Engine,
        MapSession ResumedSession,
        MapSession? ClosedSession,
        MapPresentationAssetManifest? PresentationAssets = null);

    public enum MapLoadCompletionState
    {
        Pending = 0,
        Ready = 1,
        Failed = 2,
    }

    public readonly record struct MapLoadCompletionResult(
        MapLoadCompletionState State,
        string ErrorMessage,
        int RequiredAssetCount = 0,
        int ResidentAssetCount = 0,
        int InFlightAssetCount = 0,
        int FailedAssetCount = 0)
    {
        public static MapLoadCompletionResult Pending(
            int requiredAssetCount = 0,
            int residentAssetCount = 0,
            int inFlightAssetCount = 0,
            int failedAssetCount = 0)
            => new(MapLoadCompletionState.Pending, string.Empty, requiredAssetCount, residentAssetCount, inFlightAssetCount, failedAssetCount);

        public static MapLoadCompletionResult Ready(
            int requiredAssetCount = 0,
            int residentAssetCount = 0,
            int inFlightAssetCount = 0,
            int failedAssetCount = 0)
            => new(MapLoadCompletionState.Ready, string.Empty, requiredAssetCount, residentAssetCount, inFlightAssetCount, failedAssetCount);

        public static MapLoadCompletionResult Failed(
            string errorMessage,
            int requiredAssetCount = 0,
            int residentAssetCount = 0,
            int inFlightAssetCount = 0,
            int failedAssetCount = 0)
            => new(MapLoadCompletionState.Failed, errorMessage ?? string.Empty, requiredAssetCount, residentAssetCount, inFlightAssetCount, failedAssetCount);
    }

    public readonly record struct MapLoadStatus(
        MapLoadCompletionState State,
        bool IsDeferred,
        string ErrorMessage,
        int RequiredAssetCount = 0,
        int ResidentAssetCount = 0,
        int InFlightAssetCount = 0,
        int FailedAssetCount = 0)
    {
        public bool IsCompleted => State != MapLoadCompletionState.Pending;
        public bool Succeeded => State == MapLoadCompletionState.Ready;
        public bool Failed => State == MapLoadCompletionState.Failed;

        public static MapLoadStatus ImmediateSuccess { get; } =
            new(MapLoadCompletionState.Ready, false, string.Empty);

        public static MapLoadStatus DeferredPending { get; } =
            new(MapLoadCompletionState.Pending, true, string.Empty);

        public static MapLoadStatus DeferredSuccess { get; } =
            new(MapLoadCompletionState.Ready, true, string.Empty);

        public static MapLoadStatus DeferredFailure(string errorMessage)
            => new(MapLoadCompletionState.Failed, true, errorMessage ?? string.Empty);

        public static MapLoadStatus FromCompletion(in MapLoadCompletionResult result, bool isDeferred)
        {
            if (result.State == MapLoadCompletionState.Ready)
            {
                return new MapLoadStatus(
                    MapLoadCompletionState.Ready,
                    isDeferred,
                    string.Empty,
                    result.RequiredAssetCount,
                    result.ResidentAssetCount,
                    result.InFlightAssetCount,
                    result.FailedAssetCount);
            }

            if (result.State == MapLoadCompletionState.Failed)
            {
                return new MapLoadStatus(
                    MapLoadCompletionState.Failed,
                    isDeferred,
                    result.ErrorMessage ?? string.Empty,
                    result.RequiredAssetCount,
                    result.ResidentAssetCount,
                    result.InFlightAssetCount,
                    result.FailedAssetCount);
            }

            return new MapLoadStatus(
                MapLoadCompletionState.Pending,
                isDeferred,
                string.Empty,
                result.RequiredAssetCount,
                result.ResidentAssetCount,
                result.InFlightAssetCount,
                result.FailedAssetCount);
        }
    }
}
