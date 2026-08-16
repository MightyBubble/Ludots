using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS
{
    public static class AbilityActivationPreconditionEvaluator
    {
        public static bool Evaluate(
            World world,
            Entity caster,
            Entity explicitTarget,
            IntVector2 targetPosCm,
            int abilityId,
            in AbilityActivationPrecondition precondition,
            GraphProgramRegistry graphPrograms,
            IGraphRuntimeApi graphApi)
        {
            if (precondition.ValidationGraphId <= 0)
            {
                return true;
            }

            if (graphPrograms == null || graphApi == null)
            {
                throw new InvalidOperationException(
                    $"Ability {abilityId} requires activation validation graphId={precondition.ValidationGraphId}, but graph validation services are not configured.");
            }

            if (!graphPrograms.TryGetProgram(precondition.ValidationGraphId, out var validationProgram))
            {
                throw new InvalidOperationException(
                    $"Ability {abilityId} references missing activation validation graphId={precondition.ValidationGraphId}.");
            }

            GraphKind kind = graphPrograms.RequireKind(precondition.ValidationGraphId, GraphKind.Validation);
            return GasGraphExecutor.ExecuteValidation(
                world,
                caster,
                explicitTarget,
                targetPosCm,
                validationProgram,
                graphApi,
                kind,
                programs: graphPrograms);
        }
    }
}
