using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Mathematics;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using SaveLoadShowcaseMod.UI;
using SavePanelMod;
using SavePanelMod.Runtime;

namespace SaveLoadShowcaseMod.Runtime;

public enum SaveLoadStoryStep : byte
{
    MeetActor = 0,
    MovedOnce = 1,
    Saved = 2,
    MovedAfterSave = 3,
    Restored = 4,
}

public sealed class SaveLoadShowcaseRuntime
{
    private readonly SaveLoadShowcasePanelController _panel;
    private readonly List<string> _log = new(10);
    private GameEngine? _engine;
    private WorldSaveSnapshot? _factorySnapshot;
    private SaveLoadStoryStep _step = SaveLoadStoryStep.MeetActor;
    private string _status = "看到巡逻兵了吗？用方向键把他挪开。";
    private string? _error;
    private string _ablation = "有存档恢复";
    private bool _excludeScout = true;
    private bool _hasSavedPoint;
    private int _spawnX;
    private int _spawnY;
    private int _savedX;
    private int _savedY;
    private int _currentX;
    private int _currentY;
    private int _moveCount;
    private string _outcome = "还没读档。";

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
        if (!TryReadPatrol(engine, out _spawnX, out _spawnY))
        {
            Fail("地图上找不到巡逻兵——演示没法开演。");
        }
        else
        {
            _currentX = _spawnX;
            _currentY = _spawnY;
            _hasSavedPoint = false;
            _moveCount = 0;
            _step = SaveLoadStoryStep.MeetActor;
            SetStatus("看到巡逻兵了吗？用方向键 / 屏幕按钮把他挪开。");
        }

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
        if (engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel) && panel != null)
        {
            panel.DrainPendingAfterFixedStep(engine);
        }

        SyncPatrolFromWorld(engine);
        EmitOverlays(engine);
        RefreshPanel(engine);
    }

    public void Move(int dxCm, int dyCm)
    {
        if (Current() is not { } engine) return;
        if (!TryFindPatrol(engine, out Entity entity))
        {
            Fail("找不到巡逻兵。");
            RefreshPanel(engine);
            return;
        }

        ref WorldPositionCm pos = ref engine.World.Get<WorldPositionCm>(entity);
        var cm = pos.ToWorldCmInt2();
        pos = WorldPositionCm.FromCm(cm.X + dxCm, cm.Y + dyCm);
        StepOnce(engine);
        SyncPatrolFromWorld(engine);
        _moveCount++;
        if (_step == SaveLoadStoryStep.MeetActor || _step == SaveLoadStoryStep.Restored)
        {
            _step = SaveLoadStoryStep.MovedOnce;
            SetStatus($"巡逻兵挪到了 ({_currentX},{_currentY})。下一步：点「存这一档」。");
        }
        else if (_step == SaveLoadStoryStep.Saved || _step == SaveLoadStoryStep.MovedAfterSave)
        {
            _step = SaveLoadStoryStep.MovedAfterSave;
            SetStatus($"又挪远了，现在 ({_currentX},{_currentY})。绿幽灵还钉在存档点——点「读档回来」。");
        }
        else
        {
            SetStatus($"巡逻兵现在 ({_currentX},{_currentY})。");
        }

        RefreshPanel(engine);
    }

    public void QuickSave()
    {
        if (Current() is not { } engine) return;
        try
        {
            SavePanelRuntime panel = RequireSavePanel(engine);
            panel.SetManualName(SaveLoadShowcaseIds.SlotName);
            panel.RequestManualSave(engine);
            StepOnce(engine);
            panel.DrainPendingAfterFixedStep(engine);
            if (panel.Error != null) throw new SaveContextException(panel.Error);

            SyncPatrolFromWorld(engine);
            _savedX = _currentX;
            _savedY = _currentY;
            _hasSavedPoint = true;
            _step = SaveLoadStoryStep.Saved;
            _outcome = "已存档。绿幽灵钉在存档点。";
            SetStatus($"存好了：巡逻兵站在 ({_savedX},{_savedY})。再挪几步，然后读档看他弹回来。");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        RefreshPanel(engine);
    }

    public void QuickLoad()
    {
        if (Current() is not { } engine) return;
        try
        {
            if (!_hasSavedPoint) throw new SaveContextException("还没有存档点。先挪兵，再点「存这一档」。");

            SavePanelRuntime panel = RequireSavePanel(engine);
            panel.SetManualName(SaveLoadShowcaseIds.SlotName);
            panel.SelectSlot($"manual/{SaveLoadShowcaseIds.SlotName}");
            int beforeX = _currentX;
            int beforeY = _currentY;
            panel.RestoreSelected(engine);
            if (panel.Error != null) throw new SaveContextException(panel.Error);

            SyncPatrolFromWorld(engine);
            bool home = _currentX == _savedX && _currentY == _savedY;
            _step = SaveLoadStoryStep.Restored;
            _ablation = "有存档恢复";
            if (home)
            {
                _outcome = $"归位成功：从 ({beforeX},{beforeY}) 弹回存档点 ({_savedX},{_savedY})。";
                SetStatus(_outcome);
            }
            else
            {
                Fail($"读档后巡逻兵在 ({_currentX},{_currentY})，不是存档点 ({_savedX},{_savedY})。");
                _outcome = "归位失败。";
            }
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

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
        ApplyExcludePolicy(engine);
        SyncPatrolFromWorld(engine);
        _ablation = "无存档重置";
        _step = SaveLoadStoryStep.MeetActor;
        _outcome = $"无存档重置：兵回到出厂 ({_spawnX},{_spawnY})，不是存档点。";
        SetStatus(_outcome);
        RefreshPanel(engine);
    }

    public void AblateRestore() => QuickLoad();

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
            var id = SaveSlotId.Manual(SaveLoadShowcaseIds.SlotName);
            string key = id.ToStorageKey();
            if (!storage.Exists(key)) throw new SaveContextException("还没有可弄坏的档。先存一档。");
            int beforeX = _currentX;
            int beforeY = _currentY;
            byte[] bytes = storage.ReadAllBytes(key);
            bytes[^3] ^= 0xFF;
            storage.WriteAllBytes(key, bytes);
            panel.SelectSlot(id.Value);
            panel.RestoreSelected(engine);
            SyncPatrolFromWorld(engine);
            if (panel.Error == null)
            {
                Fail("弄坏的档竟然读成功了——校验闸门失守。");
            }
            else if (_currentX != beforeX || _currentY != beforeY)
            {
                Fail("校验失败时巡逻兵位置仍被改写——禁止静默污染。");
            }
            else
            {
                _outcome = "弄坏的档被拒读，巡逻兵原地没动。";
                _error = panel.Error;
                _status = $"{_outcome}（{panel.Error}）";
                Log(_status);
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
        _excludeScout = !_excludeScout;
        ApplyExcludePolicy(engine);
        SetStatus(_excludeScout
            ? "临时侦察挂了排除标记（灰圈）——他不进档。"
            : "临时侦察会进档。");
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
            WorldSaveSnapshot snap = slots.ReadSlot(SaveSlotId.Manual(SaveLoadShowcaseIds.SlotName));
            new WorldRestoreService().Restore(engine, snap);
            SyncPatrolFromWorld(engine);
            _hasSavedPoint = true;
            _savedX = _currentX;
            _savedY = _currentY;
            _step = SaveLoadStoryStep.Restored;
            _outcome = $"从磁盘读回：{root}/saves/manual/{SaveLoadShowcaseIds.SlotName}.ldsave";
            SetStatus($"{_outcome}。真跨进程冷启动用 Bridge save.write→重启→save.restore。");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        RefreshPanel(engine);
    }

    public SaveLoadShowcasePanelState BuildPanelState()
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

        string savedText = _hasSavedPoint ? $"({_savedX},{_savedY})" : "还没有";
        return new SaveLoadShowcasePanelState(
            Header: "存档读档",
            Hook: "把巡逻兵挪走，存一档，再挪远，读档看他弹回绿圈。",
            StepGuide: StepGuideText(_step),
            StepIndex: (int)_step,
            Controls: "WASD 挪兵 · F 存这一档 · G 读档回来 · 1 无存档重置",
            Status: _status,
            Error: _error,
            Outcome: _outcome,
            Ablation: _ablation,
            StorageRoot: root,
            ExcludeScout: _excludeScout,
            AutosaveRetention: retention,
            PatrolNow: $"({_currentX},{_currentY})",
            SavedPoint: savedText,
            MoveCount: _moveCount,
            HasSavedPoint: _hasSavedPoint,
            LogLines: _log.Count == 0 ? new[] { _status } : _log.ToArray());
    }

    public void EmitOverlays(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer overlays) return;

        overlays.Upsert(new GroundOverlayItem
        {
            StableId = SaveLoadShowcaseIds.OverlayCurrent,
            Shape = GroundOverlayShape.Ring,
            Center = WorldPlane2D.LogicCmToVisualMeters(_currentX, _currentY, 0.2f),
            Radius = 1.6f,
            InnerRadius = 1.25f,
            BorderColor = new Vector4(0.35f, 0.85f, 1f, 0.95f),
            FillColor = new Vector4(0.2f, 0.6f, 0.9f, 0.12f),
            BorderWidth = 0.08f,
        });

        if (_hasSavedPoint)
        {
            overlays.Upsert(new GroundOverlayItem
            {
                StableId = SaveLoadShowcaseIds.OverlaySaved,
                Shape = GroundOverlayShape.Ring,
                Center = WorldPlane2D.LogicCmToVisualMeters(_savedX, _savedY, 0.15f),
                Radius = 2.1f,
                InnerRadius = 1.7f,
                BorderColor = new Vector4(0.25f, 0.95f, 0.45f, 0.95f),
                FillColor = new Vector4(0.15f, 0.7f, 0.3f, 0.14f),
                BorderWidth = 0.1f,
            });
        }

        if (TryReadNamed(engine, SaveLoadShowcaseIds.ScoutName, out int sx, out int sy))
        {
            bool excluded = _excludeScout;
            overlays.Upsert(new GroundOverlayItem
            {
                StableId = SaveLoadShowcaseIds.OverlayScout,
                Shape = GroundOverlayShape.Circle,
                Center = WorldPlane2D.LogicCmToVisualMeters(sx, sy, 0.15f),
                Radius = 1.1f,
                BorderColor = excluded
                    ? new Vector4(0.55f, 0.55f, 0.55f, 0.85f)
                    : new Vector4(0.95f, 0.75f, 0.25f, 0.9f),
                FillColor = default,
                BorderWidth = 0.06f,
            });
        }
    }

    private static string StepGuideText(SaveLoadStoryStep step) => step switch
    {
        SaveLoadStoryStep.MeetActor => "① 挪巡逻兵",
        SaveLoadStoryStep.MovedOnce => "② 存这一档",
        SaveLoadStoryStep.Saved => "③ 再挪远一点",
        SaveLoadStoryStep.MovedAfterSave => "④ 读档回来",
        SaveLoadStoryStep.Restored => "✓ 归位完成——可再挪再存",
        _ => "① 挪巡逻兵",
    };

    private void OpenSavePanel(GameEngine engine)
    {
        if (!engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel) || panel == null)
        {
            Fail("SavePanelMod 未安装。本演示禁止私搓存档面板。");
            return;
        }

        panel.BindEngine(engine);
        panel.SetManualName(SaveLoadShowcaseIds.SlotName);
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
            if (!string.Equals(name.Value, SaveLoadShowcaseIds.ScoutName, StringComparison.Ordinal)) return;
            bool has = engine.World.Has<SaveExcludedTag>(entity);
            if (_excludeScout && !has) engine.World.Add(entity, new SaveExcludedTag());
            if (!_excludeScout && has) engine.World.Remove<SaveExcludedTag>(entity);
        });
    }

    private void SyncPatrolFromWorld(GameEngine engine)
    {
        if (TryReadPatrol(engine, out int x, out int y))
        {
            _currentX = x;
            _currentY = y;
        }
    }

    private bool TryFindPatrol(GameEngine engine, out Entity entity)
    {
        entity = default;
        if (engine.CurrentMapSession?.EntityIndex.TryGet(SaveLoadShowcaseIds.PatrolInstanceId, out entity) == true)
        {
            return true;
        }

        Entity found = default;
        bool ok = false;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in query, (Entity e, ref Name name, ref WorldPositionCm _) =>
        {
            if (ok) return;
            if (!string.Equals(name.Value, SaveLoadShowcaseIds.PatrolName, StringComparison.Ordinal)) return;
            found = e;
            ok = true;
        });
        entity = found;
        return ok;
    }

    private bool TryReadPatrol(GameEngine engine, out int x, out int y) =>
        TryReadNamed(engine, SaveLoadShowcaseIds.PatrolName, out x, out y);

    private static bool TryReadNamed(GameEngine engine, string actorName, out int x, out int y)
    {
        x = 0;
        y = 0;
        int rx = 0, ry = 0;
        bool ok = false;
        var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in query, (Entity _, ref Name name, ref WorldPositionCm pos) =>
        {
            if (ok) return;
            if (!string.Equals(name.Value, actorName, StringComparison.Ordinal)) return;
            var cm = pos.ToWorldCmInt2();
            rx = cm.X;
            ry = cm.Y;
            ok = true;
        });
        x = rx;
        y = ry;
        return ok;
    }

    private static void StepOnce(GameEngine engine)
    {
        if (engine.Pacemaker is TurnBasedPacemaker pace)
        {
            pace.Step();
        }

        engine.Tick(1f);
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
