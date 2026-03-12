using System;

namespace Ludots.Core.Presentation.Components
{
    public struct PresentationStartupPerformers
    {
        public const int MaxCount = 8;

        public byte Count;
        public int Performer0;
        public int Performer1;
        public int Performer2;
        public int Performer3;
        public int Performer4;
        public int Performer5;
        public int Performer6;
        public int Performer7;

        public void Set(int index, int performerId)
        {
            switch (index)
            {
                case 0: Performer0 = performerId; break;
                case 1: Performer1 = performerId; break;
                case 2: Performer2 = performerId; break;
                case 3: Performer3 = performerId; break;
                case 4: Performer4 = performerId; break;
                case 5: Performer5 = performerId; break;
                case 6: Performer6 = performerId; break;
                case 7: Performer7 = performerId; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), $"Startup performer index must be in [0, {MaxCount - 1}].");
            }
        }

        public readonly int Get(int index)
        {
            return index switch
            {
                0 => Performer0,
                1 => Performer1,
                2 => Performer2,
                3 => Performer3,
                4 => Performer4,
                5 => Performer5,
                6 => Performer6,
                7 => Performer7,
                _ => throw new ArgumentOutOfRangeException(nameof(index), $"Startup performer index must be in [0, {MaxCount - 1}]."),
            };
        }
    }
}
