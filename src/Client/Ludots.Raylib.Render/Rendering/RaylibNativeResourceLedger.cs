namespace Ludots.Raylib.Render;

public enum RaylibNativeResourceKind
{
    Texture = 0,
    RenderTexture,
    Model,
    Mesh,
    Shader,
    Material,
    Sound,
    SoundAlias,
    KindCount,
}

public readonly record struct RaylibNativeResourceSnapshot(
    long ResidentBytes,
    int OutstandingCount,
    int[] OutstandingByKind,
    int LifetimeTracked,
    int LifetimeUntracked,
    int RetrackedCount,
    int UnknownUntrackCount);

/// <summary>
/// raylib 原生资源（纹理/模型/网格/着色器/声音）驻留字节与计数的进程级台账。
/// 只登记不拥有：调用方在资源创建后 Track、释放前 Untrack；字节是估算值，用于预算与回归信号，不是精确显存。
/// 身份失配不抛异常（账本不得改变渲染行为），以计数器暴露，由测试与诊断消费。
/// </summary>
public static class RaylibNativeResourceLedger
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(RaylibNativeResourceKind Kind, ulong Identity), long> Outstanding = new();
    private static readonly int[] OutstandingCounts = new int[(int)RaylibNativeResourceKind.KindCount];
    private static long _residentBytes;
    private static int _lifetimeTracked;
    private static int _lifetimeUntracked;
    private static int _retrackedCount;
    private static int _unknownUntrackCount;

    public static void Track(RaylibNativeResourceKind kind, ulong identity, long estimatedBytes)
    {
        lock (Gate)
        {
            if (Outstanding.TryGetValue((kind, identity), out long previousBytes))
            {
                _retrackedCount++;
                _residentBytes += estimatedBytes - previousBytes;
                Outstanding[(kind, identity)] = estimatedBytes;
                return;
            }

            Outstanding[(kind, identity)] = estimatedBytes;
            OutstandingCounts[(int)kind]++;
            _residentBytes += estimatedBytes;
            _lifetimeTracked++;
        }
    }

    public static void Untrack(RaylibNativeResourceKind kind, ulong identity)
    {
        lock (Gate)
        {
            if (!Outstanding.TryGetValue((kind, identity), out long bytes))
            {
                _unknownUntrackCount++;
                return;
            }

            Outstanding.Remove((kind, identity));
            _residentBytes -= bytes;
            OutstandingCounts[(int)kind]--;
            _lifetimeUntracked++;
        }
    }

    public static RaylibNativeResourceSnapshot Snapshot()
    {
        lock (Gate)
        {
            return new RaylibNativeResourceSnapshot(
                _residentBytes,
                Outstanding.Count,
                (int[])OutstandingCounts.Clone(),
                _lifetimeTracked,
                _lifetimeUntracked,
                _retrackedCount,
                _unknownUntrackCount);
        }
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            Outstanding.Clear();
            Array.Clear(OutstandingCounts);
            _residentBytes = 0;
            _lifetimeTracked = 0;
            _lifetimeUntracked = 0;
            _retrackedCount = 0;
            _unknownUntrackCount = 0;
        }
    }
}
