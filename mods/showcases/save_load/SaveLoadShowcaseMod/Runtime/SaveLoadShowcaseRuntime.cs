using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using SaveLoadShowcaseMod.UI;
using SavePanelMod;
using SavePanelMod.Runtime;
using SaveShowcasesShared;

namespace SaveLoadShowcaseMod.Runtime;

public sealed class SaveLoadShowcaseRuntime
{
    private readonly SaveLoadShowcasePanelController _panel;
    private readonly List<string> _log = new(10);
    private GameEngine? _engine;
    private WorldSaveSnapshot? _factorySnapshot;
    private string _status = "挪单位或按 N 推进世界 → 用右侧存档面板存档 → 再推进 → 读档看绿标回来。";
    private string? _error;
    private string _ablation = "有存档恢复";
    private bool _excludeEphemeral = true;
    private int _beforeEntityCount;
    private int _afterEntityCount;
    private string _beforeDigest = "-";
    private string _afterDigest = "-";
    private readonly List<string> _diffLines = new();

    public SaveLoadShowcaseRuntime()
    {
        _panel = new SaveLoadShowcasePanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is not GameEngine engine) return Task.CompletedTask;
        if (!SaveLoadShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            _panel.ClearIfOwned();
            return Task.CompletedTask;
        }

        _engine = engine;
        engine.GetService(CoreServiceKeys.InputHandler)?.PushContext(SaveLoadShowcaseIds.InputContext);
        EnsureFactorySnapshot(engine);
        ApplyExcludePolicy(engine);
        OpenSavePanel(engine);
        CaptureBefore(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.Engine) is GameEngine engine)
        {
            engine.GetService(CoreServiceKeys.InputHandler)?.PopContext(SaveLoadShowcaseIds.InputContext);
            _panel.ClearIfOwned();
        }

        _engine = null;
        return Task.CompletedTask;
    }

    public void AdvanceFixedStep(GameEngine engine)
    {
        if (!SaveLoadShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value)) return;
        RefreshPanel(engine);
    }

    public void NudgeWorld()
    {
        if (Current() is not { } engine) return;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in query, (Entity e, ref Name name, ref WorldPositionCm pos) =>
        {
            if (name.Value == null || name.Value.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) < 0) return;
            var cm = pos.ToWorldCmInt2();
            pos = WorldPositionCm.FromCm(cm.X + 120, cm.Y + 40);
        });
        ((Ludots.Core.Engine.Pacemaker.TurnBasedPacemaker?)engine.Pacemaker)?.Step();
        engine.Tick(1f);
        SetStatus($"已推进世界。tick={engine.GameSession.CurrentTick} digest={WorldDigestLens.Short(WorldDigestLens.FromEngine(engine))}");
        RefreshPanel(engine);
    }

    public void AblateReset()
    {
        if (Current() is not { } engine) return;
        if (_factorySnapshot == null)
        {
            Fail("出厂快照尚未捕获。");
            RefreshPanel(engine);
            return;
        }

        new WorldRestoreService().Restore(engine, _factorySnapshot);
        _ablation = "无存档重置";
        CaptureAfter(engine);
        SetStatus("消融：无存档重置 — 回到出厂摆位。");
        RefreshPanel(engine);
    }

    public void AblateRestore()
    {
        if (Current() is not { } engine) return;
        try
        {
            SavePanelRuntime panel = RequireSavePanel(engine);
            if (string.IsNullOrWhiteSpace(panel.SelectedSlot))
            {
                panel.SetManualName("showcase");
                // Prefer selected; if empty try restore showcase name by selecting first.
            }

            CaptureBefore(engine);
            if (string.IsNullOrWhiteSpace(panel.SelectedSlot))
            {
                // Use formal store to restore manual/showcase if present
                if (!engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) || storage == null)
                {
                    throw new SaveContextException("缺存档存储服务。");
                }

                var slots = new SaveSlotStore(storage);
                WorldSaveSnapshot snap = slots.ReadSlot(SaveSlotId.Manual("showcase"));
                new WorldRestoreService().Restore(engine, snap);
            }
            else
            {
                panel.RestoreSelected(engine);
                if (panel.Error != null) throw new SaveContextException(panel.Error);
            }

            _ablation = "有存档恢复";
            CaptureAfter(engine);
            SetStatus("消融：有存档恢复 — 绿标是回来的状态。");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        RefreshPanel(engine);
    }

    public void TamperSelectedSlot()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (!engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) || storage == null)
            {
                throw new SaveContextException("缺存档存储服务。");
            }

            SavePanelRuntime panel = RequireSavePanel(engine);
            string slot = panel.SelectedSlot ?? "manual/showcase";
            string[] parts = slot.Split('/');
            if (parts.Length != 2) throw new SaveContextException($"槽位 '{slot}' 无效。");
            var id = new SaveSlotId(parts[0], parts[1]);
            string key = id.ToStorageKey();
            if (!storage.Exists(key)) throw new SaveContextException($"槽位 '{slot}' 不存在，先存一档。");
            byte[] bytes = storage.ReadAllBytes(key);
            bytes[^3] ^= 0xFF;
            storage.WriteAllBytes(key, bytes);
            SetStatus($"已篡改 {slot}。现在点读档应看到红色失败。");
            ClearError();
            panel.SelectSlot(slot);
            panel.RestoreSelected(engine);
            if (panel.Error != null)
            {
                Fail(panel.Error);
            }
            else
            {
                Fail("篡改后读档竟然成功 — 校验闸门失守。");
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        RefreshPanel(engine);
    }

    public void ToggleExclude()
    {
        if (Current() is not { } engine) return;
        _excludeEphemeral = !_excludeEphemeral;
        ApplyExcludePolicy(engine);
        SetStatus(_excludeEphemeral
            ? "排除开：Ephemeral Scout 挂 SaveExcludedTag，读档后应标灰不回来。"
            : "排除关：Ephemeral Scout 会进档。");
        RefreshPanel(engine);
    }

    public void AdjustRetention(int delta)
    {
        if (Current() is not { } engine) return;
        SavePanelRuntime panel = RequireSavePanel(engine);
        int next = Math.Clamp(panel.AutosaveRetention + delta, 1, 5);
        panel.SetAutosaveRetention(next);
        SetStatus($"自动存档保留数 = {next}");
        RefreshPanel(engine);
    }

    public void ColdStartStory()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (!engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) || storage == null)
            {
                throw new SaveContextException("缺存档存储服务。");
            }

            string root = string.IsNullOrWhiteSpace(storage.DisplayRoot) ? "(无绝对根)" : storage.DisplayRoot;
            var slots = new SaveSlotStore(storage);
            WorldSaveSnapshot snap = slots.ReadSlot(SaveSlotId.Manual("showcase"));
            CaptureBefore(engine);
            new WorldRestoreService().Restore(engine, snap);
            CaptureAfter(engine);
            SetStatus($"冷启动故事：从磁盘 {root}/saves/manual/showcase.ldsave 读回 tick {snap.Header.Tick}。真冷启动请用 Bridge save.write→重启→save.restore。");
            ClearError();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        RefreshPanel(engine);
    }

    internal SaveLoadShowcasePanelState BuildPanelState()
    {
        GameEngine? engine = Current();
        string root = "-";
        if (engine != null && engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) && storage != null)
        {
            root = string.IsNullOrWhiteSpace(storage.DisplayRoot) ? "(宿主未暴露绝对根)" : storage.DisplayRoot;
        }

        int retention = 3;
        if (engine != null && engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel) && panel != null)
        {
            retention = panel.AutosaveRetention;
        }

        return new SaveLoadShowcasePanelState(
            Header: "存档读档",
            Summary: "退出再进，世界原样回来。右侧是通用存档面板；左边是对照与故障旋钮。",
            Controls: "N 推进 · 1 无存档重置 · 2 有存档恢复 · T 篡改 · X 排除 · C 冷启动故事 · -/+ 自动档保留",
            Status: _status,
            Error: _error,
            Ablation: _ablation,
            StorageRoot: root,
            ExcludeEphemeral: _excludeEphemeral,
            AutosaveRetention: retention,
            BeforeDigest: WorldDigestLens.Short(_beforeDigest),
            AfterDigest: WorldDigestLens.Short(_afterDigest),
            BeforeEntityCount: _beforeEntityCount,
            AfterEntityCount: _afterEntityCount,
            DiffLines: _diffLines.Count == 0 ? new[] { "尚未对比。先存档，再推进，再恢复。" } : _diffLines.ToArray(),
            LogLines: _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    private void OpenSavePanel(GameEngine engine)
    {
        if (!engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel) || panel == null)
        {
            Fail("SavePanelMod 未安装。本 showcase 禁止私搓存档面板。");
            return;
        }

        panel.BindEngine(engine);
        panel.SetManualName("showcase");
        panel.Show(engine);
    }

    private static SavePanelRuntime RequireSavePanel(GameEngine engine)
    {
        if (!engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel) || panel == null)
        {
            throw new SaveContextException("SavePanelMod 未安装。");
        }

        return panel;
    }

    private void EnsureFactorySnapshot(GameEngine engine)
    {
        if (_factorySnapshot != null) return;
        _factorySnapshot = new WorldSnapshotService().Capture(
            engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
    }

    private void ApplyExcludePolicy(GameEngine engine)
    {
        var query = new QueryDescription().WithAll<Name>();
        engine.World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (name.Value == null || name.Value.IndexOf("Ephemeral", StringComparison.OrdinalIgnoreCase) < 0) return;
            bool has = engine.World.Has<SaveExcludedTag>(entity);
            if (_excludeEphemeral && !has) engine.World.Add(entity, new SaveExcludedTag());
            if (!_excludeEphemeral && has) engine.World.Remove<SaveExcludedTag>(entity);
        });
    }

    private void CaptureBefore(GameEngine engine)
    {
        _beforeEntityCount = CountEntities(engine);
        _beforeDigest = WorldDigestLens.FromEngine(engine);
        _diffLines.Clear();
        _diffLines.Add($"读档前 实体={_beforeEntityCount} digest={WorldDigestLens.Short(_beforeDigest)}");
        SamplePositions(engine, "前", _diffLines);
    }

    private void CaptureAfter(GameEngine engine)
    {
        _afterEntityCount = CountEntities(engine);
        _afterDigest = WorldDigestLens.FromEngine(engine);
        _diffLines.Add($"读档后 实体={_afterEntityCount} digest={WorldDigestLens.Short(_afterDigest)}");
        SamplePositions(engine, "后", _diffLines);
        bool match = string.Equals(_beforeDigest, _afterDigest, StringComparison.Ordinal);
        _diffLines.Add(match ? "对比：状态与读档前采样一致（绿）" : "对比：状态已按存档点改写（绿=回来，灰=排除）");
    }

    private static int CountEntities(GameEngine engine)
    {
        int count = 0;
        engine.World.Query(in QueryDescription.Null, (Entity _) => count++);
        return count;
    }

    private void SamplePositions(GameEngine engine, string phase, List<string> lines)
    {
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm pos) =>
        {
            if (string.IsNullOrWhiteSpace(name.Value)) return;
            bool excluded = engine.World.Has<SaveExcludedTag>(entity);
            var cm = pos.ToWorldCmInt2();
            string mark = excluded ? "灰·排除" : "绿·纳入";
            lines.Add($"{phase} [{mark}] {name.Value} @ ({cm.X},{cm.Y})");
        });
    }

    private void RefreshPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.MountOrRefresh(root, engine);
        }
    }

    private GameEngine? Current() => _engine;
    private void SetStatus(string s) { _status = s; Log(s); ClearError(); }
    private void ClearError() => _error = null;
    private void Fail(string m) { _error = m; _status = $"失败：{m}"; Log(_status); }
    private void Log(string line) { _log.Insert(0, line); if (_log.Count > 8) _log.RemoveAt(_log.Count - 1); }
}
