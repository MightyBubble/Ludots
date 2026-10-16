namespace Ludots.Core.CrowdSimulation.Units;

public enum CrowdSimNoticeKind : byte
{
    SpawnSkip = 1,
    EmptyOrderSelection = 2,
}

/// <summary>会话级演示通知(结构化;不进校验与遥测合计)。</summary>
public readonly struct CrowdSimNotice
{
    public CrowdSimNotice(CrowdSimNoticeKind kind, int tick, int player, int skipped, int requested)
    {
        Kind = kind;
        Tick = tick;
        Player = player;
        Skipped = skipped;
        Requested = requested;
    }

    public CrowdSimNoticeKind Kind { get; }
    public int Tick { get; }
    public int Player { get; }
    public int Skipped { get; }
    public int Requested { get; }
}
