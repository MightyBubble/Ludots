using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Gameplay.GAS.Input
{
    /// <summary>
    /// The response-chain prompt currently waiting for one player's answer. Opened and closed only
    /// by the effect proposal processor; answers arrive through the SubmitResponseChainOrder
    /// graph op. The prompted player is the owner of the listener that asked to respond, and only
    /// its unit (<see cref="Responder"/>) may answer, once per prompt.
    /// </summary>
    public sealed class ResponseChainPromptState
    {
        public bool IsOpen { get; private set; }
        public int WindowId { get; private set; }
        public int PlayerId { get; private set; }
        public Entity Responder { get; private set; }
        public Entity WindowSource { get; private set; }
        public Entity WindowTarget { get; private set; }
        public Entity TargetContext { get; private set; }
        public int OfferedEffectTemplateId { get; private set; }
        public bool Answered { get; private set; }

        /// <summary>Answers that arrived while no unanswered prompt was waiting on the answering player.</summary>
        public int RejectedWithoutPromptCount { get; private set; }

        /// <summary>Prompts closed unanswered because their responder was destroyed (e.g. its map unloaded).</summary>
        public int AbandonedForMissingActorCount { get; private set; }
        public OrderSubmitResult LastSubmissionResult { get; private set; } = OrderSubmitResult.RejectedByRule;
        public int LastSubmittedOrderId { get; private set; }

        internal void Open(
            int windowId,
            int playerId,
            Entity responder,
            Entity windowSource,
            Entity windowTarget,
            Entity targetContext,
            int offeredEffectTemplateId)
        {
            IsOpen = true;
            WindowId = windowId;
            PlayerId = playerId;
            Responder = responder;
            WindowSource = AsOrderOptional(windowSource);
            WindowTarget = AsOrderOptional(windowTarget);
            TargetContext = AsOrderOptional(targetContext);
            OfferedEffectTemplateId = offeredEffectTemplateId;
            Answered = false;
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
            Responder = Entity.Null;
            WindowSource = Entity.Null;
            WindowTarget = Entity.Null;
            TargetContext = Entity.Null;
            OfferedEffectTemplateId = 0;
            Answered = false;
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
            if (OrderSubmitResultSemantics.IsAccepted(result))
            {
                Answered = true;
            }
        }
    }
}
