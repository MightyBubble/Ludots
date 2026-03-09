using System.Numerics;

namespace Ludots.Core.Input.Selection
{
    public readonly struct SelectionInputFrame
    {
        public SelectionInputFrame(
            Vector2 pointerScreen,
            bool primaryDown,
            bool pressedThisFrame,
            bool releasedThisFrame,
            SelectionApplyMode applyMode)
        {
            PointerScreen = pointerScreen;
            PrimaryDown = primaryDown;
            PressedThisFrame = pressedThisFrame;
            ReleasedThisFrame = releasedThisFrame;
            ApplyMode = applyMode;
        }

        public Vector2 PointerScreen { get; }
        public bool PrimaryDown { get; }
        public bool PressedThisFrame { get; }
        public bool ReleasedThisFrame { get; }
        public SelectionApplyMode ApplyMode { get; }
    }
}
