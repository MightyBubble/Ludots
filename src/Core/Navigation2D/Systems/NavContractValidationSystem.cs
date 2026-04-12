using System;
using System.Text;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavContractValidationSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription().WithAll<NavActor>();

        private readonly Navigation2DContractCatalog _catalog;
        private readonly GameEngine _engine;
        private readonly CommandBuffer _commandBuffer = new();

        public NavContractValidationSystem(World world, Navigation2DContractCatalog catalog, GameEngine engine) : base(world)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public override void Update(in float dt)
        {
            StringBuilder? diagnostics = null;
            bool hasBoardEnvironment = _engine.CurrentMapSession?.PrimaryBoard != null;
            bool hasPathingEnvironment = _engine.GetService(CoreServiceKeys.PathService) != null;

            foreach (ref var chunk in World.Query(in Query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavActor> actors = chunk.GetSpan<NavActor>();

                bool hasNavProfileRef = chunk.Has<NavProfileRef>();
                Span<NavProfileRef> navProfileRefs = hasNavProfileRef ? chunk.GetSpan<NavProfileRef>() : default;
                bool hasCrowdProfileRef = chunk.Has<NavCrowdProfileRef>();
                Span<NavCrowdProfileRef> crowdProfileRefs = hasCrowdProfileRef ? chunk.GetSpan<NavCrowdProfileRef>() : default;
                bool hasKnockbackPolicyRef = chunk.Has<NavKnockbackPolicyRef>();
                Span<NavKnockbackPolicyRef> knockbackPolicyRefs = hasKnockbackPolicyRef ? chunk.GetSpan<NavKnockbackPolicyRef>() : default;
                bool hasRuntimeState = chunk.Has<NavActorRuntimeState>();
                Span<NavActorRuntimeState> runtimeStates = hasRuntimeState ? chunk.GetSpan<NavActorRuntimeState>() : default;
                bool hasTeam = chunk.Has<Team>();
                Span<Team> teams = hasTeam ? chunk.GetSpan<Team>() : default;
                bool hasTeamIdentity = chunk.Has<TeamIdentity>();
                Span<TeamIdentity> teamIdentities = hasTeamIdentity ? chunk.GetSpan<TeamIdentity>() : default;
                bool hasWorldPosition = chunk.Has<WorldPositionCm>();

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    NavActor actor = actors[index];
                    if (!actor.Enabled)
                    {
                        continue;
                    }

                    if (!hasWorldPosition)
                    {
                        AppendIssue(ref diagnostics, entity, "missing WorldPositionCm");
                        continue;
                    }

                    if (!hasBoardEnvironment || !hasPathingEnvironment)
                    {
                        AppendIssue(
                            ref diagnostics,
                            entity,
                            $"missing navigation environment (board={hasBoardEnvironment}, pathing={hasPathingEnvironment})");
                        continue;
                    }

                    if (!hasNavProfileRef)
                    {
                        AppendIssue(ref diagnostics, entity, "missing NavProfileRef");
                        continue;
                    }

                    if (!TryResolveTeamId(hasTeam, teams, hasTeamIdentity, teamIdentities, index, out _))
                    {
                        AppendIssue(ref diagnostics, entity, "missing Team or TeamIdentity");
                        continue;
                    }

                    if (!Enum.IsDefined(typeof(NavPhysicsMode), actor.PhysicsMode))
                    {
                        AppendIssue(ref diagnostics, entity, $"invalid NavPhysicsMode '{actor.PhysicsModeValue}'");
                        continue;
                    }

                    if (!Enum.IsDefined(typeof(NavSolverMode), actor.DefaultSolverMode))
                    {
                        AppendIssue(ref diagnostics, entity, $"invalid NavSolverMode '{actor.DefaultSolverModeValue}'");
                        continue;
                    }

                    int navProfileId = navProfileRefs[index].ProfileId;
                    if (!_catalog.TryGetNavProfile(navProfileId, out _))
                    {
                        AppendIssue(ref diagnostics, entity, $"unknown NavProfileRef '{navProfileId}'");
                        continue;
                    }

                    int crowdProfileId = 0;
                    if (actor.PhysicsMode == NavPhysicsMode.NavCrowdResolve)
                    {
                        if (!hasCrowdProfileRef)
                        {
                            AppendIssue(ref diagnostics, entity, "NavCrowdResolve requires NavCrowdProfileRef");
                            continue;
                        }

                        crowdProfileId = crowdProfileRefs[index].ProfileId;
                        if (!_catalog.TryGetCrowdProfile(crowdProfileId, out _))
                        {
                            AppendIssue(ref diagnostics, entity, $"unknown NavCrowdProfileRef '{crowdProfileId}'");
                            continue;
                        }
                    }
                    else if (hasCrowdProfileRef && crowdProfileRefs[index].ProfileId != 0 && !_catalog.TryGetCrowdProfile(crowdProfileRefs[index].ProfileId, out _))
                    {
                        AppendIssue(ref diagnostics, entity, $"unknown optional NavCrowdProfileRef '{crowdProfileRefs[index].ProfileId}'");
                        continue;
                    }

                    int knockbackPolicyId = 0;
                    if (hasKnockbackPolicyRef)
                    {
                        knockbackPolicyId = knockbackPolicyRefs[index].PolicyId;
                        if (knockbackPolicyId != 0 && !_catalog.TryGetKnockbackPolicy(knockbackPolicyId, out _))
                        {
                            AppendIssue(ref diagnostics, entity, $"unknown NavKnockbackPolicyRef '{knockbackPolicyId}'");
                            continue;
                        }
                    }

                    if (actor.PhysicsMode != NavPhysicsMode.FullPhysics2D && chunk.Has<Mass2D>())
                    {
                        NavActorRuntimeState state = hasRuntimeState ? runtimeStates[index] : default;
                        if (state.AddedMass2D == 0)
                        {
                            AppendIssue(ref diagnostics, entity, "non-FullPhysics2D NavActor cannot author Mass2D directly");
                            continue;
                        }
                    }

                    var runtimeState = hasRuntimeState ? runtimeStates[index] : default;
                    runtimeState.IsValidated = 1;
                    runtimeState.AppliedNavProfileId = navProfileId;
                    runtimeState.AppliedCrowdProfileId = crowdProfileId;
                    runtimeState.AppliedKnockbackPolicyId = knockbackPolicyId;
                    if (hasRuntimeState)
                    {
                        World.Set(entity, runtimeState);
                    }
                    else
                    {
                        _commandBuffer.Add(entity, runtimeState);
                    }
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }

            if (diagnostics != null)
            {
                throw new InvalidOperationException($"NavContractValidator failed:\n{diagnostics}");
            }
        }

        private static bool TryResolveTeamId(bool hasTeam, Span<Team> teams, bool hasTeamIdentity, Span<TeamIdentity> teamIdentities, int index, out int teamId)
        {
            if (hasTeam)
            {
                teamId = teams[index].Id;
                return true;
            }

            if (hasTeamIdentity)
            {
                teamId = teamIdentities[index].TeamId;
                return true;
            }

            teamId = 0;
            return false;
        }

        private static void AppendIssue(ref StringBuilder? builder, Entity entity, string issue)
        {
            builder ??= new StringBuilder();
            builder.Append("- entity ").Append(entity.Id).Append(": ").AppendLine(issue);
        }
    }
}
