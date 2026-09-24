using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.AI.Utility
{
    /// <summary>
    /// Resolves Utility actuator table indices through their compiled definitions before
    /// applying readiness gates to a GAS ability.
    /// </summary>
    public sealed class UtilityAiAbilityActuatorGate : IAbilityActivationActuatorGate
    {
        private readonly World _world;
        private readonly UtilityAiRuntimeSource _runtimeSource;

        public UtilityAiAbilityActuatorGate(World world, UtilityAiCompiledRuntime runtime)
            : this(world, new UtilityAiRuntimeSource(runtime))
        {
        }

        public UtilityAiAbilityActuatorGate(World world, UtilityAiRuntimeSource runtimeSource)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
        }

        public AbilityActivationRefusalReason Evaluate(Entity actor, int abilityId)
        {
            if (abilityId <= 0)
            {
                return AbilityActivationRefusalReason.None;
            }

            if (_world.Has<ActuatorReadiness>(actor))
            {
                ActuatorReadiness readiness = _world.Get<ActuatorReadiness>(actor);
                int mappedAbilityId = ResolveAbilityId(readiness.ActuatorId, nameof(ActuatorReadiness));
                if (mappedAbilityId == abilityId && readiness.Ready01 < 1f)
                {
                    return AbilityActivationRefusalReason.ActuatorNotReady;
                }
            }

            if (_world.Has<AimGate>(actor))
            {
                AimGate aimGate = _world.Get<AimGate>(actor);
                int mappedAbilityId = ResolveAbilityId(aimGate.ActuatorId, nameof(AimGate));
                if (mappedAbilityId == abilityId && aimGate.Ready01 < 1f)
                {
                    return AbilityActivationRefusalReason.AimGateNotReady;
                }
            }

            return AbilityActivationRefusalReason.None;
        }

        private int ResolveAbilityId(int actuatorId, string componentName)
        {
            UtilityAiCompiledRuntime runtime = _runtimeSource.Current;
            if ((uint)actuatorId >= (uint)runtime.Actuators.Length)
            {
                throw new InvalidOperationException(
                    $"{componentName} references Utility actuator index {actuatorId}, " +
                    $"but the compiled actuator table contains {runtime.Actuators.Length} entries.");
            }

            int abilityId = runtime.Actuators[actuatorId].AbilityId;
            if (abilityId <= 0)
            {
                throw new InvalidOperationException(
                    $"Utility actuator index {actuatorId} used by {componentName} has no compiled GAS ability mapping.");
            }

            return abilityId;
        }
    }
}
