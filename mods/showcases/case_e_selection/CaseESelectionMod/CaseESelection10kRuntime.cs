using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CaseESelectionMod;

internal sealed class CaseESelection10kRuntime
{
    internal const string MapId = "case_e_selection_10k_field";
    private const string SeatId = "seat.0";
    private const int EntityCount = 10_000;
    private const int GridSide = 100;
    private const int SpacingCm = 360;
    private const int HalfExtentCm = (GridSide - 1) * SpacingCm / 2;

    private readonly RuntimeEntitySpawnRequest[] _spawnScratch = new RuntimeEntitySpawnRequest[EntityCount];
    private ReactivePage<CaseESelection10kPanelState>? _page;
    private UiSurfaceLeaseHandle _lease;
    private GameEngine? _engine;
    private Entity _playerOneRep;
    private Entity _playerTwoRep;
    private int _activePlayerId = 1;
    private bool _spawnQueued;
    private string _status = "正在准备 10,000 名单位…";

    public Task OnMapLoadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || !string.Equals(context.Get(CoreServiceKeys.MapId).Value, MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _engine = engine;
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Case E 10k showcase requires an active map session.");
        _playerOneRep = session.PlayerEntityLookup.Get(1);
        _playerTwoRep = session.PlayerEntityLookup.Get(2);
        if (_playerOneRep == Entity.Null || _playerTwoRep == Entity.Null)
        {
            throw new InvalidOperationException("Case E 10k showcase requires player representatives 1 and 2.");
        }

        ClientLocalSeatAccess.RequireLogicViews(engine).EnsureDefaultView(_playerTwoRep);

        if (!_spawnQueued)
        {
            QueueUnits(engine, session.MapId);
            _spawnQueued = true;
            _status = "10,000 名单位已提交批量生成；索引会随实体进入世界自动收集。";
        }

        return Task.CompletedTask;
    }

    public Task OnMapUnloadedAsync(ScriptContext context)
    {
        if (string.Equals(context.Get(CoreServiceKeys.MapId).Value, MapId, StringComparison.Ordinal))
        {
            if (_engine != null) ReleaseSurface(_engine);
            _spawnQueued = false;
            _engine = null;
            _page = null;
            _lease = default;
        }

        return Task.CompletedTask;
    }

    public void UpdatePresentation(GameEngine engine)
    {
        if (!string.Equals(engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.Ordinal))
        {
            ReleaseSurface(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host)
        {
            return;
        }

        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine);
        ClientLocalSeat seat = seats.Require(SeatId);
        if (seat.PossessedPlayerId > 0)
        {
            _activePlayerId = seat.PossessedPlayerId;
        }

        EntityCollectionCounts counts = ReadCounts(engine, seat.PossessedRep);
        var state = new CaseESelection10kPanelState(
            _activePlayerId,
            EntityCount,
            counts.CandidateCount,
            counts.SelectedCount,
            _status);

        if (_page == null)
        {
            var text = (engine.GetService(CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer)
                ?? throw new InvalidOperationException("Case E panel requires UiTextMeasurer.");
            var images = (engine.GetService(CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider)
                ?? throw new InvalidOperationException("Case E panel requires UiImageSizeProvider.");
            _page = new ReactivePage<CaseESelection10kPanelState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state))
        {
            _page.SetState(_ => state);
        }

        host.PublishReactivePage(ref _lease,
            new UiSurfaceLeaseRequest("CaseESelection10k.Panel", UiSurfaceSegment.Overlay, priority: 50),
            _page);
    }

    private static EntityCollectionCounts ReadCounts(GameEngine engine, Entity owner)
    {
        if (owner == Entity.Null || engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore store)
        {
            return default;
        }

        int candidates = store.TryGetView(owner, "case_e.selectable", out EntityCollectionView candidateView)
            ? candidateView.Count : 0;
        int selected = store.TryGetView(owner, "selected", out EntityCollectionView selectedView)
            ? selectedView.Count : 0;
        return new EntityCollectionCounts(candidates, selected);
    }

    private void QueueUnits(GameEngine engine, MapId mapId)
    {
        RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Case E 10k showcase requires RuntimeEntitySpawnQueue.");
        uint state = 0xC0FFEEu;
        for (int i = 0; i < EntityCount; i++)
        {
            state = state * 1664525u + 1013904223u;
            int x = (int)(state % (uint)(GridSide * SpacingCm)) - HalfExtentCm;
            state = state * 1664525u + 1013904223u;
            int y = (int)(state % (uint)(GridSide * SpacingCm)) - HalfExtentCm;
            int playerId = i < EntityCount / 2 ? 1 : 2;
            _spawnScratch[i] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "case_e_marine",
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt(x, y),
                HasWorldPosition = 1,
                PlayerOwnerIdOverride = playerId,
                TeamIdOverride = playerId,
                HasFacing = 1,
            };
        }

        if (queue.EnqueueMany(_spawnScratch) != EntityCount)
        {
            throw new InvalidOperationException("Case E 10k showcase could not enqueue the complete spawn batch.");
        }
    }

    private void SwitchPlayer(int playerId)
    {
        if (_engine == null || playerId == _activePlayerId)
        {
            return;
        }

        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_engine);
        ClientLocalSeat seat = seats.Require(SeatId);
        Entity rep = playerId == 1 ? _playerOneRep : _playerTwoRep;
        if (rep == Entity.Null || !_engine.World.IsAlive(rep))
        {
            _status = $"玩家 {playerId} 的代表实体还没有进入世界。";
            return;
        }

        seats.SetPossession(SeatId, playerId, rep);
        LogicViewRegistry views = ClientLocalSeatAccess.RequireLogicViews(_engine);
        if (!views.TryGetDefaultViewId(rep, out string viewId))
        {
            throw new InvalidOperationException($"Player {playerId} has no presentation LogicView.");
        }

        if (seat.PresentBinding is PresentBinding binding)
        {
            seats.SetPresentBinding(SeatId, new PresentBinding(viewId, binding.NormalizedScreenRect, binding.PresentResolutionPx));
        }

        if (_engine.CurrentMapSession != null)
        {
            _engine.CurrentMapSession.LocalSeats = new[]
            {
                new ResolvedLocalSeatPossession(SeatId, playerId, rep, seat.ControlSchemeId),
            };
        }

        _activePlayerId = playerId;
        _status = $"已切换到玩家 {playerId}；框选结果按玩家分别保存。";
    }

    private UiElementBuilder BuildRoot(ReactiveContext<CaseESelection10kPanelState> context)
    {
        CaseESelection10kPanelState state = context.State;
        return Ui.Column(
                Ui.Card(
                        Ui.Text("Case E · 10k 框选场").FontSize(20f).Bold().Color("#F5F7FA"),
                        Ui.Text("滚轮缩放镜头；拖框选择当前玩家的单位。切换玩家后，同一块区域会显示另一份名单。")
                            .FontSize(12f).Color("#C7D0DD").WhiteSpace(UiWhiteSpace.Normal),
                        Ui.Row(
                            PlayerButton(1, state.ActivePlayerId == 1),
                            PlayerButton(2, state.ActivePlayerId == 2)).Gap(8f),
                        Ui.Text($"当前玩家：{state.ActivePlayerId}    世界单位：{state.WorldCount:N0}")
                            .FontSize(13f).Color("#8AD7FF"),
                        Ui.Text($"本人候选：{state.CandidateCount:N0}    已选：{state.SelectedCount:N0}")
                            .FontSize(13f).Color("#8DE3AE"),
                        Ui.Text(state.Status).FontSize(11f).Color("#F0C36B").WhiteSpace(UiWhiteSpace.Normal))
                    .Width(420f).Padding(16f).Gap(10f).Radius(8f)
                    .Background("#0B1520").Border(1f, ParseColor("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(20f)
            .Align(UiAlignItems.End).Justify(UiJustifyContent.Start).ZIndex(50);
    }

    private UiElementBuilder PlayerButton(int playerId, bool active)
        => Ui.Button($"玩家 {playerId}", _ => SwitchPlayer(playerId))
            .Id($"case-e-10k-player-{playerId}").Width(190f).Height(34f)
            .Background(active ? "#245E52" : "#203242").Color("#F5F7FA");

    private static UiColor ParseColor(string value)
        => UiColor.TryParse(value, out UiColor color) ? color : throw new InvalidOperationException(value);

    private void ReleaseSurface(GameEngine engine)
    {
        if (_lease.IsValid && engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
        {
            host.ReleaseLease(ref _lease);
        }
    }

    private readonly record struct EntityCollectionCounts(int CandidateCount, int SelectedCount);

    private readonly record struct CaseESelection10kPanelState(
        int ActivePlayerId, int WorldCount, int CandidateCount, int SelectedCount, string Status);
}
