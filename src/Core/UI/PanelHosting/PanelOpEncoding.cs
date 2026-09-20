namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// CreatePanel instruction encoding: template key id and anchor key id share Imm
    /// (both are ConfigKeyRegistry ids, bounded by MaxKeys 4095 — 16 bits each is ample).
    /// </summary>
    internal static class PanelOpEncoding
    {
        public const int MaxKeyId = 0xFFFF;

        public static int Pack(int templateKeyId, int anchorKeyId)
        {
            if ((uint)(templateKeyId - 1) > MaxKeyId - 1 || (uint)(anchorKeyId - 1) > MaxKeyId - 1)
            {
                throw new System.InvalidOperationException(
                    $"CreatePanel key ids out of range (template={templateKeyId}, anchor={anchorKeyId}).");
            }

            return templateKeyId | (anchorKeyId << 16);
        }

        public static int UnpackTemplate(int imm) => imm & MaxKeyId;

        public static int UnpackAnchor(int imm) => (imm >> 16) & MaxKeyId;

        /// <summary>
        /// SetPanelAudience encoding: seat key id 0 means "clear the override" — the
        /// template's declared audience rules again (hotseat turn end).
        /// </summary>
        public static int PackAudience(int templateKeyId, int seatKeyId)
        {
            if ((uint)(templateKeyId - 1) > MaxKeyId - 1 || (uint)seatKeyId > MaxKeyId)
            {
                throw new System.InvalidOperationException(
                    $"SetPanelAudience key ids out of range (template={templateKeyId}, seat={seatKeyId}).");
            }

            return templateKeyId | (seatKeyId << 16);
        }

        public static int UnpackAudienceSeat(int imm) => (imm >> 16) & MaxKeyId;
    }
}
