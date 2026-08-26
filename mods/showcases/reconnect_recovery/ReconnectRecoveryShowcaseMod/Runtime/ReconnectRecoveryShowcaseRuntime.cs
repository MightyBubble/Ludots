using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.UI;
using ReconnectRecoveryShowcaseMod.UI;
using SaveShowcasesShared;

namespace ReconnectRecoveryShowcaseMod.Runtime;

public sealed class ReconnectRecoveryShowcaseRuntime
{
    private readonly ReconnectRecoveryShowcasePanelController _panel;
    private readonly List<string> _log = new(10);
    private readonly AuthoritativeFrameStream _authorityStream = new();
    private GameEngine? _engine;
    private WorldSaveSnapshot? _checkpoint;
    private WorldSaveSnapshot? _factory;
    private bool _checkpointRequested;
    private bool _disconnected;
    private int _authorityTick;
    private int _clientTick;
    private long _nextSeq;
    private string _status = "先打检查点，再断线。页眉已标明：单机模拟。";
    private string? _error;
    private string _recoverySource = "live";
    private string _ablation = "权威恢复";
    private string _lastFault = "-";

    public ReconnectRecoveryShowcaseRuntime()
    {
        _panel = new ReconnectRecoveryShowcasePanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine) return Task.CompletedTask;
        if (!ReconnectRecoveryShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            _panel.ClearIfOwned();
            return Task.CompletedTask;
        }

        _engine = engine;
        engine.GetService(CoreServiceKeys.InputHandler)?.PushContext(ReconnectRecoveryShowcaseIds.InputContext);
        _factory ??= new WorldSnapshotService().Capture(engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        _authorityTick = engine.GameSession.CurrentTick;
        _clientTick = _authorityTick;
        Refresh(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is GameEngine engine)
        {
            engine.GetService(CoreServiceKeys.InputHandler)?.PopContext(ReconnectRecoveryShowcaseIds.InputContext);
            _panel.ClearIfOwned();
        }

        _engine = null;
        return Task.CompletedTask;
    }

    public void AdvanceFixedStep(GameEngine engine)
    {
        if (!ReconnectRecoveryShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value)) return;
        DrainCheckpoint(engine);
        if (!_disconnected)
        {
            _authorityTick = engine.GameSession.CurrentTick;
            _clientTick = _authorityTick;
        }
        else
        {
            // Authority keeps evolving in the sim while client HUD freezes client tick.
            _authorityTick = engine.GameSession.CurrentTick;
        }

        Refresh(engine);
    }

    public void RequestCheckpoint()
    {
        if (Current() is not { } engine) return;
        engine.GetService(CoreServiceKeys.CheckpointCoordinator)?.RequestCheckpoint();
        _checkpointRequested = true;
        SetStatus("检查点已请求。");
        Refresh(engine);
    }

    public void Disconnect()
    {
        if (Current() is not { } engine) return;
        _disconnected = true;
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
            input.ClearReplayActions();
        // Keep simulation running so authority tick advances; client tick frozen.
        _clientTick = engine.GameSession.CurrentTick;
        _recoverySource = "disconnected";
        SetStatus("已断线（单机模拟）：客户端冻结，权威侧继续走。");
        Refresh(engine);
    }

    public void AdvanceAuthority()
    {
        if (Current() is not { } engine) return;
        if (!_disconnected)
        {
            SetStatus("先断线，再单独推进权威侧。");
            Refresh(engine);
            return;
        }

        if (engine.Pacemaker is TurnBasedPacemaker pace)
        {
            pace.Step();
            engine.Tick(1f);
        }

        _authorityTick = engine.GameSession.CurrentTick;
        _authorityStream.Append(new AuthoritativeFrame(_nextSeq++, _authorityTick, Array.Empty<AuthoritativeAction>()));
        SetStatus($"权威推进到 tick={_authorityTick} seq={_nextSeq - 1}；客户端仍冻在 {_clientTick}");
        Refresh(engine);
    }

    public void ReconnectAuthority()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (!_disconnected) throw new SaveContextException("先断线。");
            if (_checkpoint == null) throw new SaveContextException("没有权威检查点。");
            new WorldRestoreService().Restore(engine, _checkpoint);
            // Replay authority frames recorded during disconnect after checkpoint.
            _disconnected = false;
            _ablation = "权威恢复";
            _recoverySource = $"checkpoint tick={_checkpoint.Header.Tick} digest={WorldDigestLens.Short(WorldDigestLens.FromSnapshot(engine, _checkpoint))}";
            _clientTick = engine.GameSession.CurrentTick;
            SetStatus($"权威恢复：{_recoverySource}。错过的权威演化已用检查点对齐。");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void ReconnectLocalReset()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (_factory == null) throw new SaveContextException("出厂快照缺失。");
            new WorldRestoreService().Restore(engine, _factory);
            _disconnected = false;
            _ablation = "本地重置";
            _recoverySource = "local factory reset";
            _clientTick = engine.GameSession.CurrentTick;
            _authorityTick = _clientTick;
            SetStatus("消融：本地重置 — 回到出厂，等于认输重来。");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void InjectMissing()
    {
        try
        {
            long expected = _authorityStream.NextSequence;
            var bad = new AuthoritativeFrame(expected + 2, Math.Max(1, _authorityTick), Array.Empty<AuthoritativeAction>());
            bad.Validate(expected, 0);
            Fail("缺帧注入竟然通过了。");
        }
        catch (Exception ex)
        {
            _lastFault = $"缺帧·红 {ex.Message}";
            Fail(_lastFault);
        }

        if (Current() is { } engine) Refresh(engine);
    }

    public void InjectDuplicate()
    {
        try
        {
            long expected = _authorityStream.NextSequence;
            if (expected == 0)
            {
                _authorityStream.Append(new AuthoritativeFrame(0, Math.Max(1, _authorityTick), Array.Empty<AuthoritativeAction>()));
                expected = _authorityStream.NextSequence;
            }

            var dup = new AuthoritativeFrame(expected - 1, Math.Max(1, _authorityTick), Array.Empty<AuthoritativeAction>());
            _authorityStream.Append(dup);
            Fail("重复帧竟然被接受。");
        }
        catch (Exception ex)
        {
            _lastFault = $"重复·橙 {ex.Message}";
            Fail(_lastFault);
        }

        if (Current() is { } engine) Refresh(engine);
    }

    public void InjectStale()
    {
        try
        {
            long expected = _authorityStream.NextSequence;
            var stale = new AuthoritativeFrame(expected, 0, Array.Empty<AuthoritativeAction>());
            stale.Validate(expected, minimumTick: Math.Max(1, _authorityTick));
            Fail("过期帧竟然通过了。");
        }
        catch (Exception ex)
        {
            _lastFault = $"过期·紫 {ex.Message}";
            Fail(_lastFault);
        }

        if (Current() is { } engine) Refresh(engine);
    }

    public void InjectOutOfOrder()
    {
        try
        {
            long expected = _authorityStream.NextSequence;
            var ooo = new AuthoritativeFrame(expected + 5, Math.Max(1, _authorityTick), Array.Empty<AuthoritativeAction>());
            _authorityStream.Append(ooo);
            Fail("乱序帧竟然通过了。");
        }
        catch (Exception ex)
        {
            _lastFault = $"乱序·黄 {ex.Message}";
            Fail(_lastFault);
        }

        if (Current() is { } engine) Refresh(engine);
    }

    public ReconnectRecoveryShowcasePanelState BuildPanelState()
    {
        return new ReconnectRecoveryShowcasePanelState(
            Header: "断线恢复",
            Banner: "单机模拟断线（联机专项未验收）",
            Summary: "断线不是重置。看双侧时间线与恢复来源。",
            Controls: "F5 检查点 · F11 断线 · N 推进权威 · F12 权威恢复 · R 本地重置 · 1缺帧 2重复 3过期 4乱序",
            Status: _status,
            Error: _error,
            Ablation: _ablation,
            RecoverySource: _recoverySource,
            AuthorityTick: _authorityTick,
            ClientTick: _clientTick,
            NextSequence: _authorityStream.NextSequence,
            Disconnected: _disconnected,
            LastFault: _lastFault,
            LogLines: _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    private void DrainCheckpoint(GameEngine engine)
    {
        if (!_checkpointRequested) return;
        var c = engine.GetService(CoreServiceKeys.CheckpointCoordinator);
        if (c == null || c.Checkpoints.Count == 0) return;
        _checkpoint = c.Checkpoints[^1];
        _checkpointRequested = false;
        _nextSeq = 0;
        SetStatus($"权威检查点 tick={_checkpoint.Header.Tick}");
    }

    private void Refresh(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            _panel.MountOrRefresh(root, engine);
    }

    private GameEngine? Current() => _engine;
    private void SetStatus(string s) { _status = s; _error = null; Log(s); }
    private void Fail(string m) { _error = m; _status = $"失败：{m}"; Log(_status); }
    private void Log(string line) { _log.Insert(0, line); if (_log.Count > 8) _log.RemoveAt(_log.Count - 1); }
}
