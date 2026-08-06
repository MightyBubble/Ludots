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
        private readonly OrderTypeRegistry _orderTypeRegistry;
        private readonly OrderTerminalResultBuffer _terminalResults;

        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<AIAgent, AIPlan32, BlackboardIntBuffer, BlackboardEntityBuffer>();

        public AIPlanExecutionSystem(
            World world,
            IClock clock,
            ActionLibraryCompiled256 library,
            OrderQueue orders,
            OrderTypeRegistry orderTypeRegistry)
            : base(world)
        {
            _clock = clock;
            _library = library;
            _orders = orders;
            _orderTypeRegistry = orderTypeRegistry ?? throw new System.ArgumentNullException(nameof(orderTypeRegistry));
            _terminalResults = _orderTypeRegistry.TerminalResults;
        }

        public override void Update(in float dt)
        {
            int step = _clock.Now(ClockDomainId.Step);
            var job = new ExecuteJob(World, _library, _orders, _orderTypeRegistry, _terminalResults, step);
            World.InlineEntityQuery<ExecuteJob, AIAgent, AIPlan32, BlackboardIntBuffer, BlackboardEntityBuffer>(in _query, ref job);
        }

        private struct ExecuteJob : IForEachWithEntity<AIAgent, AIPlan32, BlackboardIntBuffer, BlackboardEntityBuffer>
        {
            private readonly World _world;
            private readonly ActionLibraryCompiled256 _library;
            private readonly OrderQueue _orders;
            private readonly OrderTypeRegistry _orderTypeRegistry;
            private readonly OrderTerminalResultBuffer _terminalResults;
            private readonly int _step;

            public ExecuteJob(World world, ActionLibraryCompiled256 library, OrderQueue orders, OrderTypeRegistry orderTypeRegistry, OrderTerminalResultBuffer terminalResults, int step)
            {
                _world = world;
                _library = library;
                _orders = orders;
                _orderTypeRegistry = orderTypeRegistry;
                _terminalResults = terminalResults;
                _step = step;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(
                Entity entity,
                ref AIAgent agent,
                ref AIPlan32 plan,
                ref BlackboardIntBuffer ints,
                ref BlackboardEntityBuffer entities)
            {
                if (plan.IsWaitingForOrder)
                {
                    if (!_terminalResults.TryConsume(plan.WaitingOrderId, out OrderTerminalOutcome terminal))
                    {
                        if (!_terminalResults.IsAwaitingRetainedOutcome(plan.WaitingOrderId))
                        {
                            throw new System.InvalidOperationException(
                                $"AI.PLAN.ERR.WaitingOrderReceiptUnavailable: orderId={plan.WaitingOrderId}, orderTypeId={plan.WaitingOrderTypeId}.");
                        }

                        return;
                    }

                    if (terminal.OrderTypeId != plan.WaitingOrderTypeId || terminal.Actor != entity)
                    {
                        throw new System.InvalidOperationException(
                            $"AI.PLAN.ERR.TerminalReceiptMismatch: orderId={plan.WaitingOrderId}, expectedType={plan.WaitingOrderTypeId}, actualType={terminal.OrderTypeId}.");
                    }

                    int completedOrderId = plan.WaitingOrderId;
                    plan.ClearWaitingOrder();
                    switch (terminal.State)
                    {
                        case OrderTerminalState.Completed:
                            plan.Advance();
                            break;
                        case OrderTerminalState.Failed:
                        case OrderTerminalState.Cancelled:
                            plan.Clear();
                            break;
                        default:
                            throw new System.InvalidOperationException(
                                $"AI.PLAN.ERR.UnknownTerminalState: orderId={completedOrderId}, state={terminal.State}.");
                    }

                    _terminalResults.ReleaseConsumed(completedOrderId);
                    return;
                }

                if (plan.IsDone) return;

                if (!plan.TryGetCurrent(out int actionId)) return;
                if ((uint)actionId >= (uint)_library.Count)
                {
                    throw new System.InvalidOperationException(
                        $"AI.PLAN.ERR.InvalidActionId: actor={entity.Id}, actionId={actionId}, libraryCount={_library.Count}.");
                }

                if (_library.ExecutorKind[actionId] != ActionExecutorKind.SubmitOrder)
                {
                    throw new System.InvalidOperationException(
                        $"AI.PLAN.ERR.UnsupportedActionExecutorKind: actor={entity.Id}, actionId={actionId}, kind={_library.ExecutorKind[actionId]}.");
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
                    _orderTypeRegistry,
                    out int submittedOrderId);

                if (ok)
                {
                    plan.BeginWaitingForOrder(submittedOrderId, _library.OrderSpec[actionId].OrderTypeId);
                }
            }
        }
    }
}
