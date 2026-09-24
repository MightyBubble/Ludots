using System;
using System.Runtime.InteropServices;

namespace Ludots.Raylib.Render;

public static unsafe class AuditGpuTimer
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GenQueries(int count, uint* ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BeginQuery(uint target, uint id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void EndQuery(uint target);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetQuery(uint id, uint pname, ulong* value);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    private static uint[] _ids = Array.Empty<uint>();
    private static bool[] _used = Array.Empty<bool>();
    private static double[] _results = Array.Empty<double>();
    private static BeginQuery? _begin;
    private static EndQuery? _end;
    private static GetQuery? _get;
    public static int FrameIndex;

    private static T Load<T>(string name) where T : Delegate
    {
        IntPtr pointer = wglGetProcAddress(name);
        if (pointer == IntPtr.Zero || pointer.ToInt64() is 1 or 2 or 3 or -1)
            throw new InvalidOperationException("Required GPU query API missing: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public static void Initialize(int frameCapacity)
    {
        _ids = new uint[checked(frameCapacity * 2)];
        _used = new bool[_ids.Length];
        _results = new double[_ids.Length];
        Array.Fill(_results, double.NaN);
        _begin = Load<BeginQuery>("glBeginQuery");
        _end = Load<EndQuery>("glEndQuery");
        _get = Load<GetQuery>("glGetQueryObjectui64v");
        var generate = Load<GenQueries>("glGenQueries");
        fixed (uint* ids = _ids) generate(_ids.Length, ids);
    }

    public static void Start(bool shadow)
    {
        if (_begin == null) return;
        int slot = checked(FrameIndex * 2 + (shadow ? 1 : 0));
        if ((uint)slot >= (uint)_ids.Length || _used[slot])
            throw new InvalidOperationException("GPU audit capacity or single-view pass contract exceeded.");
        _used[slot] = true;
        _begin(0x88BF, _ids[slot]);
    }

    public static void End()
    {
        if (_end != null) _end(0x88BF);
    }

    public static void Collect()
    {
        if (_get == null) return;
        for (int slot = 0; slot < _ids.Length; slot++)
        {
            if (!_used[slot]) continue;
            ulong nanoseconds;
            _get(_ids[slot], 0x8866, &nanoseconds);
            _results[slot] = nanoseconds / 1_000_000d;
        }
    }

    public static double Get(int frame, bool shadow) =>
        _results.Length == 0 ? double.NaN : _results[frame * 2 + (shadow ? 1 : 0)];
}
