using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavGroupCommandApplySystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription GroupQuery = new QueryDescription()
            .WithAll<NavGroupTag, NavGroupIdentity, NavGroupTarget2D, NavGroupRuntimeState>();

        private static readonly QueryDescription MemberQuery = new QueryDescription()
            .WithAll<NavGroupMember>();

        private readonly Navigation2DContractCatalog _catalog;
        private readonly Dictionary<int, GroupPlan> _plans = new();
        private readonly CommandBuffer _commandBuffer = new();

        public NavGroupCommandApplySystem(World world, Navigation2DContractCatalog catalog) : base(world)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public override void Update(in float dt)
        {
            _plans.Clear();

            foreach (ref var chunk in World.Query(in GroupQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavGroupIdentity> identities = chunk.GetSpan<NavGroupIdentity>();
                Span<NavGroupTarget2D> targets = chunk.GetSpan<NavGroupTarget2D>();
                Span<NavGroupRuntimeState> states = chunk.GetSpan<NavGroupRuntimeState>();

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    ref NavGroupRuntimeState state = ref states[index];
                    NavSolverRuleDefinition rule = _catalog.ResolveGroupSolverRule(state.MemberCount);
                    state.SolverMode = rule.SolverMode;
                    state.ActiveRuleId = rule.Id;

                    _plans[identities[index].GroupId] = new GroupPlan(entity, targets[index], state.MemberCount, rule);
                }
            }

            foreach (ref var chunk in World.Query(in MemberQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavGroupMember> members = chunk.GetSpan<NavGroupMember>();

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    NavGroupMember member = members[index];
                    if (!_plans.TryGetValue(member.GroupId, out GroupPlan plan))
                    {
                        continue;
                    }

                    NavGoal2D goal = CreateGoal(plan, member.SlotIndex);
                    if (!World.Has<NavGoal2D>(entity))
                    {
                        _commandBuffer.Add(entity, goal);
                    }
                    else
                    {
                        World.Set(entity, goal);
                    }

                    var solverMode = new NavSolverModeComponent
                    {
                        Value = (byte)plan.Rule.SolverMode,
                        RuleId = plan.Rule.Id,
                    };

                    if (!World.Has<NavSolverModeComponent>(entity))
                    {
                        _commandBuffer.Add(entity, solverMode);
                    }
                    else
                    {
                        World.Set(entity, solverMode);
                    }
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private static NavGoal2D CreateGoal(in GroupPlan plan, int slotIndex)
        {
            GetGridLayout(plan.MemberCount, out int cols, out int rows);
            GetGridCell(slotIndex, cols, out int row, out int col);
            Fix64 offsetX = Fix64.FromInt(GetCenteredOffset(col, cols, plan.Target.FormationSpacingCm));
            Fix64 offsetY = Fix64.FromInt(GetCenteredOffset(row, rows, plan.Target.FormationSpacingCm));

            Fix64 sin = Fix64Math.Sin(plan.Target.RotationRad);
            Fix64 cos = Fix64Math.Cos(plan.Target.RotationRad);
            Fix64 rotatedX = (cos * offsetX) - (sin * offsetY);
            Fix64 rotatedY = (sin * offsetX) + (cos * offsetY);

            return new NavGoal2D
            {
                Kind = NavGoalKind2D.Point,
                TargetCm = new Fix64Vec2(plan.Target.TargetCm.X + rotatedX, plan.Target.TargetCm.Y + rotatedY),
                RadiusCm = plan.Target.RadiusCm,
            };
        }

        private static void GetGridLayout(int count, out int cols, out int rows)
        {
            if (count <= 0)
            {
                cols = 0;
                rows = 0;
                return;
            }

            cols = (int)Math.Ceiling(Math.Sqrt(count));
            rows = (int)Math.Ceiling(count / (double)cols);
        }

        private static void GetGridCell(int index, int cols, out int row, out int col)
        {
            row = cols <= 0 ? 0 : index / cols;
            col = cols <= 0 ? 0 : index % cols;
        }

        private static int GetCenteredOffset(int index, int count, int spacingCm)
        {
            return count <= 0 ? 0 : -((count - 1) * spacingCm / 2) + index * spacingCm;
        }

        private readonly record struct GroupPlan(
            Entity GroupEntity,
            NavGroupTarget2D Target,
            int MemberCount,
            NavSolverRuleDefinition Rule);
    }
}
