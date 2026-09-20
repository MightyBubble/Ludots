using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;

namespace ReconnectRecoveryShowcaseMod.Runtime;

public sealed class ReconnectState
{
    public string Phase = "Step 1 — [Nudge hero], then [Checkpoint] to arm the authoritative recovery point";
    public string Status = "Ready.";
    public bool Disconnected;
    public int AuthorityTick;
    public int ClientTick;
    public int DisconnectStartTick = -1;
    public string RecoverySource = "live";
    public string LastRejection = "n/a";
    public string CheckpointDigest = "-";
    public string CurrentDigest = "-";
    public IReadOnlyList<string> LogLines = Array.Empty<string>();

    public bool Equals(ReconnectState? other)
    {
        if (other == null || LogLines.Count != other.LogLines.Count) return false;
        for (int i = 0; i < LogLines.Count; i++)
        {
            if (LogLines[i] != other.LogLines[i]) return false;
        }

        return Phase == other.Phase && Status == other.Status && Disconnected == other.Disconnected &&
            AuthorityTick == other.AuthorityTick && ClientTick == other.ClientTick &&
            DisconnectStartTick == other.DisconnectStartTick && RecoverySource == other.RecoverySource &&
            LastRejection == other.LastRejection && CheckpointDigest == other.CheckpointDigest &&
            CurrentDigest == other.CurrentDigest;
    }
}

/// <summary>
/// Reconnect recovery showcase (single-machine simulation, honestly labeled): while "disconnected"
/// the authoritative side keeps ticking while the client view freezes; reconnecting restores from the
/// authoritative checkpoint and proves frame-sequence continuity. Missing / duplicate / stale frames
/// are rejected with readable errors. True network fault injection remains open (联机专项未验收).
/// </summary>
public sealed class ReconnectRecoveryRuntime
{
    private readonly GameEngine _engine;
    private readonly List<string> _log = new(10);
    private WorldSaveSnapshot? _checkpoint;
    private string? _checkpointDigest;
    private int _authorityTickAtDisconnect = -1;

    public ReconnectRecoveryRuntime(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public bool IsShowcaseMap => ReconnectRecoveryShowcaseIds.MapId == _engine.CurrentMapSession?.MapId.Value;
    public bool Disconnected { get; private set; }
    public int ClientTick { get; private set; } = -1;
    public int DisconnectStartTick { get; private set; } = -1;

    public void NudgeHero()
    {
        if (FindHero() is not { } hero) { Log("Hero not found."); return; }
        if (Disconnected) { Log("Client is disconnected — the nudge does not reach the authority."); return; }
        ref var pos = ref _engine.World.Get<Ludots.Core.Components.WorldPositionCm>(hero);
        var cm = pos.ToWorldCmInt2();
        pos = Ludots.Core.Components.WorldPositionCm.FromCm(cm.X + Random.Shared.Next(-700, 701), cm.Y + Random.Shared.Next(-700, 701));
        Log("Hero moved (live).");
    }

    public void Checkpoint()
    {
        _checkpoint = new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        _checkpointDigest = Digest12(_checkpoint.WorldBytes);
        ClientTick = _engine.GameSession.CurrentTick;
        Log($"Authoritative checkpoint armed at tick {ClientTick}, digest {_checkpointDigest}.");
        Phase = "Step 2 — [Disconnect]: the authority keeps ticking while the client freezes";
    }

    public void Disconnect()
    {
        if (Disconnected) return;
        if (_checkpoint == null) { Log("Arm a checkpoint first — reconnect proves recovery from the authority."); return; }
        Disconnected = true;
        DisconnectStartTick = _engine.GameSession.CurrentTick;
        _authorityTickAtDisconnect = ClientTick;
        Log($"Disconnected at authority tick {DisconnectStartTick}; client stays frozen at tick {ClientTick}.");
        Phase = "Step 3 — watch the two tick lines diverge, then [Reconnect]";
    }

    public void Reconnect()
    {
        if (!Disconnected) { Log("Not disconnected."); return; }
        Disconnected = false;
        int authorityNow = _engine.GameSession.CurrentTick;
        int gap = authorityNow - Math.Max(_authorityTickAtDisconnect, 0);
        try
        {
            if (_checkpoint == null) throw new SaveContextException("No checkpoint.");
            new WorldRestoreService().Restore(_engine, _checkpoint);
            ClientTick = _engine.GameSession.CurrentTick;
            Log($"Reconnected: authority advanced {gap} ticks during the gap; client resumed from the authoritative checkpoint (tick {ClientTick}), not a local illusion.");
            Phase = "Done — recovery source is authoritative. Try the fault injections below";
        }
        catch (Exception ex)
        {
            Log($"Reconnect rejected: {ex.Message}");
        }
    }

    public void RejectMissingFrame() => RejectInjected("missing", frames =>
    {
        frames.RemoveAt(frames.Count / 2);
    });

    public void RejectDuplicateFrame() => RejectInjected("duplicate", frames =>
    {
        frames.Insert(frames.Count / 2, frames[frames.Count / 2]);
    });

    public void RejectStaleFrame() => RejectInjected("stale", frames =>
    {
        frames[^1] = frames[^1] with { Sequence = 1 };
    });

    public void Tick()
    {
        if (!Disconnected) ClientTick = _engine.GameSession.CurrentTick;
    }

    public string Phase { get; private set; } = "Step 1 — [Nudge hero], then [Checkpoint] to arm the authoritative recovery point";

    public ReconnectState BuildState()
    {
        byte[] world = new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags)).WorldBytes;
        return new ReconnectState
        {
            Phase = Phase,
            Disconnected = Disconnected,
            AuthorityTick = _engine.GameSession.CurrentTick,
            ClientTick = ClientTick < 0 ? _engine.GameSession.CurrentTick : ClientTick,
            DisconnectStartTick = DisconnectStartTick,
            RecoverySource = Disconnected ? "disconnected" : "authoritative checkpoint",
            CheckpointDigest = _checkpointDigest ?? "-",
            CurrentDigest = Digest12(world),
            LogLines = _log.ToArray(),
        };
    }

    private void RejectInjected(string kind, Action<List<AuthoritativeFrame>> mutate)
    {
        if (_checkpoint == null) { Log("Arm a checkpoint first."); return; }
        try
        {
            var frames = new List<AuthoritativeFrame>();
            long seq = 1;
            int tick = ClientTick;
            for (int i = 0; i < 20; i++)
            {
                frames.Add(new AuthoritativeFrame(seq++, tick + i, Array.Empty<AuthoritativeAction>()));
            }

            mutate(frames);
            var header = new ReplayHeader(
                ReplayHeader.CurrentSchemaVersion,
                _checkpoint.Header.ModSetHash,
                _checkpoint.Header.RegistryFingerprint,
                _checkpoint.Header.MapId,
                _checkpoint.Header.Tick,
                frames[0].Sequence);
            _ = new ReplayArchive(header, _checkpoint, frames).Validate();
            Log($"Injected {kind} frame — unexpectedly accepted; this is a bug in the showcase.");
        }
        catch (Exception ex)
        {
            LastRejection = $"{kind}: {ex.Message}";
            Log($"Injected {kind} frame → rejected: {ex.Message}");
        }
    }

    public string LastRejection { get; private set; } = "n/a";

    private static string Digest12(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..12];

    private Arch.Core.Entity? FindHero()
    {
        Arch.Core.Entity found = Arch.Core.Entity.Null;
        var query = new Arch.Core.QueryDescription().WithAll<Ludots.Core.Components.Name, Ludots.Core.Components.WorldPositionCm>();
        _engine.World.Query(in query, (Arch.Core.Entity e, ref Ludots.Core.Components.Name name) =>
        {
            if (name.Value == ReconnectRecoveryShowcaseIds.HeroName) found = e;
        });
        return found == Arch.Core.Entity.Null ? null : found;
    }

    private void Log(string line)
    {
        _log.Insert(0, $"[reconnect] {line}");
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
    }
}
