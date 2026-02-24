using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.AI.Systems
{
    public sealed class AIPlanExecutionSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly ActionLibraryCompiled256 _library;
        private readonly OrderQueue _orders;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIPlan32, GameplayTagContainer, BlackboardIntBuffer, BlackboardEntityBuffer>();

        public AIPlanExecutionSystem(World world, IClock clock, ActionLibraryCompiled256 library, OrderQueue orders)
            : base(world)
        {
            _clock = clock;
            _library = library;
            _orders = orders;
        }

        public override void Update(in float dt)
        {
            int step = _clock.Now(ClockDomainId.Step);
            var job = new ExecuteJob(_library, _orders, step);
            World.InlineEntityQuery<ExecuteJob, AIAgent, AIPlan32, GameplayTagContainer, BlackboardIntBuffer, BlackboardEntityBuffer>(in _query, ref job);
        }

        private struct ExecuteJob : IForEachWithEntity<AIAgent, AIPlan32, GameplayTagContainer, BlackboardIntBuffer, BlackboardEntityBuffer>
        {
            private readonly ActionLibraryCompiled256 _library;
            private readonly OrderQueue _orders;
            private readonly int _step;

            public ExecuteJob(ActionLibraryCompiled256 library, OrderQueue orders, int step)
            {
                _library = library;
                _orders = orders;
                _step = step;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref AIAgent agent,
                ref AIPlan32 plan,
                ref GameplayTagContainer tags,
                ref BlackboardIntBuffer ints,
                ref BlackboardEntityBuffer entities)
            {
                if (plan.IsDone) return;
                if (tags.HasTag(OrderStateTags.State_HasActive)) return;

                if (!plan.TryGetCurrent(out int actionId)) return;
                if ((uint)actionId >= (uint)_library.Count)
                {
                    plan.Advance();
                    return;
                }

                if (_library.ExecutorKind[actionId] != ActionExecutorKind.SubmitOrder)
                {
                    plan.Advance();
                    return;
                }

                bool ok = PlanExecutor.TrySubmitOrder(
                    in _library.OrderSpec[actionId],
                    _library.GetBindings(actionId),
                    entity,
                    ref ints,
                    ref entities,
                    _step,
                    _orders);

                if (ok) plan.Advance();
            }
        }
    }
}
