using System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    /// <summary>
    /// Networking-owned admission stages. Core GAS keeps only GlobalIntake/EntityIntake;
    /// wire protocol and HUD consume these projected stages.
    /// </summary>
    public enum NetworkCommandAdmissionStage : byte
    {
        NetworkIntake = 0,
        GlobalIntake = 1,
        EntityIntake = 2,
        Terminal = 3,
    }

    /// <summary>
    /// Networking-owned admission/terminal codes for wire protocol and client feedback.
    /// Projected Core submit results keep the same numeric values as <see cref="OrderSubmitResult"/>.
    /// Network validation, schedule, and terminal outcomes occupy the remaining dense range.
    /// </summary>
    public enum NetworkCommandAdmissionCode : byte
    {
        Activated = 0,
        Queued = 1,
        Pending = 2,
        RejectedQueueFull = 3,
        RejectedByRule = 4,
        RejectedValidation = 5,
        RejectedInvalidActor = 6,
        RejectedInvalidOrderType = 7,
        RejectedBlackboardCapacity = 8,
        RejectedMissingBlackboard = 9,
        RejectedAdmissionCapacity = 10,
        NetworkRateLimited = 11,
        NetworkTargetTickExpired = 12,
        NetworkTargetTickTooFarAhead = 13,
        NetworkActorLimitExceeded = 14,
        NetworkAdmissionBackpressured = 15,
        NetworkInvalidConnectionSeat = 16,
        NetworkSequenceGap = 17,
        NetworkSequenceOutsideHistory = 18,
        NetworkScheduled = 19,
        NetworkScheduleFull = 20,
        NetworkInvalidActorHandle = 21,
        NetworkStaleActorGeneration = 22,
        NetworkActorNotControlled = 23,
        NetworkInvalidTargetHandle = 24,
        NetworkStaleTargetGeneration = 25,
        NetworkTargetNotKnown = 26,
        NetworkCommandSchemaMismatch = 27,
        NetworkMatchNotStarted = 28,
        NetworkMatchCompleted = 29,
        NetworkSequenceExhausted = 30,
        TerminalCompleted = 31,
        TerminalFailed = 32,
        TerminalCancelled = 33,
    }

    public static class NetworkCommandAdmissionCodeSemantics
    {
        public const int CodeCount = (int)NetworkCommandAdmissionCode.TerminalCancelled + 1;

        public static bool IsKnown(NetworkCommandAdmissionCode code) =>
            (uint)code < (uint)CodeCount;

        public static bool IsKnown(NetworkCommandAdmissionStage stage) =>
            stage is NetworkCommandAdmissionStage.NetworkIntake
                or NetworkCommandAdmissionStage.GlobalIntake
                or NetworkCommandAdmissionStage.EntityIntake
                or NetworkCommandAdmissionStage.Terminal;

        public static bool IsAcceptedProgress(NetworkCommandAdmissionCode code) =>
            code switch
            {
                NetworkCommandAdmissionCode.Activated or
                NetworkCommandAdmissionCode.Queued or
                NetworkCommandAdmissionCode.Pending or
                NetworkCommandAdmissionCode.NetworkScheduled or
                NetworkCommandAdmissionCode.TerminalCompleted => true,
                NetworkCommandAdmissionCode.RejectedQueueFull or
                NetworkCommandAdmissionCode.RejectedByRule or
                NetworkCommandAdmissionCode.RejectedValidation or
                NetworkCommandAdmissionCode.RejectedInvalidActor or
                NetworkCommandAdmissionCode.RejectedInvalidOrderType or
                NetworkCommandAdmissionCode.RejectedBlackboardCapacity or
                NetworkCommandAdmissionCode.RejectedMissingBlackboard or
                NetworkCommandAdmissionCode.RejectedAdmissionCapacity or
                NetworkCommandAdmissionCode.NetworkRateLimited or
                NetworkCommandAdmissionCode.NetworkTargetTickExpired or
                NetworkCommandAdmissionCode.NetworkTargetTickTooFarAhead or
                NetworkCommandAdmissionCode.NetworkActorLimitExceeded or
                NetworkCommandAdmissionCode.NetworkAdmissionBackpressured or
                NetworkCommandAdmissionCode.NetworkInvalidConnectionSeat or
                NetworkCommandAdmissionCode.NetworkSequenceGap or
                NetworkCommandAdmissionCode.NetworkSequenceOutsideHistory or
                NetworkCommandAdmissionCode.NetworkScheduleFull or
                NetworkCommandAdmissionCode.NetworkInvalidActorHandle or
                NetworkCommandAdmissionCode.NetworkStaleActorGeneration or
                NetworkCommandAdmissionCode.NetworkActorNotControlled or
                NetworkCommandAdmissionCode.NetworkInvalidTargetHandle or
                NetworkCommandAdmissionCode.NetworkStaleTargetGeneration or
                NetworkCommandAdmissionCode.NetworkTargetNotKnown or
                NetworkCommandAdmissionCode.NetworkCommandSchemaMismatch or
                NetworkCommandAdmissionCode.NetworkMatchNotStarted or
                NetworkCommandAdmissionCode.NetworkMatchCompleted or
                NetworkCommandAdmissionCode.NetworkSequenceExhausted or
                NetworkCommandAdmissionCode.TerminalFailed or
                NetworkCommandAdmissionCode.TerminalCancelled => false,
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown network command admission code."),
            };

        public static bool IsRejection(NetworkCommandAdmissionCode code) =>
            code switch
            {
                NetworkCommandAdmissionCode.Activated or
                NetworkCommandAdmissionCode.Queued or
                NetworkCommandAdmissionCode.Pending or
                NetworkCommandAdmissionCode.NetworkScheduled or
                NetworkCommandAdmissionCode.TerminalCompleted or
                NetworkCommandAdmissionCode.TerminalFailed or
                NetworkCommandAdmissionCode.TerminalCancelled => false,
                NetworkCommandAdmissionCode.RejectedQueueFull or
                NetworkCommandAdmissionCode.RejectedByRule or
                NetworkCommandAdmissionCode.RejectedValidation or
                NetworkCommandAdmissionCode.RejectedInvalidActor or
                NetworkCommandAdmissionCode.RejectedInvalidOrderType or
                NetworkCommandAdmissionCode.RejectedBlackboardCapacity or
                NetworkCommandAdmissionCode.RejectedMissingBlackboard or
                NetworkCommandAdmissionCode.RejectedAdmissionCapacity or
                NetworkCommandAdmissionCode.NetworkRateLimited or
                NetworkCommandAdmissionCode.NetworkTargetTickExpired or
                NetworkCommandAdmissionCode.NetworkTargetTickTooFarAhead or
                NetworkCommandAdmissionCode.NetworkActorLimitExceeded or
                NetworkCommandAdmissionCode.NetworkAdmissionBackpressured or
                NetworkCommandAdmissionCode.NetworkInvalidConnectionSeat or
                NetworkCommandAdmissionCode.NetworkSequenceGap or
                NetworkCommandAdmissionCode.NetworkSequenceOutsideHistory or
                NetworkCommandAdmissionCode.NetworkScheduleFull or
                NetworkCommandAdmissionCode.NetworkInvalidActorHandle or
                NetworkCommandAdmissionCode.NetworkStaleActorGeneration or
                NetworkCommandAdmissionCode.NetworkActorNotControlled or
                NetworkCommandAdmissionCode.NetworkInvalidTargetHandle or
                NetworkCommandAdmissionCode.NetworkStaleTargetGeneration or
                NetworkCommandAdmissionCode.NetworkTargetNotKnown or
                NetworkCommandAdmissionCode.NetworkCommandSchemaMismatch or
                NetworkCommandAdmissionCode.NetworkMatchNotStarted or
                NetworkCommandAdmissionCode.NetworkMatchCompleted or
                NetworkCommandAdmissionCode.NetworkSequenceExhausted => true,
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown network command admission code."),
            };

        public static bool IsValidStageCode(
            NetworkCommandAdmissionStage stage,
            NetworkCommandAdmissionCode code)
        {
            if (!IsKnown(stage) || !IsKnown(code))
            {
                return false;
            }

            return stage switch
            {
                NetworkCommandAdmissionStage.NetworkIntake =>
                    code is >= NetworkCommandAdmissionCode.NetworkRateLimited and
                        <= NetworkCommandAdmissionCode.NetworkSequenceExhausted,
                NetworkCommandAdmissionStage.GlobalIntake =>
                    code is NetworkCommandAdmissionCode.Queued or
                        NetworkCommandAdmissionCode.RejectedQueueFull or
                        NetworkCommandAdmissionCode.RejectedAdmissionCapacity,
                NetworkCommandAdmissionStage.EntityIntake =>
                    code is >= NetworkCommandAdmissionCode.Activated and
                        <= NetworkCommandAdmissionCode.RejectedAdmissionCapacity,
                NetworkCommandAdmissionStage.Terminal =>
                    code is NetworkCommandAdmissionCode.TerminalCompleted or
                        NetworkCommandAdmissionCode.TerminalFailed or
                        NetworkCommandAdmissionCode.TerminalCancelled,
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown network command admission stage."),
            };
        }

        public static NetworkCommandAdmissionCode ProjectCoreSubmitResult(OrderSubmitResult result) =>
            result switch
            {
                OrderSubmitResult.Activated => NetworkCommandAdmissionCode.Activated,
                OrderSubmitResult.Queued => NetworkCommandAdmissionCode.Queued,
                OrderSubmitResult.Pending => NetworkCommandAdmissionCode.Pending,
                OrderSubmitResult.RejectedQueueFull => NetworkCommandAdmissionCode.RejectedQueueFull,
                OrderSubmitResult.RejectedByRule => NetworkCommandAdmissionCode.RejectedByRule,
                OrderSubmitResult.RejectedValidation => NetworkCommandAdmissionCode.RejectedValidation,
                OrderSubmitResult.RejectedInvalidActor => NetworkCommandAdmissionCode.RejectedInvalidActor,
                OrderSubmitResult.RejectedInvalidOrderType => NetworkCommandAdmissionCode.RejectedInvalidOrderType,
                OrderSubmitResult.RejectedBlackboardCapacity => NetworkCommandAdmissionCode.RejectedBlackboardCapacity,
                OrderSubmitResult.RejectedMissingBlackboard => NetworkCommandAdmissionCode.RejectedMissingBlackboard,
                OrderSubmitResult.RejectedAdmissionCapacity => NetworkCommandAdmissionCode.RejectedAdmissionCapacity,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown order submit result."),
            };

        public static NetworkCommandAdmissionStage ProjectCoreAdmissionStage(OrderAdmissionStage stage) =>
            stage switch
            {
                OrderAdmissionStage.GlobalIntake => NetworkCommandAdmissionStage.GlobalIntake,
                OrderAdmissionStage.EntityIntake => NetworkCommandAdmissionStage.EntityIntake,
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown order admission stage."),
            };

        public static NetworkCommandAdmissionCode ProjectTerminal(OrderTerminalState state) =>
            state switch
            {
                OrderTerminalState.Completed => NetworkCommandAdmissionCode.TerminalCompleted,
                OrderTerminalState.Failed => NetworkCommandAdmissionCode.TerminalFailed,
                OrderTerminalState.Cancelled => NetworkCommandAdmissionCode.TerminalCancelled,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown order terminal state."),
            };

        public static NetworkCommandAdmissionStage DeriveStage(NetworkCommandAdmissionCode code) =>
            code switch
            {
                NetworkCommandAdmissionCode.Queued or NetworkCommandAdmissionCode.RejectedQueueFull
                    or NetworkCommandAdmissionCode.RejectedAdmissionCapacity =>
                    NetworkCommandAdmissionStage.GlobalIntake,
                NetworkCommandAdmissionCode.Activated or NetworkCommandAdmissionCode.Pending
                    or NetworkCommandAdmissionCode.RejectedByRule or NetworkCommandAdmissionCode.RejectedValidation
                    or NetworkCommandAdmissionCode.RejectedInvalidActor or NetworkCommandAdmissionCode.RejectedInvalidOrderType
                    or NetworkCommandAdmissionCode.RejectedBlackboardCapacity
                    or NetworkCommandAdmissionCode.RejectedMissingBlackboard =>
                    NetworkCommandAdmissionStage.EntityIntake,
                NetworkCommandAdmissionCode.TerminalCompleted or NetworkCommandAdmissionCode.TerminalFailed
                    or NetworkCommandAdmissionCode.TerminalCancelled =>
                    NetworkCommandAdmissionStage.Terminal,
                NetworkCommandAdmissionCode.NetworkScheduled or NetworkCommandAdmissionCode.NetworkRateLimited
                    or NetworkCommandAdmissionCode.NetworkTargetTickExpired
                    or NetworkCommandAdmissionCode.NetworkTargetTickTooFarAhead
                    or NetworkCommandAdmissionCode.NetworkActorLimitExceeded
                    or NetworkCommandAdmissionCode.NetworkAdmissionBackpressured
                    or NetworkCommandAdmissionCode.NetworkInvalidConnectionSeat
                    or NetworkCommandAdmissionCode.NetworkSequenceGap
                    or NetworkCommandAdmissionCode.NetworkSequenceOutsideHistory
                    or NetworkCommandAdmissionCode.NetworkScheduleFull
                    or NetworkCommandAdmissionCode.NetworkInvalidActorHandle
                    or NetworkCommandAdmissionCode.NetworkStaleActorGeneration
                    or NetworkCommandAdmissionCode.NetworkActorNotControlled
                    or NetworkCommandAdmissionCode.NetworkInvalidTargetHandle
                    or NetworkCommandAdmissionCode.NetworkStaleTargetGeneration
                    or NetworkCommandAdmissionCode.NetworkTargetNotKnown
                    or NetworkCommandAdmissionCode.NetworkCommandSchemaMismatch
                    or NetworkCommandAdmissionCode.NetworkMatchNotStarted
                    or NetworkCommandAdmissionCode.NetworkMatchCompleted
                    or NetworkCommandAdmissionCode.NetworkSequenceExhausted =>
                    NetworkCommandAdmissionStage.NetworkIntake,
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown network command admission code."),
            };
    }
}
