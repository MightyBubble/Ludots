using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Ludots.Adapter.Raylib;

internal sealed class RaylibWindowRepaintGuard
{
    private const int InvalidationPaddingPx = 32;
    private const int FlushFramesAfterMove = 2;

    private readonly IDesktopRegionInvalidator _invalidator;
    private DesktopRect _lastRect;
    private bool _hasLastRect;
    private int _pendingFlushFrames;

    public RaylibWindowRepaintGuard()
        : this(WindowsDesktopRegionInvalidator.Instance)
    {
    }

    internal RaylibWindowRepaintGuard(IDesktopRegionInvalidator invalidator)
    {
        _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
    }

    public bool ObserveWindowRect(IntPtr nativeWindowHandle, Vector2 fallbackPosition, int width, int height)
    {
        DesktopRect current = _invalidator.TryGetWindowRect(nativeWindowHandle, out DesktopRect nativeRect)
            ? nativeRect
            : DesktopRect.FromPositionSize(fallbackPosition, width, height);

        if (!_hasLastRect)
        {
            _lastRect = current;
            _hasLastRect = true;
            return false;
        }

        if (current == _lastRect)
        {
            return false;
        }

        DesktopRect invalidRegion = DesktopRect.Union(_lastRect, current).Inflate(InvalidationPaddingPx);
        _invalidator.Invalidate(in invalidRegion);
        _lastRect = current;
        _pendingFlushFrames = FlushFramesAfterMove;
        return true;
    }

    public void AfterPresent()
    {
        if (_pendingFlushFrames <= 0)
        {
            return;
        }

        _invalidator.Flush();
        _pendingFlushFrames--;
    }

    internal interface IDesktopRegionInvalidator
    {
        bool TryGetWindowRect(IntPtr nativeWindowHandle, out DesktopRect rect);

        void Invalidate(in DesktopRect rect);

        void Flush();
    }

    internal readonly record struct DesktopRect(int Left, int Top, int Right, int Bottom)
    {
        public static DesktopRect FromPositionSize(Vector2 position, int width, int height)
        {
            int left = (int)MathF.Floor(position.X);
            int top = (int)MathF.Floor(position.Y);
            int right = left + Math.Max(1, width);
            int bottom = top + Math.Max(1, height);
            return new DesktopRect(left, top, right, bottom);
        }

        public static DesktopRect Union(in DesktopRect a, in DesktopRect b)
        {
            return new DesktopRect(
                Math.Min(a.Left, b.Left),
                Math.Min(a.Top, b.Top),
                Math.Max(a.Right, b.Right),
                Math.Max(a.Bottom, b.Bottom));
        }

        public DesktopRect Inflate(int padding)
        {
            if (padding <= 0)
            {
                return this;
            }

            return new DesktopRect(
                Left - padding,
                Top - padding,
                Right + padding,
                Bottom + padding);
        }
    }

    private sealed class WindowsDesktopRegionInvalidator : IDesktopRegionInvalidator
    {
        public static readonly WindowsDesktopRegionInvalidator Instance = new();

        private const uint RedrawInvalidate = 0x0001;
        private const uint RedrawErase = 0x0004;
        private const uint RedrawAllChildren = 0x0080;
        private const uint RedrawUpdateNow = 0x0100;

        private WindowsDesktopRegionInvalidator()
        {
        }

        public bool TryGetWindowRect(IntPtr nativeWindowHandle, out DesktopRect rect)
        {
            rect = default;
            if (!OperatingSystem.IsWindows() || nativeWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(nativeWindowHandle, out RECT nativeRect))
            {
                return false;
            }

            rect = new DesktopRect(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            return true;
        }

        public void Invalidate(in DesktopRect rect)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            RECT nativeRect = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
            _ = RedrawWindow(
                IntPtr.Zero,
                ref nativeRect,
                IntPtr.Zero,
                RedrawInvalidate | RedrawErase | RedrawAllChildren | RedrawUpdateNow);
        }

        public void Flush()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            _ = DwmFlush();
        }

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool RedrawWindow(IntPtr hWnd, ref RECT lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmFlush();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }
    }
}
