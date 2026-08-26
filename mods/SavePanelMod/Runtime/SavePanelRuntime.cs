using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Platform.Abstractions;
using SavePanelMod.UI;

namespace SavePanelMod.Runtime;

public sealed class SavePanelRuntime
{
    public const int DefaultAutosaveRetention = 3;

    private readonly WorldRestoreService _restore = new();
    private SaveSlotStore? _slots;
    private AutosaveSlotPolicy? _autosave;
    private ISaveStorage? _storage;
    private int _autosaveRetention = DefaultAutosaveRetention;
    private string _status = "按 F5 打开存档面板。存/读/删走正式槽位链路。";
    private string? _error;
    private string? _selectedSlot;
    private string _manualName = "manual-1";
    private int _seenCheckpoints;
    private PendingCapture? _pending;
    private bool _inputContextPushed;

    public string Status => _status;
    public string? Error => _error;
    public string? SelectedSlot => _selectedSlot;
    public string ManualName => _manualName;
    public bool HasPendingCapture => _pending != null;
    public int AutosaveRetention => _autosave?.RetentionCount ?? _autosaveRetention;

    public void SetAutosaveRetention(int retentionCount)
    {
        if (retentionCount <= 0)
        {
            Fail("自动存档保留数必须大于 0。");
            return;
        }

        _autosaveRetention = retentionCount;
        _autosave = new AutosaveSlotPolicy(retentionCount);
        ClearError();
        SetStatus($"自动存档保留数已设为 {retentionCount}。");
    }

    public void BindEngine(GameEngine engine)
    {
        if (engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) && storage != null)
        {
            _storage = storage;
            _slots = new SaveSlotStore(storage);
            _autosave ??= new AutosaveSlotPolicy(_autosaveRetention);
            ClearError();
            if (!_inputContextPushed)
            {
                engine.GetService(CoreServiceKeys.InputHandler)?.PushContext(SavePanelIds.InputContext);
                _inputContextPushed = true;
            }
            SetStatus("存档存储已就绪。选中槽位后可存/读/删。");
            return;
        }

        _storage = null;
        _slots = null;
        Fail("宿主未提供存档存储服务（ISaveStorage）。面板拒绝静默降级到内存或私有路径。");
    }

    public void UnbindEngine(GameEngine? engine)
    {
        if (_inputContextPushed && engine != null)
        {
            engine.GetService(CoreServiceKeys.InputHandler)?.PopContext(SavePanelIds.InputContext);
            _inputContextPushed = false;
        }

        _storage = null;
        _slots = null;
        _pending = null;
    }

    public bool IsVisible(GameEngine engine)
    {
        return engine.TryGetService(CoreServiceKeys.PanelActivationStore, out UiPanelActivationStore? store)
               && store != null
               && store.IsVisible(SavePanelIds.PanelType);
    }

    public void ToggleVisible(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PanelActivationApi, out PanelActivationApi? api) || api == null)
        {
            Fail("面板激活服务不可用，无法开关存档面板。");
            return;
        }

        if (IsVisible(engine))
        {
            api.HidePanel(SavePanelIds.PanelType);
            SetStatus("存档面板已关闭。");
        }
        else
        {
            api.ShowPanel(SavePanelIds.PanelType);
            SetStatus("存档面板已打开。");
        }
    }

    public void Show(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PanelActivationApi, out PanelActivationApi? api) || api == null)
        {
            Fail("面板激活服务不可用。");
            return;
        }

        api.ShowPanel(SavePanelIds.PanelType);
    }

    public void Hide(GameEngine engine)
    {
        if (!engine.TryGetService(CoreServiceKeys.PanelActivationApi, out PanelActivationApi? api) || api == null)
        {
            Fail("面板激活服务不可用。");
            return;
        }

        api.HidePanel(SavePanelIds.PanelType);
    }

    public void SelectSlot(string slotValue)
    {
        _selectedSlot = slotValue;
        ClearError();
        SetStatus($"已选中槽位 {slotValue}。");
    }

    public void SetManualName(string name)
    {
        _manualName = string.IsNullOrWhiteSpace(name) ? "manual-1" : name.Trim();
    }

    public void RequestManualSave(GameEngine engine)
    {
        if (!EnsureReady(engine)) return;
        try
        {
            SaveSlotId id = SaveSlotId.Manual(_manualName);
            _ = id.ToStorageKey();
            RequestCapture(engine, PendingCapture.Manual(id));
            SetStatus($"手动存档已请求（{_manualName}），等待干净步边界…");
        }
        catch (SaveContextException ex)
        {
            Fail(ex.Message);
        }
    }

    public void RequestAutosave(GameEngine engine)
    {
        if (!EnsureReady(engine)) return;
        RequestCapture(engine, PendingCapture.Autosave());
        SetStatus($"自动存档已请求（保留 {AutosaveRetention} 个），等待干净步边界…");
    }

    public void RestoreSelected(GameEngine engine)
    {
        if (!EnsureReady(engine)) return;
        if (!TryParseSelected(out SaveSlotId id))
        {
            Fail("请先选中一个槽位再读档。");
            return;
        }

        try
        {
            WorldSaveSnapshot snapshot = _slots!.ReadSlot(id);
            _restore.Restore(engine, snapshot);
            ClearError();
            SetStatus($"已读档 {id.Value}（tick {snapshot.Header.Tick} / map {snapshot.Header.MapId}）。");
        }
        catch (SaveContextException ex)
        {
            Fail(ex.Message);
        }
    }

    public void DeleteSelected(GameEngine engine)
    {
        if (!EnsureReady(engine)) return;
        if (!TryParseSelected(out SaveSlotId id))
        {
            Fail("请先选中一个槽位再删除。");
            return;
        }

        try
        {
            _slots!.DeleteSlot(id);
            if (string.Equals(_selectedSlot, id.Value, StringComparison.Ordinal))
            {
                _selectedSlot = null;
            }

            ClearError();
            SetStatus($"已删除槽位 {id.Value}。");
        }
        catch (Exception ex) when (ex is SaveContextException or IOException)
        {
            Fail(ex.Message);
        }
    }

    public void DrainPendingAfterFixedStep(GameEngine engine)
    {
        if (_pending == null) return;
        if (!EnsureReady(engine, announce: false)) return;

        CheckpointCoordinator? coordinator = engine.GetService(CoreServiceKeys.CheckpointCoordinator);
        if (coordinator == null)
        {
            Fail("CheckpointCoordinator 不可用，无法在干净步边界落盘。");
            _pending = null;
            return;
        }

        if (coordinator.Checkpoints.Count <= _seenCheckpoints) return;

        WorldSaveSnapshot snapshot = coordinator.Checkpoints[^1];
        _seenCheckpoints = coordinator.Checkpoints.Count;
        PendingCapture pending = _pending;
        _pending = null;

        try
        {
            if (pending.Kind == PendingKind.Manual)
            {
                _slots!.WriteSlot(pending.ManualId!.Value, snapshot);
                _selectedSlot = pending.ManualId.Value.Value;
                ClearError();
                SetStatus($"已写入 {_selectedSlot}（tick {snapshot.Header.Tick}，{SlotBytes(pending.ManualId.Value)} 字节）。");
            }
            else
            {
                SaveSlotId id = _autosave!.WriteAutosave(_slots!, snapshot);
                _selectedSlot = id.Value;
                ClearError();
                SetStatus($"已写入自动存档 {id.Value}（tick {snapshot.Header.Tick}，保留 {AutosaveRetention}）。");
            }
        }
        catch (SaveContextException ex)
        {
            Fail(ex.Message);
        }
    }

    public SavePanelState BuildPanelState(GameEngine engine)
    {
        IReadOnlyList<SavePanelSlotRow> slots = ListSlotRows();
        int autosaveCount = slots.Count(s => string.Equals(s.Kind, "autosave", StringComparison.Ordinal));
        string root = _storage?.DisplayRoot ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = _storage == null
                ? "（无存储服务）"
                : "（当前宿主未暴露绝对根路径）";
        }

        string[] autosaveLines =
        {
            $"保留数：{AutosaveRetention}",
            $"当前自动档：{autosaveCount}",
            "轮换只删 autosave，永不碰 manual",
        };

        return new SavePanelState(
            Header: "存档",
            Summary: "退出再进也能原样回来。选中槽位，再存/读/删。",
            Controls: "F5 开关面板 · F6 手动存 · F7 读档 · F8 删除 · F9 自动存",
            Status: _status,
            Error: _error,
            StorageRoot: root,
            ManualName: _manualName,
            SelectedSlot: _selectedSlot,
            PendingCapture: _pending != null,
            Slots: slots,
            AutosaveLines: autosaveLines);
    }

    private void RequestCapture(GameEngine engine, PendingCapture pending)
    {
        CheckpointCoordinator? coordinator = engine.GetService(CoreServiceKeys.CheckpointCoordinator);
        if (coordinator == null)
        {
            Fail("CheckpointCoordinator 不可用。");
            return;
        }

        _seenCheckpoints = coordinator.Checkpoints.Count;
        _pending = pending;
        coordinator.RequestCheckpoint();
        ClearError();
    }

    private bool EnsureReady(GameEngine engine, bool announce = true)
    {
        if (_slots != null && _storage != null) return true;
        BindEngine(engine);
        if (_slots != null && _storage != null) return true;
        if (announce)
        {
            Fail("宿主未提供存档存储服务（ISaveStorage）。");
        }

        return false;
    }

    private bool TryParseSelected(out SaveSlotId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(_selectedSlot)) return false;
        string[] parts = _selectedSlot.Split('/', 2, StringSplitOptions.None);
        if (parts.Length != 2) return false;
        id = new SaveSlotId(parts[0], parts[1]);
        return true;
    }

    private IReadOnlyList<SavePanelSlotRow> ListSlotRows()
    {
        if (_slots == null || _storage == null) return Array.Empty<SavePanelSlotRow>();

        var rows = new List<SavePanelSlotRow>();
        foreach (SaveSlotHeader header in _slots.ListSlots())
        {
            rows.Add(new SavePanelSlotRow(
                Kind: header.Id.Kind,
                Name: header.Id.Name,
                Slot: header.Id.Value,
                Tick: header.Header.Tick,
                MapId: header.Header.MapId,
                CreatedUtc: header.Header.CreatedUtc.ToString("u"),
                Bytes: SlotBytes(header.Id),
                SchemaVersion: header.Header.SchemaVersion,
                ModSetHashShort: ShortHash(header.Header.ModSetHash),
                RegistryFingerprintShort: ShortHash(header.Header.RegistryFingerprint)));
        }

        return rows;
    }

    private int SlotBytes(SaveSlotId id)
    {
        if (_storage == null) return 0;
        string key = id.ToStorageKey();
        return _storage.Exists(key) ? _storage.ReadAllBytes(key).Length : 0;
    }

    private static string ShortHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return value.Length <= 8 ? value : value[..8];
    }

    private void SetStatus(string status) => _status = status;

    private void ClearError() => _error = null;

    private void Fail(string message)
    {
        _error = message;
        _status = $"失败：{message}";
    }

    private enum PendingKind
    {
        Manual,
        Autosave,
    }

    private sealed record PendingCapture(PendingKind Kind, SaveSlotId? ManualId)
    {
        public static PendingCapture Manual(SaveSlotId id) => new(PendingKind.Manual, id);
        public static PendingCapture Autosave() => new(PendingKind.Autosave, null);
    }
}
