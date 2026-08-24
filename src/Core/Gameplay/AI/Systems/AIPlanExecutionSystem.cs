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
        private readonly OrderTypeRegistry? _orderTypeRegistry;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIPlan32, OrderBuffer, BlackboardIntBuffer, BlackboardEntityBuffer>();

        public AIPlanExecutionSystem(
            World world,
            IClock clock,
            ActionLibraryCompiled256 library,
            OrderQueue orders,
            OrderTypeRegistry? orderTypeRegistry = null)
            : base(world)
        {
            _clock = clock;
            _library = library;
            _orders = orders;
            _orderTypeRegistry = orderTypeRegistry;
        }

        public override void Update(in float dt)
        {
            int step = _clock.Now(ClockDomainId.Step);
            var job = new ExecuteJob(World, _library, _orders, _orderTypeRegistry, step);
            World.InlineEntityQuery<ExecuteJob, AIAgent, AIPlan32, OrderBuffer, BlackboardIntBuffer, BlackboardEntityBuffer>(in _query, ref job);
        }

        private struct ExecuteJob : IForEachWithEntity<AIAgent, AIPlan32, OrderBuffer, BlackboardIntBuffer, BlackboardEntityBuffer>
        {
            private readonly World _world;
            private readonly ActionLibraryCompiled256 _library;
            private readonly OrderQueue _orders;
            private readonly OrderTypeRegistry? _orderTypeRegistry;
            private readonly int _step;

            public ExecuteJob(World world, ActionLibraryCompiled256 library, OrderQueue orders, OrderTypeRegistry? orderTypeRegistry, int step)
            {
                _world = world;
                _library = library;
                _orders = orders;
                _orderTypeRegistry = orderTypeRegistry;
                _step = step;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref AIAgent agent,
                ref AIPlan32 plan,
                ref OrderBuffer orderBuffer,
                ref BlackboardIntBuffer ints,
                ref BlackboardEntityBuffer entities)
            {
                if (plan.IsDone || orderBuffer.HasActive) return;

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
                    _world,
                    in _library.OrderSpec[actionId],
                    _library.GetBindings(actionId),
                    entity,
                    ref ints,
                    ref entities,
                    _step,
                    _orders,
                    _orderTypeRegistry);

                if (ok) plan.Advance();
            }
        }
    }
}
