using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Navigation2D.Runtime
{
    public readonly record struct NavProfileDefinition(
        int Id,
        string Key,
        Fix64 MaxSpeedCmPerSec,
        Fix64 MaxAccelCmPerSec2,
        Fix64 RadiusCm,
        Fix64 NeighborDistCm,
        Fix64 TimeHorizonSec,
        int MaxNeighbors,
        Fix64 GoalRadiusCm);

    public readonly record struct NavCrowdProfileDefinition(
        int Id,
        string Key,
        Fix64 GeometryRadiusCm,
        Fix64 NavMass,
        Fix64 YieldWeight,
        NavPushClass PushClass,
        NavSolverMode SolverPreference,
        int RetryLimit,
        int TimeoutTicks,
        int AbandonTicks);

    public readonly record struct NavKnockbackPolicyDefinition(
        int Id,
        string Key,
        int OverrideTicks,
        bool ClearNavGoalWhileActive);

    public readonly record struct NavSolverRuleDefinition(
        int Id,
        string Key,
        int MinGroupSize,
        int MaxGroupSize,
        NavSolverMode SolverMode,
        string Reason);

    public readonly record struct NavCrowdRelationshipPolicy(
        Fix64 FriendlyYieldFactor,
        Fix64 NeutralYieldFactor,
        Fix64 HostileYieldFactor,
        Fix64 DominantPushMassRatio);

    public readonly record struct NavGroupSolverPolicy(
        bool Enabled,
        int PreciseOrcaMaxGroupSize,
        int CrowdFlowMinGroupSize);

    public sealed class Navigation2DContractCatalog
    {
        private readonly NavProfileDefinition[] _navProfilesById;
        private readonly NavCrowdProfileDefinition[] _crowdProfilesById;
        private readonly NavKnockbackPolicyDefinition[] _knockbackPoliciesById;
        private readonly NavSolverRuleDefinition[] _solverRulesById;

        public Navigation2DContractCatalog(Navigation2DConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ValidateContractsSource(config.Contracts);
            Navigation2DContractsConfig contracts = config.CloneValidated().Contracts;

            NavProfileIds = new StringIntRegistry(capacity: Math.Max(8, contracts.NavProfiles.Count));
            CrowdProfileIds = new StringIntRegistry(capacity: Math.Max(8, contracts.CrowdProfiles.Count));
            KnockbackPolicyIds = new StringIntRegistry(capacity: Math.Max(8, contracts.KnockbackPolicies.Count));
            SolverRuleIds = new StringIntRegistry(capacity: Math.Max(8, contracts.GroupSolver.Rules.Count));

            _navProfilesById = BuildNavProfiles(contracts.NavProfiles, NavProfileIds);
            _crowdProfilesById = BuildCrowdProfiles(contracts.CrowdProfiles, CrowdProfileIds);
            _knockbackPoliciesById = BuildKnockbackPolicies(contracts.KnockbackPolicies, KnockbackPolicyIds);
            _solverRulesById = BuildSolverRules(contracts.GroupSolver.Rules, SolverRuleIds);

            NavProfileIds.Freeze();
            CrowdProfileIds.Freeze();
            KnockbackPolicyIds.Freeze();
            SolverRuleIds.Freeze();

            GroupSolver = new NavGroupSolverPolicy(
                Enabled: contracts.GroupSolver.Enabled,
                PreciseOrcaMaxGroupSize: contracts.GroupSolver.PreciseOrcaMaxGroupSize,
                CrowdFlowMinGroupSize: contracts.GroupSolver.CrowdFlowMinGroupSize);
            CrowdRelationship = new NavCrowdRelationshipPolicy(
                FriendlyYieldFactor: Fix64.FromFloat(contracts.CrowdRelationship.FriendlyYieldFactor),
                NeutralYieldFactor: Fix64.FromFloat(contracts.CrowdRelationship.NeutralYieldFactor),
                HostileYieldFactor: Fix64.FromFloat(contracts.CrowdRelationship.HostileYieldFactor),
                DominantPushMassRatio: Fix64.FromFloat(contracts.CrowdRelationship.DominantPushMassRatio));
        }

        public StringIntRegistry NavProfileIds { get; }
        public StringIntRegistry CrowdProfileIds { get; }
        public StringIntRegistry KnockbackPolicyIds { get; }
        public StringIntRegistry SolverRuleIds { get; }
        public NavGroupSolverPolicy GroupSolver { get; }
        public NavCrowdRelationshipPolicy CrowdRelationship { get; }

        public int RequireNavProfileId(string key)
        {
            int profileId = NavProfileIds.GetId(key);
            if (!TryGetNavProfile(profileId, out _))
            {
                throw new InvalidOperationException($"Unknown Navigation2D nav profile '{key}'.");
            }

            return profileId;
        }

        public int RequireCrowdProfileId(string key)
        {
            int profileId = CrowdProfileIds.GetId(key);
            if (!TryGetCrowdProfile(profileId, out _))
            {
                throw new InvalidOperationException($"Unknown Navigation2D crowd profile '{key}'.");
            }

            return profileId;
        }

        public int RequireKnockbackPolicyId(string key)
        {
            int policyId = KnockbackPolicyIds.GetId(key);
            if (!TryGetKnockbackPolicy(policyId, out _))
            {
                throw new InvalidOperationException($"Unknown Navigation2D knockback policy '{key}'.");
            }

            return policyId;
        }

        public bool TryGetNavProfile(int profileId, out NavProfileDefinition profile)
        {
            profile = default;
            if ((uint)profileId >= (uint)_navProfilesById.Length || profileId == NavProfileIds.InvalidId)
            {
                return false;
            }

            profile = _navProfilesById[profileId];
            return profile.Id != 0;
        }

        public bool TryGetCrowdProfile(int profileId, out NavCrowdProfileDefinition profile)
        {
            profile = default;
            if ((uint)profileId >= (uint)_crowdProfilesById.Length || profileId == CrowdProfileIds.InvalidId)
            {
                return false;
            }

            profile = _crowdProfilesById[profileId];
            return profile.Id != 0;
        }

        public bool TryGetKnockbackPolicy(int policyId, out NavKnockbackPolicyDefinition policy)
        {
            policy = default;
            if ((uint)policyId >= (uint)_knockbackPoliciesById.Length || policyId == KnockbackPolicyIds.InvalidId)
            {
                return false;
            }

            policy = _knockbackPoliciesById[policyId];
            return policy.Id != 0;
        }

        public bool TryGetSolverRule(int ruleId, out NavSolverRuleDefinition rule)
        {
            rule = default;
            if ((uint)ruleId >= (uint)_solverRulesById.Length || ruleId == SolverRuleIds.InvalidId)
            {
                return false;
            }

            rule = _solverRulesById[ruleId];
            return rule.Id != 0;
        }

        public NavSolverRuleDefinition ResolveGroupSolverRule(int memberCount)
        {
            for (int i = 1; i < _solverRulesById.Length; i++)
            {
                NavSolverRuleDefinition rule = _solverRulesById[i];
                if (rule.Id == 0)
                {
                    continue;
                }

                if (memberCount >= rule.MinGroupSize && memberCount <= rule.MaxGroupSize)
                {
                    return rule;
                }
            }

            throw new InvalidOperationException($"No Navigation2D solver rule covers group size {memberCount}.");
        }

        private static void ValidateContractsSource(Navigation2DContractsConfig? contracts)
        {
            if (contracts == null)
            {
                throw new InvalidOperationException("Navigation2D contracts are required when Navigation2D.Enabled=true.");
            }

            RequireNamedEntries("NavProfiles", contracts.NavProfiles, static item => item?.Id);
            RequireNamedEntries("CrowdProfiles", contracts.CrowdProfiles, static item => item?.Id);
            RequireNamedEntries("KnockbackPolicies", contracts.KnockbackPolicies, static item => item?.Id);
            RequireNamedEntries("GroupSolver.Rules", contracts.GroupSolver?.Rules, static item => item?.Id);
            RequireNamedEntries("GroupSolver.Rules.Reason", contracts.GroupSolver?.Rules, static item => item?.Reason);
        }

        private static void RequireNamedEntries<T>(string label, IReadOnlyList<T>? entries, Func<T, string?> selector)
        {
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException($"Navigation2D contracts '{label}' must declare at least one explicit entry.");
            }

            for (int i = 0; i < entries.Count; i++)
            {
                string? value = selector(entries[i]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException($"Navigation2D contracts '{label}' entry #{i} is missing its explicit key.");
                }
            }
        }

        private static NavProfileDefinition[] BuildNavProfiles(IReadOnlyList<Navigation2DNavProfileConfig> source, StringIntRegistry ids)
        {
            int maxId = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int id = ids.Register(source[i].Id);
                maxId = Math.Max(maxId, id);
            }

            var result = new NavProfileDefinition[Math.Max(1, maxId + 1)];
            for (int i = 0; i < source.Count; i++)
            {
                Navigation2DNavProfileConfig config = source[i];
                int id = ids.GetId(config.Id);
                result[id] = new NavProfileDefinition(
                    Id: id,
                    Key: config.Id,
                    MaxSpeedCmPerSec: Fix64.FromInt(config.MaxSpeedCmPerSec),
                    MaxAccelCmPerSec2: Fix64.FromInt(config.MaxAccelCmPerSec2),
                    RadiusCm: Fix64.FromInt(config.RadiusCm),
                    NeighborDistCm: Fix64.FromInt(config.NeighborDistCm),
                    TimeHorizonSec: Fix64.FromFloat(config.TimeHorizonSec),
                    MaxNeighbors: config.MaxNeighbors,
                    GoalRadiusCm: Fix64.FromInt(config.GoalRadiusCm));
            }

            return result;
        }

        private static NavCrowdProfileDefinition[] BuildCrowdProfiles(IReadOnlyList<Navigation2DCrowdProfileConfig> source, StringIntRegistry ids)
        {
            int maxId = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int id = ids.Register(source[i].Id);
                maxId = Math.Max(maxId, id);
            }

            var result = new NavCrowdProfileDefinition[Math.Max(1, maxId + 1)];
            for (int i = 0; i < source.Count; i++)
            {
                Navigation2DCrowdProfileConfig config = source[i];
                int id = ids.GetId(config.Id);
                result[id] = new NavCrowdProfileDefinition(
                    Id: id,
                    Key: config.Id,
                    GeometryRadiusCm: Fix64.FromInt(config.GeometryRadiusCm),
                    NavMass: Fix64.FromFloat(config.NavMass),
                    YieldWeight: Fix64.FromFloat(config.YieldWeight),
                    PushClass: config.PushClass,
                    SolverPreference: config.SolverPreference,
                    RetryLimit: config.RetryLimit,
                    TimeoutTicks: config.TimeoutTicks,
                    AbandonTicks: config.AbandonTicks);
            }

            return result;
        }

        private static NavKnockbackPolicyDefinition[] BuildKnockbackPolicies(IReadOnlyList<Navigation2DKnockbackPolicyConfig> source, StringIntRegistry ids)
        {
            int maxId = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int id = ids.Register(source[i].Id);
                maxId = Math.Max(maxId, id);
            }

            var result = new NavKnockbackPolicyDefinition[Math.Max(1, maxId + 1)];
            for (int i = 0; i < source.Count; i++)
            {
                Navigation2DKnockbackPolicyConfig config = source[i];
                int id = ids.GetId(config.Id);
                result[id] = new NavKnockbackPolicyDefinition(
                    Id: id,
                    Key: config.Id,
                    OverrideTicks: config.OverrideTicks,
                    ClearNavGoalWhileActive: config.ClearNavGoalWhileActive);
            }

            return result;
        }

        private static NavSolverRuleDefinition[] BuildSolverRules(IReadOnlyList<Navigation2DNavSolverRuleConfig> source, StringIntRegistry ids)
        {
            int maxId = 0;
            for (int i = 0; i < source.Count; i++)
            {
                int id = ids.Register(source[i].Id);
                maxId = Math.Max(maxId, id);
            }

            var result = new NavSolverRuleDefinition[Math.Max(1, maxId + 1)];
            for (int i = 0; i < source.Count; i++)
            {
                Navigation2DNavSolverRuleConfig config = source[i];
                int id = ids.GetId(config.Id);
                result[id] = new NavSolverRuleDefinition(
                    Id: id,
                    Key: config.Id,
                    MinGroupSize: config.MinGroupSize,
                    MaxGroupSize: config.MaxGroupSize,
                    SolverMode: config.SolverMode,
                    Reason: config.Reason);
            }

            return result;
        }
    }

    public static class Navigation2DContractCatalogScope
    {
        private static Navigation2DContractCatalog? _current;

        public static void SetCurrent(Navigation2DContractCatalog? catalog)
        {
            _current = catalog;
        }

        public static Navigation2DContractCatalog RequireCurrent()
        {
            return _current ?? throw new InvalidOperationException("Navigation2DContractCatalog has not been initialized.");
        }
    }
}
