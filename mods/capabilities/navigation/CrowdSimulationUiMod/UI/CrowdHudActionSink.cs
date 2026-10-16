using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// HUD 控件按下的动作走本机唯一座位的输入通道，和键盘同一条链。群体演示只配一个本地座位；
/// 座位不止一个或没有座位时，点控件就报错，不猜该给谁。
/// </summary>
internal sealed class CrowdHudActionSink
{
    private readonly GameEngine _engine;
    private readonly ClientLocalSeatActionEdges _edges;

    public CrowdHudActionSink(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _edges = new ClientLocalSeatActionEdges(
            () => engine.TryGetService(CoreServiceKeys.ClientLocalSeatInputRuntime, out var runtime) ? runtime : null,
            () => engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out var seats) ? seats : null,
            () => engine.TryGetService(CoreServiceKeys.InputHandler, out var handler) ? handler : null);
    }

    public int PendingReleases => _edges.PendingReleases;

    public void Press(string actionId)
    {
        if (!_engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? seats)
            || seats == null
            || !seats.TryGetSoleSeat(out ClientLocalSeat seat))
        {
            throw new InvalidOperationException("群体 HUD 只给唯一的本地座位注入动作；当前没有本地座位或不止一个。");
        }

        var handler = _edges.ResolveHandler(seat.SeatId)
            ?? throw new InvalidOperationException($"本地座位 {seat.SeatId} 没有输入通道。");
        _edges.Press(handler, actionId);
    }

    /// <summary>每个 HUD 帧调一次，按住满两次更新的动作在这里松开。</summary>
    public void Update() => _edges.Update();
}
