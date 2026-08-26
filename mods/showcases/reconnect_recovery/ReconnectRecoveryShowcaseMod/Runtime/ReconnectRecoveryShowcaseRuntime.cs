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
    private WorldSaveSnapshot? _authorityLive;
    private bool _checkpointRequested;
    private bool _disconnected;
    private int _authorityTick;
    private int _clientTick;
    private int _disconnectTick;
    private long _nextSeq;
    private string _status = "先打检查点，再断线。页眉已标明：单机模拟。";
    private string? _error;
    private string _recoverySource = "live";
    private string _ablation = "权威恢复";
    private string _lastFault = "-";
    private string _timeline = "双侧时间线：连线中";

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
            _timeline = "双侧时间线：同步";
        }
        else
        {
            _authorityTick = engine.GameSession.CurrentTick;
            _timeline = $"断线区间高亮：客户端冻在 {_disconnectTick}，权威走到 {_authorityTick}";
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
        _clientTick = engine.GameSession.CurrentTick;
        _disconnectTick = _clientTick;
        _recoverySource = "disconnected";
        _timeline = $"断线区间开始：客户端冻在 {_disconnectTick}";
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
        _authorityLive = new WorldSnapshotService().Capture(
            engine,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        _authorityStream.Append(new AuthoritativeFrame(_nextSeq++, _authorityTick, Array.Empty<AuthoritativeAction>()));
        _timeline = $"断线区间：客户端 {_disconnectTick} … 权威 {_authorityTick}（已记 seq={_nextSeq - 1}）";
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

            // Catch up to live authority — do NOT rewind to the pre-disconnect checkpoint.
            // Single-process theater: the running world already is authority; if we captured
            // live snapshots during AdvanceAuthority, restore the latest so reconnect proves
            // "server fact" even after a local-reset ablation attempt.
            if (_authorityLive != null)
            {
                new WorldRestoreService().Restore(engine, _authorityLive);
            }

            _disconnected = false;
            _ablation = "权威恢复";
            string digest = WorldDigestLens.Short(WorldDigestLens.FromEngine(engine));
            int missed = (int)_authorityStream.NextSequence;
            _recoverySource =
                $"authority live tick={engine.GameSession.CurrentTick} digest={digest} missedFrames={missed} sinceCheckpoint={_checkpoint.Header.Tick}";
            _clientTick = engine.GameSession.CurrentTick;
            _authorityTick = _clientTick;
            _timeline = $"重连补齐：客户端从 {_disconnectTick} 追到 {_clientTick}（权威事实）";
            SetStatus($"权威恢复：{_recoverySource}。错过的权威演化已补齐，不是倒回检查点。");
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
            _timeline = "消融：本地重置，双侧回到出厂";
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
            Timeline: _timeline,
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
        _authorityLive = null;
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
