using System;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;

namespace Ludots.Tests.Navigation2D
{
    internal static class Navigation2DTestContracts
    {
        public static Navigation2DConfig EnsureExplicitContracts(Navigation2DConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            config.Contracts ??= new Navigation2DContractsConfig();

            if (config.Contracts.NavProfiles.Count == 0)
            {
                config.Contracts.NavProfiles.Add(new Navigation2DNavProfileConfig
                {
                    Id = "test_nav_default",
                    MaxSpeedCmPerSec = 800,
                    MaxAccelCmPerSec2 = 6000,
                    RadiusCm = 40,
                    NeighborDistCm = 400,
                    TimeHorizonSec = 2f,
                    MaxNeighbors = 16,
                    GoalRadiusCm = 120,
                });
            }

            if (config.Contracts.CrowdProfiles.Count == 0)
            {
                config.Contracts.CrowdProfiles.Add(new Navigation2DCrowdProfileConfig
                {
                    Id = "test_crowd_default",
                    GeometryRadiusCm = 40,
                    NavMass = 1f,
                    YieldWeight = 1f,
                    PushClass = NavPushClass.Cooperative,
                    SolverPreference = NavSolverMode.Hybrid,
                    RetryLimit = 2,
                    TimeoutTicks = 90,
                    AbandonTicks = 240,
                });
            }

            if (config.Contracts.KnockbackPolicies.Count == 0)
            {
                config.Contracts.KnockbackPolicies.Add(new Navigation2DKnockbackPolicyConfig
                {
                    Id = "test_knockback_default",
                    OverrideTicks = 15,
                    ClearNavGoalWhileActive = true,
                });
            }

            if (config.Contracts.GroupSolver.Rules.Count == 0)
            {
                config.Contracts.GroupSolver.Enabled = true;
                config.Contracts.GroupSolver.PreciseOrcaMaxGroupSize = 24;
                config.Contracts.GroupSolver.CrowdFlowMinGroupSize = 96;
                config.Contracts.GroupSolver.Rules.Add(new Navigation2DNavSolverRuleConfig
                {
                    Id = "test_precise_orca",
                    MinGroupSize = 0,
                    MaxGroupSize = 24,
                    SolverMode = NavSolverMode.PreciseOrca,
                    Reason = "group_size_small",
                });
                config.Contracts.GroupSolver.Rules.Add(new Navigation2DNavSolverRuleConfig
                {
                    Id = "test_hybrid",
                    MinGroupSize = 25,
                    MaxGroupSize = 95,
                    SolverMode = NavSolverMode.Hybrid,
                    Reason = "group_size_mid",
                });
                config.Contracts.GroupSolver.Rules.Add(new Navigation2DNavSolverRuleConfig
                {
                    Id = "test_crowd_flow",
                    MinGroupSize = 96,
                    MaxGroupSize = int.MaxValue,
                    SolverMode = NavSolverMode.CrowdFlow,
                    Reason = "group_size_large",
                });
            }

            return config.CloneValidated();
        }
    }
}
