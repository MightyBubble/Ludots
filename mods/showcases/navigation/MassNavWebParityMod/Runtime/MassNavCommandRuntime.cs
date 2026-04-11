using System;
using System.Numerics;
using Arch.Core;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavCommandRuntime
{
    private MassNavQueuedCommand[] _commands = new MassNavQueuedCommand[8];
    private Entity[] _selectionPayload = new Entity[128];
    private int _commandCount;
    private int _payloadCount;

    public int PendingCommandCount => _commandCount;

    public void Reset()
    {
        _commandCount = 0;
        _payloadCount = 0;
    }

    public bool EnqueueTeamMove(int teamId, Vector2 centerCm)
    {
        EnsureCommandCapacity(_commandCount + 1);
        _commands[_commandCount++] = new MassNavQueuedCommand
        {
            Kind = MassNavQueuedCommandKind.TeamMove,
            TeamId = teamId,
            DestinationX = centerCm.X,
            DestinationY = centerCm.Y,
        };
        return true;
    }

    public bool EnqueueSelectionMove(ReadOnlySpan<Entity> selected, Vector2 centerCm, MassNavFormationMode formationMode)
    {
        if (selected.Length <= 0)
        {
            return false;
        }

        int payloadStart = ReserveSelectionPayload(selected);
        EnsureCommandCapacity(_commandCount + 1);
        _commands[_commandCount++] = new MassNavQueuedCommand
        {
            Kind = MassNavQueuedCommandKind.SelectionMove,
            DestinationX = centerCm.X,
            DestinationY = centerCm.Y,
            FormationMode = formationMode,
            SelectionStart = payloadStart,
            SelectionLength = selected.Length,
        };
        return true;
    }

    public bool EnqueueSelectionRotate(ReadOnlySpan<Entity> selected, float deltaRadians)
    {
        if (selected.Length <= 0 || !(MathF.Abs(deltaRadians) > 1e-5f))
        {
            return false;
        }

        int payloadStart = ReserveSelectionPayload(selected);
        EnsureCommandCapacity(_commandCount + 1);
        _commands[_commandCount++] = new MassNavQueuedCommand
        {
            Kind = MassNavQueuedCommandKind.SelectionRotate,
            RotationDeltaRadians = deltaRadians,
            SelectionStart = payloadStart,
            SelectionLength = selected.Length,
        };
        return true;
    }

    public int ApplyPending(MassNavSimulationRuntime simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        int appliedCount = 0;
        for (int commandIndex = 0; commandIndex < _commandCount; commandIndex++)
        {
            ref readonly MassNavQueuedCommand command = ref _commands[commandIndex];
            switch (command.Kind)
            {
                case MassNavQueuedCommandKind.TeamMove:
                    simulation.WebParity.SetTeamTarget(command.TeamId, new Vector2(command.DestinationX, command.DestinationY));
                    simulation.MarkStructuralChange();
                    simulation.MarkFlowReconcile();
                    simulation.ObserveFlowFieldRebuild(simulation.WebParity.LastFlowFieldRebuildMs);
                    simulation.MarkCommandApply();
                    appliedCount++;
                    break;
                case MassNavQueuedCommandKind.SelectionMove:
                {
                    ReadOnlySpan<Entity> selected = _selectionPayload.AsSpan(command.SelectionStart, command.SelectionLength);
                    int assigned = simulation.NavGroupRuntime.IssueSelectionMoveCommand(
                        simulation.WebParity,
                        simulation.AgentState,
                        selected,
                        new Vector2(command.DestinationX, command.DestinationY),
                        command.FormationMode);
                    if (assigned > 0)
                    {
                        simulation.MarkStructuralChange();
                        simulation.MarkCommandApply();
                        appliedCount++;
                    }

                    break;
                }
                case MassNavQueuedCommandKind.SelectionRotate:
                {
                    ReadOnlySpan<Entity> selected = _selectionPayload.AsSpan(command.SelectionStart, command.SelectionLength);
                    simulation.NavGroupRuntime.RotateSelected(simulation.AgentState, selected, command.RotationDeltaRadians);
                    simulation.MarkCommandApply();
                    appliedCount++;
                    break;
                }
            }
        }

        Reset();
        return appliedCount;
    }

    private int ReserveSelectionPayload(ReadOnlySpan<Entity> selected)
    {
        int start = _payloadCount;
        int required = start + selected.Length;
        if (required > _selectionPayload.Length)
        {
            int nextLength = _selectionPayload.Length;
            while (nextLength < required)
            {
                nextLength *= 2;
            }

            Array.Resize(ref _selectionPayload, nextLength);
        }

        selected.CopyTo(_selectionPayload.AsSpan(start, selected.Length));
        _payloadCount = required;
        return start;
    }

    private void EnsureCommandCapacity(int required)
    {
        if (required <= _commands.Length)
        {
            return;
        }

        int nextLength = _commands.Length;
        while (nextLength < required)
        {
            nextLength *= 2;
        }

        Array.Resize(ref _commands, nextLength);
    }

    private enum MassNavQueuedCommandKind : byte
    {
        TeamMove = 0,
        SelectionMove = 1,
        SelectionRotate = 2,
    }

    private readonly struct MassNavQueuedCommand
    {
        public MassNavQueuedCommandKind Kind { get; init; }
        public int TeamId { get; init; }
        public float DestinationX { get; init; }
        public float DestinationY { get; init; }
        public float RotationDeltaRadians { get; init; }
        public MassNavFormationMode FormationMode { get; init; }
        public int SelectionStart { get; init; }
        public int SelectionLength { get; init; }
    }
}
