using System.Runtime.InteropServices;

internal sealed unsafe class ProbeGpuTimer : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Queries(int count, uint* ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void QueryCounter(uint id, uint target);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetQuery(uint id, uint pname, ulong* value);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll", EntryPoint = "glFinish")] public static extern void Finish();

    private readonly uint[] _ids;
    private readonly bool[] _used;
    private readonly double[] _results;
    private readonly QueryCounter _counter;
    private readonly GetQuery _get;
    private readonly Queries _delete;
    private int _active = -1;
    private bool _collected;
    private bool _disposed;

    public ProbeGpuTimer(int frames)
    {
        if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        _ids = new uint[checked(frames * 6)];
        _used = new bool[checked(frames * 3)];
        _results = new double[_used.Length];
        _counter = Load<QueryCounter>("glQueryCounter");
        _get = Load<GetQuery>("glGetQueryObjectui64v");
        _delete = Load<Queries>("glDeleteQueries");
        var generate = Load<Queries>("glGenQueries");
        fixed (uint* ids = _ids) generate(_ids.Length, ids);
    }

    private static T Load<T>(string name) where T : Delegate
    {
        IntPtr pointer = wglGetProcAddress(name);
        if (pointer == IntPtr.Zero || pointer.ToInt64() is 1 or 2 or 3 or -1)
            throw new InvalidOperationException("Required GPU timing API missing: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public void Start(int frame, int stage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int slot = checked(frame * 3 + stage);
        if ((uint)stage >= 3 || (uint)slot >= (uint)_used.Length || _used[slot] || _active != -1 || _collected)
            throw new InvalidOperationException("GPU timing capacity or pass sequence violated.");
        _used[slot] = true;
        _active = slot;
        _counter(_ids[slot * 2], 0x8E28);
    }

    public void End()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active < 0) throw new InvalidOperationException("No GPU timing pass is active.");
        _counter(_ids[_active * 2 + 1], 0x8E28);
        _active = -1;
    }

    public void Collect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active != -1 || _collected) throw new InvalidOperationException("Invalid GPU collection sequence.");
        for (int slot = 0; slot < _used.Length; slot++)
        {
            if (!_used[slot]) throw new InvalidOperationException("A required frame/pass was not measured.");
            ulong start, end;
            _get(_ids[slot * 2], 0x8866, &start);
            _get(_ids[slot * 2 + 1], 0x8866, &end);
            if (end < start) throw new InvalidOperationException("GPU timestamps reversed.");
            _results[slot] = (end - start) / 1_000_000d;
        }
        _collected = true;
    }

    public double Get(int frame, int stage)
    {
        if (!_collected) throw new InvalidOperationException("GPU results must be collected first.");
        return _results[checked(frame * 3 + stage)];
    }

    public void Dispose()
    {
        if (_disposed) return;
        fixed (uint* ids = _ids) _delete(_ids.Length, ids);
        _disposed = true;
    }
}
