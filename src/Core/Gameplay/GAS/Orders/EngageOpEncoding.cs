using System;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// SubmitEngageBatch immediate packing: low 16 bits = EQS query registry id,
    /// high 16 bits = follow-up cast order-type config-key id. Both spaces are dense
    /// from 1; overflow fails loud instead of aliasing.
    /// </summary>
    public static class EngageOpEncoding
    {
        public const int MaxKeyId = 0xFFFF;

        public static int Pack(int queryKeyId, int orderTypeKeyId)
        {
            if ((uint)(queryKeyId - 1) > MaxKeyId - 1 || (uint)(orderTypeKeyId - 1) > MaxKeyId - 1)
            {
                throw new InvalidOperationException(
                    $"SubmitEngageBatch key ids out of range (eqsQuery={queryKeyId}, orderType={orderTypeKeyId}).");
            }

            return queryKeyId | (orderTypeKeyId << 16);
        }

        public static int UnpackQueryKeyId(int imm) => imm & MaxKeyId;

        public static int UnpackOrderTypeKeyId(int imm) => (imm >> 16) & MaxKeyId;
    }
}
