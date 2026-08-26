using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.UI;
using DeterministicReplayShowcaseMod.UI;
using SaveShowcasesShared;

namespace DeterministicReplayShowcaseMod.Runtime;

public sealed class DeterministicReplayShowcaseRuntime
{
    private readonly DeterministicReplayShowcasePanelController _panel;
    private readonly List<string> _log = new(10);
    private readonly List<AuthoritativeAction> _actionBuffer = new(16);
    private readonly List<string> _hashRows = new(32);
    private GameEngine? _engine;
    private ReplayRecorder? _recorder;
    private ReplayArchive? _archive;
    private WorldSaveSnapshot? _checkpoint;
    private bool _checkpointRequested;
    private bool _recordingRequested;
    private bool _recording;
    private bool _playing;
    private bool _paused;
    private bool _frameQueued;
    private bool _snapshotAblation;
    private int _replayIndex;
    private int _replayFrames;
    private int _speedIndex;
    private int _framesUntilStep;
    private long _nextSequence;
    private string _status = "先检查点，再录制。播放时看两条指纹并排。";
    private string? _error;
    private string _recordingDigest = "-";
    private string _playbackDigest = "-";
    private string _compare = "未比较";
    private string _mode = "录制管线";

    private static readonly int[] Speeds = { 1, 2, 4 };

    public DeterministicReplayShowcaseRuntime()
    {
        _panel = new DeterministicReplayShowcasePanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine) return Task.CompletedTask;
        if (!DeterministicReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            _panel.ClearIfOwned();
            return Task.CompletedTask;
        }

        _engine = engine;
        engine.GetService(CoreServiceKeys.InputHandler)?.PushContext(DeterministicReplayShowcaseIds.InputContext);
        TryLoadArchive();
        Refresh(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is GameEngine engine)
        {
            engine.GetService(CoreServiceKeys.InputHandler)?.PopContext(DeterministicReplayShowcaseIds.InputContext);
            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
            {
                input.SetReplayInputIsolation(false);
            }

            _panel.ClearIfOwned();
        }

        _engine = null;
        return Task.CompletedTask;
    }

    public void AdvanceFixedStep(GameEngine engine)
    {
        if (!DeterministicReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value)) return;
        DrainCheckpoint(engine);
        if (_recording && _recorder != null && engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.CopyAuthoritativeActions(_actionBuffer);
            _recorder.Record(new AuthoritativeFrame(_nextSequence++, engine.GameSession.CurrentTick, _actionBuffer.ToArray()));
            _hashRows.Add($"rec tick={engine.GameSession.CurrentTick} digest={WorldDigestLens.Short(WorldDigestLens.FromEngine(engine))}");
        }

        if (_playing && !_paused)
        {
            if (_framesUntilStep > 0)
            {
                _framesUntilStep--;
            }
            else
            {
                AdvanceReplay(engine);
                _framesUntilStep = Speeds[_speedIndex] - 1;
            }
        }

        Refresh(engine);
    }

    public void RequestCheckpoint()
    {
        if (Current() is not { } engine) return;
        engine.GetService(CoreServiceKeys.CheckpointCoordinator)?.RequestCheckpoint();
        _checkpointRequested = true;
        SetStatus("已请求检查点，等待干净步。");
        Refresh(engine);
    }

    public void StartRecording()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (_recording || _recordingRequested) throw new SaveContextException("已在录制。");
            _recordingRequested = true;
            _checkpointRequested = true;
            engine.GetService(CoreServiceKeys.CheckpointCoordinator)?.RequestCheckpoint();
            SetStatus("录制将从新鲜检查点开始。");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void StopRecording()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (_recorder == null || !_recording) throw new SaveContextException("没有进行中的录制。");
            _archive = _recorder.BuildArchive();
            _recordingDigest = WorldDigestLens.FromEngine(engine);
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath())!);
            File.WriteAllBytes(ArchivePath(), new ReplayArchiveCodec().Encode(_archive));
            _recording = false;
            _compare = "已录制，待回放";
            SetStatus($"录制完成 {_archive.Frames.Count} 帧 → {ArchivePath()} schema={_archive.Header.SchemaVersion}");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void Play(bool fromMid = false)
    {
        if (Current() is not { } engine) return;
        try
        {
            if (_archive == null) throw new SaveContextException("先停止一段录制。");
            if (_recording) throw new SaveContextException("先停止录制。");
            if (_snapshotAblation)
            {
                _mode = "快照跳终点";
                new WorldRestoreService().Restore(engine, _archive.Checkpoint);
                // Jump to end without replaying frames — ablation contrast.
                _playing = false;
                _playbackDigest = WorldDigestLens.FromEngine(engine);
                _compare = "快照消融：只证明终点，不证明过程";
                _snapshotAblation = false;
                SetStatus(_compare);
                Refresh(engine);
                return;
            }

            _mode = "确定性回放";
            new WorldRestoreService().Restore(engine, _archive.Checkpoint);
            _replayIndex = fromMid ? Math.Max(1, _archive.Frames.Count / 2) : 0;
            _replayFrames = 0;
            _playing = true;
            _paused = false;
            _hashRows.Clear();
            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader replayInput)
            {
                replayInput.SetReplayInputIsolation(true);
            }

            // Fast-forward mid: apply frames before mid without UI
            while (_replayFrames < _replayIndex)
            {
                QueueFrame(engine);
                ConsumeQueued(engine);
            }

            QueueFrame(engine);
            SetStatus(fromMid ? $"从中途帧 {_replayIndex} 续播。" : "回放开始；输入已隔离。");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void TogglePause()
    {
        if (!_playing) { SetStatus("先播放再暂停。"); return; }
        _paused = !_paused;
        SetStatus(_paused ? "已暂停" : "继续播放");
        if (Current() is { } engine) Refresh(engine);
    }

    public void Step()
    {
        if (Current() is not { } engine) return;
        if (!_playing) { SetStatus("先播放再逐帧。"); Refresh(engine); return; }
        _paused = true;
        AdvanceReplay(engine);
        Refresh(engine);
    }

    public void Reset()
    {
        if (Current()?.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.Clear();
            input.SetReplayInputIsolation(false);
        }

        _playing = false;
        _paused = false;
        _replayIndex = 0;
        _replayFrames = 0;
        _frameQueued = false;
        _mode = "录制管线";
        SetStatus("已重置回放。");
        if (Current() is { } engine) Refresh(engine);
    }

    public void CycleSpeed()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        SetStatus($"播放速度 ×{Speeds[_speedIndex]}（不影响确定性）");
        if (Current() is { } engine) Refresh(engine);
    }

    public void JumpMid() => Play(fromMid: true);

    public void InjectDuringPlay()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (!_playing) throw new SaveContextException("只在回放中演示输入隔离。");
            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is not FrozenInputActionReader input)
            {
                throw new SaveContextException("权威输入服务不可用。");
            }

            if (!input.ReplayInputIsolation)
            {
                throw new SaveContextException("输入隔离未打开。");
            }

            // Attempting to clear isolation mid-play would pollute; we refuse the inject instead.
            Fail("回放输入隔离中：实时注入被拒绝，轨迹不被污染。");
            SetStatus("惊喜：播放中注入被拒绝。");
        }
        catch (Exception ex) { Fail(ex.Message); }
        Refresh(engine);
    }

    public void ToggleSnapshotAblation()
    {
        _snapshotAblation = !_snapshotAblation;
        SetStatus(_snapshotAblation
            ? "消融武装：下次播放将快照跳终点（不证过程）"
            : "消融关闭：下次播放走确定性回放");
        if (Current() is { } engine) Refresh(engine);
    }

    public DeterministicReplayShowcasePanelState BuildPanelState()
    {
        GameEngine? engine = Current();
        int tick = engine?.GameSession.CurrentTick ?? 0;
        int frames = _archive?.Frames.Count ?? _recorder?.FrameCount ?? 0;
        bool match = string.Equals(_recordingDigest, _playbackDigest, StringComparison.Ordinal)
                     && _recordingDigest != "-";

        return new DeterministicReplayShowcasePanelState(
            Header: "确定性回放",
            Summary: "同一段操作，重放一模一样。看两条指纹并排。",
            Controls: "F5 检查点 · F8 录 · F9 停 · F10 播 · F1 暂停 · F2 逐帧 · F3 重置 · F4 调速 · J 中途 · I 注入 · A 快照消融",
            Status: _status,
            Error: _error,
            Mode: _mode,
            ArchivePath: ArchivePath(),
            SchemaVersion: _archive?.Header.SchemaVersion ?? 0,
            Tick: tick,
            TotalFrames: frames,
            PlaybackIndex: _replayIndex,
            Speed: Speeds[_speedIndex],
            Recording: _recording,
            Playing: _playing,
            Paused: _paused,
            RecordingDigest: WorldDigestLens.Short(_recordingDigest),
            PlaybackDigest: WorldDigestLens.Short(_playbackDigest),
            Compare: match ? "一致（绿）" : _compare,
            HashRows: _hashRows.Count == 0 ? new[] { "录制/回放指纹将在此滚动" } : _hashRows.ToArray(),
            LogLines: _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    private void DrainCheckpoint(GameEngine engine)
    {
        if (!_checkpointRequested) return;
        var coordinator = engine.GetService(CoreServiceKeys.CheckpointCoordinator);
        if (coordinator == null || coordinator.Checkpoints.Count == 0) return;
        _checkpoint = coordinator.Checkpoints[^1];
        _checkpointRequested = false;
        SetStatus($"检查点 tick={_checkpoint.Header.Tick}");
        if (_recordingRequested)
        {
            _recordingRequested = false;
            _recorder = new ReplayRecorder();
            _recorder.SetCheckpoint(_checkpoint);
            _nextSequence = 0;
            _recording = true;
            _hashRows.Clear();
            SetStatus($"开始录制，原点 tick={_checkpoint.Header.Tick}");
        }
    }

    private void AdvanceReplay(GameEngine engine)
    {
        if (!_frameQueued) return;
        ConsumeQueued(engine);
        QueueFrame(engine);
    }

    private void ConsumeQueued(GameEngine engine)
    {
        _replayFrames++;
        _replayIndex++;
        _frameQueued = false;
        _playbackDigest = WorldDigestLens.FromEngine(engine);
        _hashRows.Add($"play i={_replayIndex} tick={engine.GameSession.CurrentTick} digest={WorldDigestLens.Short(_playbackDigest)}");
        if (_replayIndex >= (_archive?.Frames.Count ?? 0))
        {
            FinishReplay(engine);
        }
    }

    private void QueueFrame(GameEngine engine)
    {
        if (!_playing || _archive == null || _replayIndex >= _archive.Frames.Count)
        {
            FinishReplay(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.QueueReplayActions(_archive.Frames[_replayIndex].Actions);
            _frameQueued = true;
            if (engine.Pacemaker is TurnBasedPacemaker pace)
            {
                pace.Step();
                engine.Tick(1f);
            }
        }
    }

    private void FinishReplay(GameEngine engine)
    {
        _playing = false;
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.SetReplayInputIsolation(false);
        }

        _playbackDigest = WorldDigestLens.FromEngine(engine);
        bool ok = string.Equals(_playbackDigest, _recordingDigest, StringComparison.Ordinal);
        _compare = ok ? "一致（绿）" : "分叉（红）";
        SetStatus($"回放结束：{_compare} 录制={WorldDigestLens.Short(_recordingDigest)} 回放={WorldDigestLens.Short(_playbackDigest)}");
        if (!ok) Fail("回放与录制终点指纹不一致。");
    }

    private void TryLoadArchive()
    {
        try
        {
            string path = ArchivePath();
            if (!File.Exists(path)) return;
            _archive = new ReplayArchiveCodec().Decode(File.ReadAllBytes(path)).Validate();
            SetStatus($"已加载回放资产 {_archive.Frames.Count} 帧");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private static string ArchivePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ludots", "deterministic-replay", "replays", "showcase.ldreplay");

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
