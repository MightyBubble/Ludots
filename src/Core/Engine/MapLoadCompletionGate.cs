using Ludots.Core.Config;
using Ludots.Core.Map;

namespace Ludots.Core.Engine
{
    public interface IMapLoadCompletionGate
    {
        IPendingMapLoad BeginPendingLoad(in MapLoadCompletionRequest request);
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
        bool IsPush);

    public enum MapLoadCompletionState
    {
        Pending = 0,
        Ready = 1,
        Failed = 2,
    }

    public readonly record struct MapLoadCompletionResult(
        MapLoadCompletionState State,
        string ErrorMessage)
    {
        public static MapLoadCompletionResult Pending()
            => new(MapLoadCompletionState.Pending, string.Empty);

        public static MapLoadCompletionResult Ready()
            => new(MapLoadCompletionState.Ready, string.Empty);

        public static MapLoadCompletionResult Failed(string errorMessage)
            => new(MapLoadCompletionState.Failed, errorMessage ?? string.Empty);
    }

    public readonly record struct MapLoadStatus(
        MapLoadCompletionState State,
        bool IsDeferred,
        string ErrorMessage)
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
                return isDeferred ? DeferredSuccess : ImmediateSuccess;
            }

            if (result.State == MapLoadCompletionState.Failed)
            {
                return isDeferred
                    ? DeferredFailure(result.ErrorMessage)
                    : new MapLoadStatus(MapLoadCompletionState.Failed, false, result.ErrorMessage ?? string.Empty);
            }

            return new MapLoadStatus(MapLoadCompletionState.Pending, isDeferred, string.Empty);
        }
    }
}
