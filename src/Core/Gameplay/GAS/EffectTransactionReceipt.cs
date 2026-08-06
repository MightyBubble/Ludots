using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;

namespace Ludots.Core.Gameplay.GAS
{
    public enum EffectTransactionOutcome : byte
    {
        Succeeded = 0,
        Failed = 1,
    }

    /// <summary>
    /// Explicit transaction receipt for one EffectRequest RootId.
    /// AbilityExec waits on and consumes this before advancing past EffectClip / EffectSignal.
    /// </summary>
    public struct EffectTransactionReceipt : IHasRequestId
    {
        public int RequestId { get; set; }
        public EffectTransactionOutcome Outcome;
        public OrderFailureReason FailureReason;
        public Entity Source;
        public Entity Target;
        public int TemplateId;
    }

    public sealed class EffectTransactionReceiptBuffer : SwapRemoveBuffer<EffectTransactionReceipt>
    {
        public const string CapacityExceededError = "GAS.EFFECT_RECEIPT.ERR.CapacityExceeded";
        public const string MissingBufferError = "GAS.EFFECT_RECEIPT.ERR.MissingBuffer";

        public EffectTransactionReceiptBuffer(int capacity = 1024) : base(capacity)
        {
        }

        public void Write(in EffectTransactionReceipt receipt)
        {
            if (receipt.RequestId <= 0)
            {
                throw new System.InvalidOperationException(
                    $"GAS.EFFECT_RECEIPT.ERR.InvalidRootId: rootId={receipt.RequestId}, templateId={receipt.TemplateId}.");
            }

            if (!TryAdd(in receipt))
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: capacity={Capacity}, count={Count}, rootId={receipt.RequestId}, templateId={receipt.TemplateId}.");
            }
        }
    }
}
