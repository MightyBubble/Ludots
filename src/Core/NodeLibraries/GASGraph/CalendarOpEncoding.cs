using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// ReadCalendarCycle* packs two ConfigKeyRegistry ids into Imm:
    /// cycle in the low 16 bits (required), calendar in the high 16 bits (0 = active calendar).
    /// ReadCalendarYear uses <see cref="CalendarAuthoredFlag"/> on Flags while the symbol is
    /// still an index; after patch, Imm is the calendar key id and 0 means the active calendar.
    /// </summary>
    internal static class CalendarOpEncoding
    {
        public const byte CalendarAuthoredFlag = 1;
        public const int MaxKeyId = 0xFFFF;

        public static int Pack(int cycleKeyId, int calendarKeyId)
        {
            if ((uint)(cycleKeyId - 1) > MaxKeyId - 1 || (uint)calendarKeyId > MaxKeyId)
            {
                throw new InvalidOperationException(
                    $"Calendar op key ids out of range (cycle={cycleKeyId}, calendar={calendarKeyId}).");
            }

            return cycleKeyId | (calendarKeyId << 16);
        }

        public static int UnpackCycle(int imm) => imm & MaxKeyId;

        public static int UnpackCalendar(int imm) => (imm >> 16) & MaxKeyId;
    }
}
