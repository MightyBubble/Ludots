using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class WorldStateProjectionSystem : BaseSystem<World, float>
    {
        private readonly WorldStateProjectionTable _table;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIWorldState256, BlackboardIntBuffer, BlackboardEntityBuffer>();

        public WorldStateProjectionSystem(World world, WorldStateProjectionTable table)
            : base(world)
        {
            _table = table;
        }

        public override void Update(in float dt)
        {
            var job = new ProjectJob(_table.Rules);
            World.InlineQuery<ProjectJob, AIAgent, AIWorldState256, BlackboardIntBuffer, BlackboardEntityBuffer>(in _query, ref job);
        }

        private struct ProjectJob : IForEach<AIAgent, AIWorldState256, BlackboardIntBuffer, BlackboardEntityBuffer>
        {
            private readonly WorldStateProjectionRule[] _rules;

            public ProjectJob(WorldStateProjectionRule[] rules)
            {
                _rules = rules;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref AIAgent agent, ref AIWorldState256 worldState, ref BlackboardIntBuffer ints, ref BlackboardEntityBuffer entities)
            {
                var next = worldState.Bits;
                next.Clear();

                for (int i = 0; i < _rules.Length; i++)
                {
                    ref readonly var rule = ref _rules[i];
                    bool value = rule.Op switch
                    {
                        WorldStateProjectionOp.IntEquals => ints.TryGet(rule.IntKey, out int vEq) && vEq == rule.IntValue,
                        WorldStateProjectionOp.IntGreaterOrEqual => ints.TryGet(rule.IntKey, out int vGe) && vGe >= rule.IntValue,
                        WorldStateProjectionOp.IntLessOrEqual => ints.TryGet(rule.IntKey, out int vLe) && vLe <= rule.IntValue,
                        WorldStateProjectionOp.EntityIsNonNull => entities.TryGet(rule.EntityKey, out var eNn) && eNn.Id != 0,
                        WorldStateProjectionOp.EntityIsNull => !entities.TryGet(rule.EntityKey, out var eNu) || eNu.Id == 0,
                        _ => false
                    };

                    next.SetBit(rule.AtomId, value);
                }

                if (!worldState.Bits.Equals(next))
                {
                    worldState.Bits = next;
                    worldState.Version++;
                }
            }
        }
    }
}

