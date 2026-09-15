namespace Ludots.Core.Gameplay.GraphBrains;

/// <summary>
/// Pause gate for order-driven behavior hosts: hosts skip their update entirely
/// while gameplay cannot advance (pre-match, spectator, network stalls).
/// </summary>
public interface IGameplayActionLoopGate
{
    bool CanAdvanceGameplay { get; }
}
