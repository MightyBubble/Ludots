using System;

namespace Ludots.Core.Physics3DNet;

/// <summary>
/// Injected Character/Vehicle local simulation used only for correction replay.
/// Does not imply full-world Bepu rollback.
/// </summary>
public interface IPhysics3DNetLocalDrivenSimulationPort
{
    Physics3DNetLocalDrivenKind Kind { get; }

    void RestoreLocalDrivenState(in Physics3DNetPredictedPose authoritativePoseAtConfirmedTick);

    Physics3DNetPredictedPose SimulateLocalDrivenTick(long tick, in Physics3DNetInputFrameView input);
}

public readonly struct Physics3DNetLocalCorrectionResult
{
    public Physics3DNetLocalCorrectionResult(
        long authoritativeConfirmedTick,
        long replayedFromTickInclusive,
        long replayedToTickInclusive,
        int replayedFrameCount,
        in Physics3DNetPredictedPose finalPose)
    {
        AuthoritativeConfirmedTick = authoritativeConfirmedTick;
        ReplayedFromTickInclusive = replayedFromTickInclusive;
        ReplayedToTickInclusive = replayedToTickInclusive;
        ReplayedFrameCount = replayedFrameCount;
        FinalPose = finalPose;
    }

    public long AuthoritativeConfirmedTick { get; }
    public long ReplayedFromTickInclusive { get; }
    public long ReplayedToTickInclusive { get; }
    public int ReplayedFrameCount { get; }
    public Physics3DNetPredictedPose FinalPose { get; }
}

/// <summary>
/// Coordinates local Character/Vehicle correction: restore authoritative pose at confirmed tick,
/// then replay retained inputs through an injected simulation port.
/// Rejects insufficient history and kind mismatch explicitly. Not full-world rollback.
/// </summary>
public sealed class Physics3DNetLocalCorrectionCoordinator
{
    public Physics3DNetLocalCorrectionResult Correct(
        Physics3DNetLocalPredictionHistory history,
        IPhysics3DNetLocalDrivenSimulationPort simulation,
        in Physics3DNetPredictedPose authoritativePoseAtConfirmedTick,
        int networkEntityId,
        int generation,
        long authoritativeConfirmedTick,
        Span<Physics3DNetPredictedPose> poseScratch,
        Span<Physics3DNetInputFrameView> inputScratch)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(simulation);

        if (!history.IsBound)
        {
            throw new InvalidOperationException("Local prediction history is not bound.");
        }

        if (history.BoundKind != simulation.Kind)
        {
            throw new InvalidOperationException(
                $"Local correction kind mismatch: history bound as {history.BoundKind}, simulation port is {simulation.Kind}.");
        }

        if (authoritativePoseAtConfirmedTick.Tick != authoritativeConfirmedTick)
        {
            throw new InvalidOperationException(
                $"Authoritative pose tick {authoritativePoseAtConfirmedTick.Tick} must equal confirmed tick {authoritativeConfirmedTick}.");
        }

        history.RejectRemoteOrWorldRollback(networkEntityId, generation);

        Physics3DNetCorrectionReplayRange range = history.BeginCorrectionReplay(
            networkEntityId,
            generation,
            authoritativeConfirmedTick,
            poseScratch,
            inputScratch);

        simulation.RestoreLocalDrivenState(authoritativePoseAtConfirmedTick);

        Physics3DNetPredictedPose finalPose = authoritativePoseAtConfirmedTick;
        for (int i = 0; i < range.FrameCount; i++)
        {
            Physics3DNetInputFrameView input = inputScratch[i];
            if (input.Tick != range.FromTickInclusive + i)
            {
                throw new InvalidOperationException(
                    $"Correction replay input tick {input.Tick} is not contiguous at index {i}.");
            }

            finalPose = simulation.SimulateLocalDrivenTick(input.Tick, input);
            if (finalPose.Tick != input.Tick)
            {
                throw new InvalidOperationException(
                    $"Simulation port returned pose tick {finalPose.Tick} for input tick {input.Tick}.");
            }

            history.OverwritePredictedPose(finalPose);
        }

        return new Physics3DNetLocalCorrectionResult(
            authoritativeConfirmedTick,
            range.FromTickInclusive,
            range.ToTickInclusive,
            range.FrameCount,
            finalPose);
    }
}

/// <summary>
/// Physics3DNet-owned Character correction adapter surface. Concrete Character3D wiring lives in owning module or tests.
/// </summary>
public sealed class Physics3DNetCharacterCorrectionAdapter : IPhysics3DNetLocalDrivenSimulationPort
{
    private readonly IPhysics3DNetLocalDrivenSimulationPort _inner;

    public Physics3DNetCharacterCorrectionAdapter(IPhysics3DNetLocalDrivenSimulationPort inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (inner.Kind != Physics3DNetLocalDrivenKind.Character)
        {
            throw new ArgumentException(
                $"Character correction adapter requires Kind=Character, got {inner.Kind}.",
                nameof(inner));
        }

        _inner = inner;
    }

    public Physics3DNetLocalDrivenKind Kind => Physics3DNetLocalDrivenKind.Character;

    public void RestoreLocalDrivenState(in Physics3DNetPredictedPose authoritativePoseAtConfirmedTick) =>
        _inner.RestoreLocalDrivenState(authoritativePoseAtConfirmedTick);

    public Physics3DNetPredictedPose SimulateLocalDrivenTick(long tick, in Physics3DNetInputFrameView input) =>
        _inner.SimulateLocalDrivenTick(tick, input);
}

/// <summary>
/// Physics3DNet-owned Vehicle correction adapter surface. Concrete Vehicle3D wiring lives in owning module or tests.
/// </summary>
public sealed class Physics3DNetVehicleCorrectionAdapter : IPhysics3DNetLocalDrivenSimulationPort
{
    private readonly IPhysics3DNetLocalDrivenSimulationPort _inner;

    public Physics3DNetVehicleCorrectionAdapter(IPhysics3DNetLocalDrivenSimulationPort inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (inner.Kind != Physics3DNetLocalDrivenKind.Vehicle)
        {
            throw new ArgumentException(
                $"Vehicle correction adapter requires Kind=Vehicle, got {inner.Kind}.",
                nameof(inner));
        }

        _inner = inner;
    }

    public Physics3DNetLocalDrivenKind Kind => Physics3DNetLocalDrivenKind.Vehicle;

    public void RestoreLocalDrivenState(in Physics3DNetPredictedPose authoritativePoseAtConfirmedTick) =>
        _inner.RestoreLocalDrivenState(authoritativePoseAtConfirmedTick);

    public Physics3DNetPredictedPose SimulateLocalDrivenTick(long tick, in Physics3DNetInputFrameView input) =>
        _inner.SimulateLocalDrivenTick(tick, input);
}
