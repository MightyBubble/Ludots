using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// The response-chain window currently waiting for a player's answer. Opened and closed only
    /// by the effect proposal processor; answers arrive through the SubmitResponseChainOrder
    /// graph op, which reads actor / target / offered effect from here.
    /// </summary>
    public sealed class ResponseChainPromptState
    {
        public bool IsOpen { get; private set; }
        public int WindowId { get; private set; }
        public int PlayerId { get; private set; }
        public Entity Actor { get; private set; }
        public Entity Target { get; private set; }
        public Entity TargetContext { get; private set; }
        public int OfferedEffectTemplateId { get; private set; }

        /// <summary>Answers that arrived while no prompt was waiting on the answering player.</summary>
        public int RejectedWithoutPromptCount { get; private set; }

        /// <summary>Prompts closed unanswered because their actor was destroyed (e.g. its map unloaded).</summary>
        public int AbandonedForMissingActorCount { get; private set; }
        public OrderSubmitResult LastSubmissionResult { get; private set; } = OrderSubmitResult.RejectedByRule;
        public int LastSubmittedOrderId { get; private set; }

        internal void Open(int windowId, int playerId, Entity actor, Entity target, Entity targetContext, int offeredEffectTemplateId)
        {
            IsOpen = true;
            WindowId = windowId;
            PlayerId = playerId;
            Actor = actor;
            Target = AsOrderOptional(target);
            TargetContext = AsOrderOptional(targetContext);
            OfferedEffectTemplateId = offeredEffectTemplateId;
        }

        /// <summary>
        /// Effect windows leave absent references as default(Entity); order intake only accepts
        /// Entity.Null for "absent", so the prompt hands answers canonical references.
        /// </summary>
        private static Entity AsOrderOptional(Entity entity) => entity == default ? Entity.Null : entity;

        internal void Close()
        {
            IsOpen = false;
            WindowId = 0;
            PlayerId = 0;
            Actor = Entity.Null;
            Target = Entity.Null;
            TargetContext = Entity.Null;
            OfferedEffectTemplateId = 0;
        }

        internal void RecordAbandonedForMissingActor()
        {
            AbandonedForMissingActorCount++;
        }

        internal void RecordRejectedWithoutPrompt()
        {
            RejectedWithoutPromptCount++;
        }

        internal void RecordSubmission(OrderSubmitResult result, int orderId)
        {
            LastSubmissionResult = result;
            LastSubmittedOrderId = orderId;
        }
    }
}
