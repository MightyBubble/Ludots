using System.Diagnostics;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 把“设成第 k 项”翻译成若干次循环键或加减键。演示只认按键动作，没有“设为某值”的命令，
/// 所以每帧看一次读数：到了就停；上一次按下还没生效就等（最多 <see cref="SettleMs"/>）；否则再按一次。
/// 按键次数有上限，到不了的目标（比如循环里没有的组合）按满上限就放弃。读数用构造期建好的委托取，每帧不分配。
/// </summary>
internal sealed class CrowdHudStepper
{
    public const int SettleMs = 400;

    private static readonly long SettleTicks = SettleMs * Stopwatch.Frequency / 1000;

    private readonly CrowdHudActionSink _sink;
    private Func<int>? _observe;
    private string? _up;
    private string? _down;
    private string? _then;
    private int _target;
    private int _pressesLeft;
    private int _lastSeen;
    private int _lastDirection;
    private long _pressedAt;
    private bool _waiting;

    public CrowdHudStepper(CrowdHudActionSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public bool Active => _observe != null;

    /// <summary>循环键：每按一次前进一项，按到读数等于 <paramref name="target"/>，再按一次 <paramref name="then"/>（可空）。</summary>
    public void Cycle(string action, Func<int> observe, int target, int maxPresses, string? then = null)
    {
        Start(observe, target, maxPresses);
        _up = action;
        _down = null;
        _then = then;
    }

    /// <summary>加减键：读数小于目标按加，大于按减；越过目标（方向反了）就停在当前值。</summary>
    public void Seek(string down, string up, Func<int> observe, int target, int maxPresses)
    {
        Start(observe, target, maxPresses);
        _up = up;
        _down = down;
        _then = null;
    }

    public void Cancel()
    {
        _observe = null;
        _then = null;
    }

    /// <param name="now"><see cref="Stopwatch.GetTimestamp"/> 刻度。</param>
    public void Step(long now)
    {
        if (_observe == null) return;
        int value = _observe();
        if (value == _target)
        {
            string? then = _then;
            Cancel();
            if (then != null) _sink.Press(then);
            return;
        }

        if (_waiting && value == _lastSeen && now - _pressedAt < SettleTicks) return;
        if (_sink.PendingReleases > 0) return;
        int direction = _down == null || value < _target ? 1 : -1;
        if (_pressesLeft <= 0 || (_down != null && _lastDirection != 0 && direction != _lastDirection))
        {
            Cancel();
            return;
        }

        _sink.Press(direction > 0 ? _up! : _down!);
        _pressesLeft--;
        _lastSeen = value;
        _lastDirection = direction;
        _pressedAt = now;
        _waiting = true;
    }

    private void Start(Func<int> observe, int target, int maxPresses)
    {
        _observe = observe ?? throw new ArgumentNullException(nameof(observe));
        _target = target;
        _pressesLeft = maxPresses;
        _lastDirection = 0;
        _waiting = false;
    }
}
