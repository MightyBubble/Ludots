namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 参考实现的 mulberry32 确定性随机流(noise.js 逐位移植):每条指令从自己加盐的流取值,
/// 输出 u32 / 2³²——二进分数,在 Fix64 上精确表示,两端生成的部署逐位一致。
/// </summary>
public sealed class CrowdSimRng
{
    private uint _a;

    private CrowdSimRng(uint seed) => _a = seed;

    public static CrowdSimRng Create(long seed) => new((uint)seed);

    /// <summary>返回 [0, 1) 的确定性浮点(u32 / 2³²,Fix64 可精确表示)。</summary>
    public Ludots.Core.Mathematics.FixedPoint.Fix64 Next()
    {
        _a += 0x6d2b79f5;
        uint t = _a ^ (_a >> 15);
        t *= 1 | _a;
        t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
        uint v = t ^ (t >> 14);
        // v / 2^32:二进分数,直接进定点原始值
        return Ludots.Core.Mathematics.FixedPoint.Fix64.FromRaw(v);
    }
}
