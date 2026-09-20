using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using SavePanelMod.Runtime;

namespace SaveLoadShowcaseMod.Runtime;

public sealed class SaveLoadShowcaseState
{
    public string Phase = "Step 1 — press [Nudge hero] a few times, then [Save via panel]";
    public string Status = "Ready.";
    public int CurrentTick;
    public string HeroPosition = "-";
    public string SavedAt = "no save yet";
    public string SavedHeroPosition = "-";
    public string Drift = "-";
    public string StorageLine = "-";
    public bool SavedExists;
    public bool DriftIsZero;
    public IReadOnlyList<string> LogLines = Array.Empty<string>();

    public bool Equals(SaveLoadShowcaseState? other)
    {
        if (other == null || LogLines.Count != other.LogLines.Count) return false;
        for (int i = 0; i < LogLines.Count; i++)
        {
            if (LogLines[i] != other.LogLines[i]) return false;
        }

        return Phase == other.Phase && Status == other.Status && CurrentTick == other.CurrentTick &&
            HeroPosition == other.HeroPosition && SavedAt == other.SavedAt &&
            SavedHeroPosition == other.SavedHeroPosition && Drift == other.Drift &&
            StorageLine == other.StorageLine && SavedExists == other.SavedExists && DriftIsZero == other.DriftIsZero;
    }
}

/// <summary>
/// Save/load capability showcase runtime. Every slot operation delegates to the SavePanelMod
/// runtime resolved from the engine GlobalContext — the showcase owns zero slot code, proving the
/// panel is drop-in reusable. World mutation is direct position editing so the dynamic axis
/// (state change -> save -> mutate -> restore -> state returns) stays in full view.
/// </summary>
public sealed class SaveLoadShowcaseRuntime
{
    private readonly GameEngine _engine;
    private readonly List<string> _log = new(10);
    private (int tick, int x, int y)? _savedHero;

    public SaveLoadShowcaseRuntime(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public bool IsShowcaseMap => SaveLoadShowcaseIds.MapId == _engine.CurrentMapSession?.MapId.Value;

    public void NudgeHero()
    {
        if (FindHero() is not { } hero) { Log("Hero not found on this map."); return; }
        ref WorldPositionCm pos = ref _engine.World.Get<WorldPositionCm>(hero);
        var random = new Random();
        int dx = random.Next(-900, 901), dy = random.Next(-900, 901);
        var next = WorldPositionCm.FromCm(pos.ToWorldCmInt2().X + dx, pos.ToWorldCmInt2().Y + dy);
        pos = next;
        Log($"Hero moved by ({dx}, {dy}) cm — the world just changed.");
        if (_savedHero != null) Phase = "Step 3 — press [Restore latest] to bring the save point back";
    }

    public void SaveViaPanel()
    {
        if (Panel() is not { } panel) { Log("SavePanelMod runtime is not installed."); return; }
        if (FindHero() is not { } hero) return;
        var pos = _engine.World.Get<WorldPositionCm>(hero).ToWorldCmInt2();
        panel.SaveSlot();
        _savedHero = (_engine.GameSession.CurrentTick, pos.X, pos.Y);
        Log($"Saved at tick {_savedHero.Value.tick} — slot list is on the right (F5 panel).");
        Phase = "Step 2 — [Nudge hero] again, watch the drift ring grow";
    }

    public void RestoreLatest()
    {
        if (Panel() is not { } panel) { Log("SavePanelMod runtime is not installed."); return; }
        var rows = panel.BuildPanelState().Rows;
        string? latest = null;
        foreach (var row in rows)
        {
            if (string.Equals(row.Kind, "manual", StringComparison.Ordinal) &&
                (latest == null || string.CompareOrdinal(row.Name, latest) > 0))
            {
                latest = row.Name;
            }
        }

        if (latest == null) { Log("No manual slot yet — press [Save via panel] first."); return; }
        panel.RestoreSlot("manual", latest);
        Log($"Restored manual/{latest} — hero should be back at the save point.");
        Phase = "Step 4 — cold start: quit, relaunch, the slot is still there";
    }

    public void SpawnExcludedDecoy()
    {
        if (FindHero() is not { } hero) return;
        var pos = _engine.World.Get<WorldPositionCm>(hero).ToWorldCmInt2();
        _engine.World.Create(
            new Name { Value = SaveLoadShowcaseIds.DecoyName },
            WorldPositionCm.FromCm(pos.X + 400, pos.Y + 400),
            new SaveExcludedTag());
        Log("Spawned an excluded decoy — it lives now, but a restore will drop it (SaveExcludedTag).");
    }

    public void CorruptLatestSlot()
    {
        if (_engine.GetService(CoreServiceKeys.SaveStorage) is not ISaveStorage storage)
        {
            Log("No engine save storage service in this host.");
            return;
        }

        if (Panel() is not { } panel) return;
        var rows = panel.BuildPanelState().Rows;
        string? latest = null;
        foreach (var row in rows)
        {
            if (string.Equals(row.Kind, "manual", StringComparison.Ordinal) &&
                (latest == null || string.CompareOrdinal(row.Name, latest) > 0))
            {
                latest = row.Name;
            }
        }

        if (latest == null) { Log("No manual slot to corrupt yet."); return; }
        string key = $"saves/manual/{latest}.ldsave";
        byte[] bytes = storage.ReadAllBytes(key);
        bytes[^3] ^= 0xFF;
        storage.WriteAllBytes(key, bytes);
        Log($"Corrupted slot manual/{latest} — now press [Restore latest] and read the red error.");
    }

    public (int x, int y)? SavedHeroCm => _savedHero is { } s ? (s.x, s.y) : null;
    public int SavedTick => _savedHero?.tick ?? -1;

    public SaveLoadShowcaseState BuildState()
    {
        string hero = "-";
        (int x, int y) heroPos = (0, 0);
        int entities = 0;
        if (FindHero() is { } found)
        {
            var pos = _engine.World.Get<WorldPositionCm>(found).ToWorldCmInt2();
            heroPos = (pos.X, pos.Y);
            hero = $"({pos.X}, {pos.Y}) cm";
        }

        var query = new QueryDescription();
        _engine.World.Query(in query, _ => entities++);

        string saved = _savedHero is { } s ? $"tick {s.tick} @ ({s.x}, {s.y}) cm" : "no save yet";
        bool savedExists = _savedHero != null;
        string drift = "-";
        bool driftZero = false;
        if (_savedHero != null)
        {
            int dx = heroPos.x - _savedHero.Value.x, dy = heroPos.y - _savedHero.Value.y;
            drift = $"{Math.Sqrt(dx * dx + dy * dy):F0} cm";
            driftZero = dx == 0 && dy == 0;
        }

        ISaveStorage? storage = _engine.GetService(CoreServiceKeys.SaveStorage);
        string storageLine = storage is Ludots.Platform.Desktop.DesktopSaveStorage desktop
            ? $"disk @ {desktop.RootDirectory}"
            : storage?.GetType().Name ?? "no storage service";

        return new SaveLoadShowcaseState
        {
            Phase = Phase,
            CurrentTick = _engine.GameSession.CurrentTick,
            HeroPosition = hero,
            SavedAt = saved,
            SavedHeroPosition = _savedHero is { } sv ? $"({sv.x}, {sv.y}) cm" : "-",
            Drift = drift,
            DriftIsZero = driftZero,
            SavedExists = savedExists,
            StorageLine = storageLine,
            LogLines = _log.ToArray(),
        };
    }

    public string Phase { get; private set; } = "Step 1 — press [Nudge hero] a few times, then [Save via panel]";

    private SavePanelRuntime? Panel() =>
        _engine.GlobalContext.TryGetValue("SavePanelMod.Runtime", out object? value) && value is SavePanelRuntime panel
            ? panel
            : null;

    private Entity? FindHero()
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        _engine.World.Query(in query, (Entity e, ref Name name) =>
        {
            if (name.Value == SaveLoadShowcaseIds.HeroName) found = e;
        });
        return found == Entity.Null ? null : found;
    }

    private void Log(string line)
    {
        _log.Insert(0, $"[showcase] {line}");
        if (_log.Count > 8) _log.RemoveAt(_log.Count - 1);
    }
}
