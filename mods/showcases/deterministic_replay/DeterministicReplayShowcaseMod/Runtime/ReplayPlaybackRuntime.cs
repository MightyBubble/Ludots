using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using AuthoritativeAction = Ludots.Core.Persistence.AuthoritativeAction;

namespace DeterministicReplayShowcaseMod.Runtime;

public sealed class ReplayShowcaseState
{
    public string Phase = "Step 1 — [Nudge hero] a few times, then [Start record] and nudge more";
    public string Status = "Ready.";
    public bool IsRecording;
    public bool IsReplaying;
    public bool IsPaused;
    public int Frames;
    public int PlaybackIndex = -1;
    public int CurrentTick;
    public string RecordedEndDigest = "-";
    public string PlaybackDigest = "-";
    public bool EndMatches;
    public string ArchiveLine = "no archive yet";
    public string IsolationNote = "n/a";
    public IReadOnlyList<string> LogLines = Array.Empty<string>();

    public bool Equals(ReplayShowcaseState? other)
    {
        if (other == null || LogLines.Count != other.LogLines.Count) return false;
        for (int i = 0; i < LogLines.Count; i++)
        {
            if (LogLines[i] != other.LogLines[i]) return false;
        }

        return Phase == other.Phase && Status == other.Status && IsRecording == other.IsRecording &&
            IsReplaying == other.IsReplaying && IsPaused == other.IsPaused && Frames == other.Frames &&
            PlaybackIndex == other.PlaybackIndex && CurrentTick == other.CurrentTick &&
            RecordedEndDigest == other.RecordedEndDigest && PlaybackDigest == other.PlaybackDigest &&
            EndMatches == other.EndMatches && ArchiveLine == other.ArchiveLine && IsolationNote == other.IsolationNote;
    }
}

/// <summary>
/// Deterministic replay showcase: nudges inject real authoritative input actions (Command order),
/// the recorder captures every frame, and ReplayPlayer replays them from the recorded checkpoint
/// through the same pipeline. The visible proof: recorded-end digest == post-replay digest, and live
/// input during playback is isolated. Archives round-trip through disk for cold-session replay.
/// </summary>
public sealed class ReplayPlaybackRuntime
{
    private readonly GameEngine _engine;
    private readonly List<string> _log = new(10);
    private readonly List<AuthoritativeAction> _frameActions = new(8);
    private ReplayRecorder? _recorder;
    private ReplayArchive? _archive;
    private bool _replayPlaying;
    private bool _replayPaused;
    private bool _replayFrameQueued;
    private int _replayIndex;
    private int _replayFrames;
    private string? _recordedEndDigest;
    private string? _liveRejectedSample;
    private long _nextSequence = 0;

    public ReplayPlaybackRuntime(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public bool IsShowcaseMap => DeterministicReplayShowcaseIds.MapId == _engine.CurrentMapSession?.MapId.Value;
    public bool IsRecording => _recorder != null;
    public bool IsReplaying => _replayPlaying;

    public void NudgeHero()
    {
        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not FrozenInputActionReader input) return;
        if (_replayPlaying)
        {
            input.SetActionState("Command", new Vector3(RandomShared(-800, 800), 0, RandomShared(-800, 800)), true, true, false);
            _liveRejectedSample = "Command (live)";
            Log("Live input during replay: isolated — playback frames stay authoritative.");
            return;
        }

        input.SetActionState("Command", new Vector3(6400 + RandomShared(-800, 800), 0, 6400 + RandomShared(-800, 800)), true, true, false);
        Log("Command order issued — the hero moves and the frame is being recorded.");
    }

    public void StartRecording()
    {
        if (_replayPlaying) { Log("Stop the replay before recording."); return; }
        if (_recorder != null) return;
        _recorder = new ReplayRecorder();
        _recorder.SetCheckpoint(new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags)));
        _recordedEndDigest = null;
        Log($"Recording from tick {_engine.GameSession.CurrentTick} — nudge now; every authoritative frame is captured.");
        Phase = "Step 2 — [Stop record] then [Play replay] to watch the same evolution again";
    }

    public void StopRecording()
    {
        if (_recorder == null) { Log("Not recording."); return; }
        _archive = _recorder.BuildArchive();
        _recorder = null;
        _recordedEndDigest = WorldDigest();
        Log($"Stopped: {_archive.Frames.Count} frames; end digest {_recordedEndDigest}.");
        Phase = "Step 3 — [Play replay]: frames drive the world from the recorded checkpoint";
    }

    public void PlayReplay()
    {
        if (_recorder != null) { Log("Stop recording first."); return; }
        if (_archive == null) { Log("No archive: record first (or [Load latest archive])."); return; }
        try
        {
            new ReplayPlayer(_archive.Validate()).PlayFromCheckpoint(_engine, new WorldRestoreService(), _ => { });
            _replayPlaying = true;
            _replayPaused = false;
            _replayIndex = 0;
            _replayFrames = 0;
            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader replayInput)
            {
                replayInput.SetReplayInputIsolation(true);
            }

            QueueNextFrame();
            Log($"Replay started from checkpoint; live input isolated.");
            Phase = "Step 4 — compare the digests; then [Save archive] and replay it after a restart";
        }
        catch (Exception ex)
        {
            _replayPlaying = false;
            Log($"Replay rejected: {ex.Message}");
        }
    }

    public void TogglePause()
    {
        if (!_replayPlaying) return;
        _replayPaused = !_replayPaused;
        Log(_replayPaused ? "Paused — [Step one frame] walks tick by tick." : "Resumed.");
    }

    public void StepOne()
    {
        if (!_replayPlaying || !_replayPaused) { Log("Pause first, then step."); return; }
        if (!_replayFrameQueued) QueueNextFrame();
        if (_replayFrameQueued) { _replayFrames++; _replayIndex++; _replayFrameQueued = false; QueueNextFrame(); }
    }

    public void ResetReplay()
    {
        if (!_replayPlaying) return;
        _replayPlaying = false;
        _replayPaused = false;
        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.SetReplayInputIsolation(false);
            input.ClearReplayActions();
        }

        Log("Replay reset — the world stays where the replay left it.");
    }

    public void SaveArchive()
    {
        if (_archive == null) { Log("Nothing to archive."); return; }
        Directory.CreateDirectory(ArchiveDir);
        string path = LatestArchivePath(preferNew: true);
        File.WriteAllBytes(path, new ReplayArchiveCodec().Encode(_archive));
        Log($"Archive written: {path} — relaunch cold and [Load latest archive].");
    }

    public void LoadLatestArchive()
    {
        if (_replayPlaying || _recorder != null) { Log("Stop record/replay first."); return; }
        string[] files = Directory.Exists(ArchiveDir) ? Directory.GetFiles(ArchiveDir, "*.ludotsreplay") : Array.Empty<string>();
        if (files.Length == 0) { Log("No archives on disk yet."); return; }
        Array.Sort(files, StringComparer.Ordinal);
        _archive = new ReplayArchiveCodec().Decode(File.ReadAllBytes(files[^1])).Validate();
        _recordedEndDigest = null;
        Log($"Cold archive loaded: {files[^1]} — [Play replay] now runs it.");
    }

    public void AdvanceReplayFixedStep()
    {
        if (!_replayPlaying || _replayPaused || !_replayFrameQueued) return;
        _replayFrames++;
        _replayIndex++;
        _replayFrameQueued = false;
        QueueNextFrame();
    }

    public void CaptureRecordingFrame()
    {
        if (_recorder == null || _replayPlaying) return;
        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not FrozenInputActionReader input) return;
        _frameActions.Clear();
        input.CopyAuthoritativeActions(_frameActions);
        _recorder.Record(new AuthoritativeFrame(_nextSequence++, _engine.GameSession.CurrentTick, _frameActions.ToArray()));
    }

    public string WorldDigest()
    {
        byte[] worldBytes = new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags)).WorldBytes;
        return Convert.ToHexString(SHA256.HashData(worldBytes))[..12];
    }

    public string Phase { get; private set; } = "Step 1 — [Nudge hero] a few times, then [Start record] and nudge more";

    public ReplayShowcaseState BuildState() => new()
    {
        Phase = Phase,
        IsRecording = IsRecording,
        IsReplaying = _replayPlaying,
        IsPaused = _replayPaused,
        Frames = _archive?.Frames.Count ?? 0,
        PlaybackIndex = _replayPlaying ? _replayIndex : -1,
        CurrentTick = _engine.GameSession.CurrentTick,
        RecordedEndDigest = _recordedEndDigest ?? "-",
        PlaybackDigest = !_replayPlaying && _archive != null ? WorldDigest() : WorldDigest(),
        EndMatches = _recordedEndDigest != null && !_replayPlaying && WorldDigest() == _recordedEndDigest,
        ArchiveLine = _archive == null ? "no archive yet" : $"{_archive.Frames.Count} frames",
        IsolationNote = _liveRejectedSample == null
            ? (_replayPlaying ? "try [Nudge hero] now — live input is rejected" : "n/a")
            : $"last live input '{_liveRejectedSample}' → rejected",
        LogLines = _log.ToArray(),
    };

    private void QueueNextFrame()
    {
        if (!_replayPlaying || _replayIndex >= (_archive?.Frames.Count ?? 0))
        {
            _replayPlaying = false;
            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader endedInput)
            {
                endedInput.SetReplayInputIsolation(false);
            }

            string digest = WorldDigest();
            bool matches = _recordedEndDigest != null && digest == _recordedEndDigest;
            Log($"Replay finished {_replayFrames}/{_archive?.Frames.Count ?? 0} frames; digest {digest} " +
                (matches ? "== recorded end — deterministic" : $"MISMATCH vs {_recordedEndDigest}"));
            return;
        }

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is FrozenInputActionReader input)
        {
            input.QueueReplayActions(_archive!.Frames[_replayIndex].Actions);
            _replayFrameQueued = true;
        }
    }

    private static string ArchiveDir => Path.Combine(AppContext.BaseDirectory, "Saves", "replay-showcase");

    private static string LatestArchivePath(bool preferNew) => Path.Combine(
        ArchiveDir,
        $"replay-{DateTime.UtcNow:yyyyMMdd-HHmmss}{(preferNew ? "" : "")}.ludotsreplay");

    private static int RandomShared(int min, int max) => Random.Shared.Next(min, max + 1);

    private void Log(string line)
    {
        _log.Insert(0, $"[replay] {line}");
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
    }
}
