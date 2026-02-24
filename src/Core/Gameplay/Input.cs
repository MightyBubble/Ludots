using System.Runtime.InteropServices;

namespace Ludots.Core.Gameplay
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PlayerInputFrame
    {
        /// <summary>
        /// Logic Tick number for synchronization validation.
        /// </summary>
        public int Tick;

        /// <summary>
        /// Generic digital buttons (64 bits).
        /// Meanings are defined by the upper-level InputMap.
        /// e.g., Bit0=Select, Bit1=Cancel, Bit2=Group1...
        /// </summary>
        public ulong Buttons;

        /// <summary>
        /// Generic analog axes (Fixed point or raw integer).
        /// e.g., [0]=CursorX, [1]=CursorY (World Space or Screen Space)
        /// </summary>
        public unsafe fixed int Axes[4];
    }

    public interface IInputSource
    {
        /// <summary>
        /// Get input data for the specified Tick.
        /// </summary>
        PlayerInputFrame GetInput(int tick);
    }
}
