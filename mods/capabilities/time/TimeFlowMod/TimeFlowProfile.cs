using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS;

namespace TimeFlowMod;

public sealed class TimeFlowProfile
{
    public required string Id { get; init; }
    public string Description { get; init; } = string.Empty;
    // Legacy alias retained for config compatibility. Prefer SimulationScalePermille.
    public float? GlobalTimeScale { get; init; }
    public int? SimulationScalePermille { get; init; }
    public int? GasScalePermille { get; init; }
    public int? Physics2DScalePermille { get; init; }
    public int? Navigation2DScalePermille { get; init; }
    public int? TasksScalePermille { get; init; }
    public SimulationLoopMode? LoopMode { get; init; }
    public GasStepMode? GasMode { get; init; }
    public int? GasStepEveryFixedTicks { get; init; }
    // Legacy absolute-rate aliases retained for config compatibility. Prefer *ScalePermille.
    public int? PhysicsTargetHz { get; init; }
    public int? PhysicsMaxStepsPerFixedTick { get; init; }
    public int? NavigationTargetHz { get; init; }
    public int? NavigationMaxStepsPerFixedTick { get; init; }
}
