using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace SavePanelMod.Runtime;

public sealed class SaveSlotRow
{
    public string Kind = "";
    public string Name = "";
    public int Tick;
    public string MapId = "";
    public string CreatedUtc = "";
    public int SchemaVersion;
    public long Bytes;
}

public sealed class SavePanelState
{
    public string Header = "Save / Load";
    public string Status = "F5 toggles this panel; any mod can drive it with the ShowPanel graph op.";
    public string StorageLine = "";
    public string Error = "";
    public bool StorageAvailable;
    public IReadOnlyList<SaveSlotRow> Rows = Array.Empty<SaveSlotRow>();
    public int AutosaveCount;
    public int ManualCount;

    public bool Equals(SavePanelState other) =>
        other != null &&
        Header == other.Header && Status == other.Status && StorageLine == other.StorageLine &&
        Error == other.Error && StorageAvailable == other.StorageAvailable &&
        AutosaveCount == other.AutosaveCount && ManualCount == other.ManualCount &&
        Rows.Count == other.Rows.Count &&
        RowsEqual(other.Rows);

    private bool RowsEqual(IReadOnlyList<SaveSlotRow> other)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            SaveSlotRow a = Rows[i];
            SaveSlotRow b = other[i];
            if (a.Kind != b.Kind || a.Name != b.Name || a.Tick != b.Tick || a.MapId != b.MapId ||
                a.CreatedUtc != b.CreatedUtc || a.SchemaVersion != b.SchemaVersion || a.Bytes != b.Bytes)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Player-facing save/load surface over the formal persistence pipeline: slots via SaveSlotStore
/// on the engine ISaveStorage service, capture at the clean tick boundary, restore through
/// WorldRestoreService. The mod owns no file IO and no parallel storage; when the host did not
/// provide storage the panel reports the failure instead of falling back.
/// </summary>
public sealed class SavePanelRuntime
{
    private readonly List<SaveSlotRow> _rows = new();

    public SavePanelRuntime(GameEngine engine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public GameEngine Engine { get; }

    public SavePanelState BuildPanelState()
    {
        RefreshSlots();
        ISaveStorage? storage = ResolveStorage();
        string storageLine = storage switch
        {
            null => "Storage service: MISSING (host provided no ISaveStorage)",
            Ludots.Platform.Desktop.DesktopSaveStorage desktop => $"Storage: disk @ {desktop.RootDirectory}",
            _ => $"Storage: {storage.GetType().Name}",
        };
        int autosaves = 0, manual = 0;
        foreach (SaveSlotRow row in _rows)
        {
            if (string.Equals(row.Kind, "autosave", StringComparison.Ordinal)) autosaves++;
            else manual++;
        }

        return new SavePanelState
        {
            StorageLine = storageLine,
            StorageAvailable = storage != null,
            Error = storage == null ? "Save/Load unavailable: this host did not register an ISaveStorage service." : "",
            Rows = _rows.ToArray(),
            AutosaveCount = autosaves,
            ManualCount = manual,
        };
    }

    public void SaveSlot()
    {
        if (RequireStore("Save") is not { } store) return;
        try
        {
            var id = SaveSlotId.Manual($"panel-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            WorldSaveSnapshot snapshot = new WorldSnapshotService().Capture(
                Engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
            store.WriteSlot(id, snapshot);
            Status($"Saved {id.Value} at tick {snapshot.Header.Tick}.");
        }
        catch (Exception ex)
        {
            Status($"Save failed: {ex.Message}");
        }
    }

    public void RestoreSlot(string kind, string name)
    {
        if (RequireStore("Restore") is not { } store) return;
        try
        {
            var id = new SaveSlotId(kind, name);
            WorldSaveSnapshot snapshot = store.ReadSlot(id);
            new WorldRestoreService().Restore(Engine, snapshot);
            Status($"Restored {id.Value} at tick {snapshot.Header.Tick}; keep playing.");
        }
        catch (Exception ex)
        {
            Status($"Restore failed: {ex.Message}");
        }
    }

    public void DeleteSlot(string kind, string name)
    {
        if (RequireStore("Delete") is not { } store) return;
        try
        {
            store.DeleteSlot(new SaveSlotId(kind, name));
            Status($"Deleted {kind}/{name}.");
        }
        catch (Exception ex)
        {
            Status($"Delete failed: {ex.Message}");
        }
    }

    public void WriteAutosave()
    {
        if (RequireStore("Autosave") is not { } store) return;
        try
        {
            var id = SaveSlotId.Autosave($"{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            WorldSaveSnapshot snapshot = new WorldSnapshotService().Capture(
                Engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
            store.WriteSlot(id, snapshot);
            Status($"Autosaved {id.Value} at tick {snapshot.Header.Tick}.");
        }
        catch (Exception ex)
        {
            Status($"Autosave failed: {ex.Message}");
        }
    }

    public void ToggleVisible()
    {
        if (Engine.GetService(CoreServiceKeys.PanelActivationApi) is not Ludots.Core.UI.PanelActivation.PanelActivationApi api)
        {
            return;
        }

        if (Engine.GetService(CoreServiceKeys.PanelActivationStore) is not Ludots.Core.UI.PanelActivation.UiPanelActivationStore store)
        {
            return;
        }

        if (store.IsVisible(SavePanelIds.PanelType))
        {
            api.HidePanel(SavePanelIds.PanelType);
        }
        else
        {
            api.ShowPanel(SavePanelIds.PanelType);
        }
    }

    public bool IsVisible =>
        Engine.GetService(CoreServiceKeys.PanelActivationStore) is Ludots.Core.UI.PanelActivation.UiPanelActivationStore store &&
        store.IsVisible(SavePanelIds.PanelType);

    private string? _status;

    public string Status() => _status ?? "Ready.";

    private void Status(string message) => _status = message;

    public SavePanelState StateWithStatus()
    {
        SavePanelState state = BuildPanelState();
        state.Status = Status();
        return state;
    }

    private void RefreshSlots()
    {
        _rows.Clear();
        if (ResolveStorage() is not { } storage)
        {
            return;
        }

        var store = new SaveSlotStore(storage);
        try
        {
            foreach (SaveSlotHeader header in store.ListSlots())
            {
                string key = header.Id.ToStorageKey();
                long bytes = storage.Exists(key) ? storage.ReadAllBytes(key).Length : 0;
                _rows.Add(new SaveSlotRow
                {
                    Kind = header.Id.Kind,
                    Name = header.Id.Name,
                    Tick = header.Header.Tick,
                    MapId = header.Header.MapId,
                    CreatedUtc = header.Header.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    SchemaVersion = header.Header.SchemaVersion,
                    Bytes = bytes,
                });
            }
        }
        catch (Exception ex)
        {
            _rows.Clear();
            _status = $"Slot list failed: {ex.Message}";
        }
    }

    private SaveSlotStore? RequireStore(string operation)
    {
        if (ResolveStorage() is not { } storage)
        {
            Status($"{operation} unavailable: this host did not register an ISaveStorage service.");
            return null;
        }

        return new SaveSlotStore(storage);
    }

    private ISaveStorage? ResolveStorage()
    {
        return Engine.GetService(CoreServiceKeys.SaveStorage);
    }
}
