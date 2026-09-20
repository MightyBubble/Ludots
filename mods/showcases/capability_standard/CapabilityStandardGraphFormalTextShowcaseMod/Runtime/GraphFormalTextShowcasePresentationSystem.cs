using Arch.Core;
using Arch.System;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardGraphFormalTextShowcaseMod.Runtime;

internal sealed class GraphFormalTextShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly QueryDescription _named = new QueryDescription().WithAll<Name>();
    private string? _fixedLine;
    private string? _countLine;

    public GraphFormalTextShowcasePresentationSystem(GameEngine engine, ScreenOverlayBuffer overlay)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!string.Equals(
                _engine.CurrentMapSession?.MapConfig?.Id,
                GraphFormalTextShowcaseContract.MapId,
                StringComparison.Ordinal))
        {
            return;
        }

        DrainPresentationTextSink();

        Entity guard = FindNamed(_engine.World, GraphFormalTextShowcaseContract.GuardName);
        if (guard == Entity.Null)
        {
            GraphShowcaseStagePresenter.DrawPlayerCaption(
                _overlay,
                GraphFormalTextShowcaseContract.PlayerTitle,
                "场上还没有倒下的守卫，短剧开不了。");
            return;
        }

        if (_fixedLine == null || _countLine == null)
        {
            GraphShowcaseStagePresenter.DrawPlayerCaption(
                _overlay,
                GraphFormalTextShowcaseContract.PlayerTitle,
                "拼句图还没把字幕送到出口。");
            return;
        }

        GraphShowcaseStagePresenter.DrawPlayerCaption(
            _overlay,
            GraphFormalTextShowcaseContract.PlayerTitle,
            $"{_fixedLine}；{_countLine}");
    }

    private void DrainPresentationTextSink()
    {
        GasGraphRuntimeApi api = _engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
            ?? throw new InvalidOperationException("拼句字幕短剧需要图运行时 API。");
        GraphPresentationTextSink sink = api.PresentationTextSink
            ?? throw new InvalidOperationException(
                "拼句字幕短剧需要已绑定的 PresentationTextSink，不能空操作假装上字幕。");

        while (sink.TryDequeue(out GraphPresentationTextSurface surface, out string text))
        {
            if (surface != GraphPresentationTextSurface.Subtitle)
            {
                throw new InvalidOperationException(
                    $"拼句字幕短剧只收 Subtitle 出口，收到了 {surface}：「{text}」。");
            }

            if (string.Equals(text, GraphFormalTextShowcaseContract.FixedSentence, StringComparison.Ordinal))
            {
                _fixedLine = text;
            }
            else if (string.Equals(text, GraphFormalTextShowcaseContract.CountSentence, StringComparison.Ordinal))
            {
                _countLine = text;
            }
            else
            {
                throw new InvalidOperationException(
                    $"拼句字幕短剧收到意外字幕「{text}」，不是约定的两句。");
            }
        }
    }

    private Entity FindNamed(World world, string entityName)
    {
        Entity result = Entity.Null;
        world.Query(in _named, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });
        return result;
    }
}
