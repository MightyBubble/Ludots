using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.UI;
using PersistenceOnlineReplayShowcaseMod.UI;

namespace PersistenceOnlineReplayShowcaseMod.Runtime;

public sealed class PersistenceOnlineReplayRuntime
{
    private readonly IModContext _context;
    private readonly PersistenceOnlineReplayPanelController _panel;
    private readonly List<string> _log = new(12);
    private readonly List<AuthoritativeAction> _actionBuffer = new(16);
    private readonly SaveSlotStore _slots;
    private GameEngine? _engine;
    private ReplayRecorder? _recorder;
    private ReplayArchive? _archive;
    private WorldSaveSnapshot? _checkpoint;
    private bool _checkpointRequested;
    private bool _recording;
    private bool _disconnected;
    private bool _ablateNextReplay;
    private bool _replayPlaying;
    private bool _replayPaused;
    private int _replayIndex;
    private bool _replayFrameQueued;
    private long _nextSequence;
    private int _replayFrames;
    private string _status = "Press Checkpoint, then Record. The HUD will show the real tick and digest.";
    private string _checkpointDigest = "n/a";
    private string _replayResult = "not compared";
    private string _recoverySource = "live";

    public PersistenceOnlineReplayRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludots", "persistence-online-replay");
        _slots = new SaveSlotStore(new FileSaveStorage(root));
        _panel = new PersistenceOnlineReplayPanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine) return Task.CompletedTask;
        if (!PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            _panel.ClearIfOwned();
            return Task.CompletedTask;
        }
        Activate(engine.GetService(CoreServiceKeys.InputHandler));
        _engine = engine;
        TryLoadReplayAsset();
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is GameEngine engine && PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            engine.GetService(CoreServiceKeys.InputHandler)?.PopContext(PersistenceOnlineReplayShowcaseIds.InputContext);
            _panel.ClearIfOwned();
            _engine = null;
        }
        return Task.CompletedTask;
    }

    public void AdvanceFixedStep(GameEngine engine)
    {
        if (!PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value)) return;
        CheckForCapturedCheckpoint(engine);
        if (_recording && _recorder != null && !_disconnected && engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.CopyAuthoritativeActions(_actionBuffer);
            _recorder.Record(new AuthoritativeFrame(_nextSequence++, engine.GameSession?.CurrentTick ?? 0, _actionBuffer.ToArray()));
        }
        RefreshPanel(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value) &&
            engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.MountOrRefresh(root, engine);
        }
    }

    public bool IsReplayPlaying => _replayPlaying;

    public void RequestCheckpoint() { if (CurrentEngine() is { } engine) { engine.GetService(CoreServiceKeys.CheckpointCoordinator)?.RequestCheckpoint(); _checkpointRequested = true; _status = "Checkpoint requested; waiting for a completed fixed step."; Log(_status); RefreshPanel(engine); } }

    public void SaveSlot()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            if (_checkpoint == null) throw new SaveContextException("Capture a checkpoint before saving.");
            _slots.WriteSlot(SaveSlotId.Manual("showcase"), _checkpoint);
            _status = $"Saved manual/showcase at tick {_checkpoint.Header.Tick}. Cold-start path: {_slotsPath()}";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
        RefreshPanel(engine);
    }

    public void RestoreSlot()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            WorldSaveSnapshot snapshot = _slots.ReadSlot(SaveSlotId.Manual("showcase"));
            new WorldRestoreService().Restore(engine, snapshot);
            _checkpoint = snapshot;
            _checkpointDigest = Digest(snapshot.WorldBytes);
            _recoverySource = "disk save";
            _status = $"Restored manual/showcase from disk at tick {snapshot.Header.Tick}; continue playing.";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
        RefreshPanel(engine);
    }

    public void StartRecording()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            if (_checkpoint == null) throw new SaveContextException("Capture a checkpoint before recording.");
            _recorder = new ReplayRecorder();
            _recorder.SetCheckpoint(_checkpoint);
            _nextSequence = 0;
            _recording = true;
            _status = $"Recording from checkpoint tick {_checkpoint.Header.Tick}.";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
        RefreshPanel(engine);
    }

    public void StopRecording()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            if (_recorder == null || !_recording) throw new SaveContextException("No active recording.");
            _archive = _recorder.BuildArchive();
            Directory.CreateDirectory(Path.GetDirectoryName(ReplayPath())!);
            File.WriteAllBytes(ReplayPath(), new ReplayArchiveCodec().Encode(_archive));
            _recording = false;
            _replayResult = "recorded; replay pending";
            _status = $"Replay ready: {_archive.Frames.Count} authoritative frames, schema {_archive.Header.SchemaVersion}; saved to {ReplayPath()}.";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
        RefreshPanel(engine);
    }

    public void PlayReplay()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            if (_archive == null) throw new SaveContextException("Stop a recording before replaying.");
            ReplayArchive archive = _ablateNextReplay ? RemoveOneFrame(_archive) : _archive;
            new ReplayPlayer(archive).PlayFromCheckpoint(engine, new WorldRestoreService(), _ => { });
            _archive = archive;
            _replayFrames = 0;
            _replayIndex = 0;
            _replayPlaying = true;
            _replayPaused = false;
            QueueNextReplayFrame(engine);
            _ablateNextReplay = false;
            _recoverySource = "replay";
            _replayResult = "playing";
            _status = $"Replay loaded from {ReplayPath()}; live input is isolated. Press Pause or Step.";
            Log(_status);
        }
        catch (Exception ex)
        {
            _replayPlaying = false;
            _replayPaused = false;
            _replayFrameQueued = false;
            _replayResult = "rejected";
            Fail(ex);
        }
        RefreshPanel(engine);
    }

    public void SimulateDisconnect()
    {
        if (CurrentEngine() is not { } engine) return;
        _disconnected = true;
        _recording = false;
        engine.GetService(CoreServiceKeys.SimulationLoopController)?.PauseSimulation();
        _recoverySource = "disconnected";
        _status = "Disconnected: authoritative updates paused. Press Reconnect to restore the last checkpoint.";
        Log(_status);
        RefreshPanel(engine);
    }

    public void Reconnect()
    {
        if (CurrentEngine() is not { } engine) return;
        try
        {
            if (!_disconnected) throw new SaveContextException("Simulate disconnect before reconnecting.");
            if (_checkpoint == null) throw new SaveContextException("Reconnect has no authoritative checkpoint.");
            new WorldRestoreService().Restore(engine, _checkpoint);
            _disconnected = false;
            engine.GetService(CoreServiceKeys.SimulationLoopController)?.SetRealtime();
            _recoverySource = "checkpoint recovery";
            _checkpointDigest = Digest(_checkpoint.WorldBytes);
            _status = $"Reconnected from checkpoint tick {_checkpoint.Header.Tick}; frame sequence resumes at {_nextSequence}.";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
        RefreshPanel(engine);
    }

    public void ToggleReplayPause()
    {
        if (!_replayPlaying) { _status = "Load a replay before pausing."; Log(_status); return; }
        _replayPaused = !_replayPaused;
        _status = _replayPaused ? "Replay paused; live input remains rejected." : "Replay resumed.";
        Log(_status);
        if (CurrentEngine() is { } engine) RefreshPanel(engine);
    }

    public void StepReplay()
    {
        if (!_replayPlaying) { _status = "Load a replay before stepping."; Log(_status); return; }
        _replayPaused = true;
        if (CurrentEngine() is { } engine)
        {
            engine.GetService(CoreServiceKeys.SimulationLoopController)?.Step();
            AdvanceReplayFixedStep(engine);
        }
    }

    public void ResetReplay()
    {
        if (CurrentEngine()?.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.ClearReplayActions();
        }
        _replayPlaying = false;
        _replayPaused = false;
        _replayIndex = 0;
        _replayFrames = 0;
        _replayFrameQueued = false;
        _status = "Replay reset; live session is active.";
        Log(_status);
        if (CurrentEngine() is { } engine) RefreshPanel(engine);
    }

    public void AblateFrame()
    {
        if (CurrentEngine() is not { } engine) return;
        if (_archive == null) { _status = "Record a replay before deleting a frame."; Log(_status); }
        else { _ablateNextReplay = true; _status = "Ablation armed: next replay deletes one frame and must be rejected."; Log(_status); }
        RefreshPanel(engine);
    }

    internal PersistenceOnlineReplayPanelState BuildPanelState()
    {
        GameEngine? engine = CurrentEngine();
        int tick = engine?.GameSession?.CurrentTick ?? 0;
        int checkpoints = engine?.GetService(CoreServiceKeys.CheckpointCoordinator)?.Checkpoints.Count ?? 0;
        int frames = _archive?.Frames.Count ?? _recorder?.FrameCount ?? 0;
        return new PersistenceOnlineReplayPanelState(
            "Persistence / Replay / Reconnect Lab",
            "Give the RTS a checkpoint, record authoritative input, restart or reconnect, then compare what continues.",
            _status,
            new[] { $"tick: {tick}", $"checkpoints: {checkpoints}", $"replay frames: {frames}", $"replay applied: {_replayFrames}", $"recovery source: {_recoverySource}", $"checkpoint digest: {_checkpointDigest}", $"replay result: {_replayResult}", $"recording: {_recording}", $"connection: {(_disconnected ? "offline" : "online")}" },
            new[] { "Checkpoint -> Save -> continue", "Record -> Stop -> Replay", "Pause / Step / Reset replay", "Disconnect -> Reconnect", "Delete frame -> Replay to see explicit rejection", "Keyboard: F4-F12 mirrors the buttons" },
            _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    private void CheckForCapturedCheckpoint(GameEngine engine)
    {
        if (!_checkpointRequested) return;
        var coordinator = engine.GetService(CoreServiceKeys.CheckpointCoordinator);
        if (coordinator == null || coordinator.Checkpoints.Count == 0) return;
        _checkpoint = coordinator.Checkpoints[^1];
        _checkpointRequested = false;
        _checkpointDigest = Digest(_checkpoint.WorldBytes);
        _recoverySource = "live checkpoint";
        _status = $"Checkpoint captured at completed tick {_checkpoint.Header.Tick}; digest {_checkpointDigest}.";
        Log(_status);
    }

    private GameEngine? CurrentEngine() => _engine;
    private static void Activate(Ludots.Core.Input.Runtime.PlayerInputHandler? input) => input?.PushContext(PersistenceOnlineReplayShowcaseIds.InputContext);
    private string _slotsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludots", "persistence-online-replay", "saves", "manual", "showcase.ldsave");
    private string ReplayPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ludots", "persistence-online-replay", "replays", "showcase.ldreplay");
    private void TryLoadReplayAsset()
    {
        try
        {
            string path = ReplayPath();
            if (!File.Exists(path)) return;
            _archive = new ReplayArchiveCodec().Decode(File.ReadAllBytes(path)).Validate();
            _status = $"Replay asset loaded from disk: {_archive.Frames.Count} frames.";
            Log(_status);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void QueueNextReplayFrame(GameEngine engine)
    {
        if (!_replayPlaying || _replayIndex >= (_archive?.Frames.Count ?? 0))
        {
            _replayPlaying = false;
            _replayResult = $"completed {_replayFrames}/{_archive?.Frames.Count ?? 0} frames";
            _status = $"Replay finished: {_replayResult}.";
            Log(_status);
            return;
        }

        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.QueueReplayActions(_archive!.Frames[_replayIndex].Actions);
            _replayFrameQueued = true;
        }
    }

    public void AdvanceReplayFixedStep(GameEngine engine)
    {
        if (!_replayPlaying || _replayPaused) return;
        if (!_replayFrameQueued) return;
        _replayFrames++;
        _replayIndex++;
        _replayFrameQueued = false;
        QueueNextReplayFrame(engine);
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 12);
    private static ReplayArchive RemoveOneFrame(ReplayArchive archive)
    {
        if (archive.Frames.Count < 2) throw new SaveContextException("Replay ablation needs at least two frames.");
        var frames = new List<AuthoritativeFrame>(archive.Frames);
        frames.RemoveAt(frames.Count / 2);
        return new ReplayArchive(archive.Header, archive.Checkpoint, frames).Validate();
    }
    private void Fail(Exception ex) { _status = $"Rejected: {ex.Message}"; Log(_status); }
    private void Log(string line) { _log.Insert(0, $"[showcase] {line}"); if (_log.Count > 8) _log.RemoveAt(_log.Count - 1); }
}
