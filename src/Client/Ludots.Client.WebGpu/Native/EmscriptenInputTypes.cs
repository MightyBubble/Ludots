using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct EmscriptenKeyboardEvent
{
    internal double Timestamp;
    internal uint Location;
    internal int CtrlKey;
    internal int ShiftKey;
    internal int AltKey;
    internal int MetaKey;
    internal int Repeat;
    internal uint CharCode;
    internal uint KeyCode;
    internal uint Which;
    internal fixed byte Key[32];
    internal fixed byte Code[32];
    internal fixed byte CharValue[32];
    internal fixed byte Locale[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct EmscriptenMouseEvent
{
    internal double Timestamp;
    internal int ScreenX;
    internal int ScreenY;
    internal int ClientX;
    internal int ClientY;
    internal int CtrlKey;
    internal int ShiftKey;
    internal int AltKey;
    internal int MetaKey;
    internal ushort Button;
    internal ushort Buttons;
    internal int MovementX;
    internal int MovementY;
    internal int TargetX;
    internal int TargetY;
    internal int CanvasX;
    internal int CanvasY;
    internal int Padding;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EmscriptenWheelEvent
{
    internal EmscriptenMouseEvent Mouse;
    internal double DeltaX;
    internal double DeltaY;
    internal double DeltaZ;
    internal uint DeltaMode;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct EmscriptenFocusEvent
{
    internal fixed byte NodeName[128];
    internal fixed byte Id[128];
}
