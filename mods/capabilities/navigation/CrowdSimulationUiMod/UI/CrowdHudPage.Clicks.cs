using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// hud.html 里 ui-click 指向的方法。单键动作直接按一次；“选第 k 项”交给 <see cref="CrowdHudStepper"/> 按循环键或加减键走到位。
/// 页签、命令坞收起只改本地显示，不进输入链。
/// </summary>
internal sealed partial class CrowdHudPage
{
    private Func<int> _observeShape = null!;
    private Func<int> _observeTeam = null!;
    private Func<int> _observeLayer = null!;
    private Func<int> _observeClickType = null!;
    private Func<int> _observeClickRadius = null!;
    private Func<int> _observeRadiusRange = null!;
    private Func<int> _observeDeployCount = null!;
    private Func<int> _observeClickCount = null!;
    private Func<int> _observeUnitColor = null!;
    private Func<int> _observeInspectLayer = null!;
    private Func<int> _observeInspectRadius = null!;
    private Func<int> _observeSlices = null!;
    private Func<int> _observeAvoid = null!;

    private void InitObservers()
    {
        _observeShape = () => _player.ShapeIndex;
        _observeTeam = () => _player.LocalPlayer;
        _observeLayer = LayerTypeIndex;
        _observeClickType = () => ClickTemplateIndex(_player.ClickTemplate);
        _observeClickRadius = () => ClickTemplateIndex(_player.ClickTemplate);
        _observeRadiusRange = () => RadiusKey(WindowLo(), WindowHi());
        _observeDeployCount = () => _player.DeployCount;
        _observeClickCount = () => _player.ClickCount;
        _observeUnitColor = () => (int)_inspect.UnitColor;
        _observeInspectLayer = InspectAgentIndex;
        _observeInspectRadius = InspectProfileIndex;
        _observeSlices = () => _deploy.RebakeSlices;
        _observeAvoid = () => AvoidIndex(_deploy.AvoidHz);
    }

    private CrowdHudActions Actions => _feed.Actions ?? throw new InvalidOperationException("群体 HUD 的数据口没有动作表。");

    private CrowdHudActions.DeployGroup DeployKeys => Actions.Deploy;

    private CrowdHudActions.DebugViewGroup DebugKeys =>
        Actions.DebugView ?? throw new InvalidOperationException("没装调试视图，这个控件不可用。");

    private CrowdHudActions.PlayerViewGroup PlayerKeys =>
        Actions.PlayerView ?? throw new InvalidOperationException("没装玩家视图，这个控件不可用。");

    private CrowdHudCatalog Catalog =>
        _catalog ?? throw new InvalidOperationException("还没有会话，选项名单为空。");

    private void Press(string action)
    {
        _stepper.Cancel();
        _sink.Press(action);
        Burst();
    }

    private void Cycle(string action, Func<int> observe, int target, int maxPresses, string? then = null)
    {
        _stepper.Cycle(action, observe, target, maxPresses, then);
        Burst();
    }

    private void Seek(string down, string up, Func<int> observe, int target, int maxPresses)
    {
        _stepper.Seek(down, up, observe, target, maxPresses);
        Burst();
    }

    private void ToggleMagicBox() => Press(PlayerKeys.ToggleMagicBox);
    private void ToggleOptimisticTerrain() => Press(PlayerKeys.ToggleOptimisticTerrain);
    private void ToggleLos() => Press(PlayerKeys.ToggleLos);
    private void ToggleRoute() => Press(PlayerKeys.ToggleRoute);
    private void ToggleFog() => Press(PlayerKeys.ToggleFog);
    private void SelectAll() => Press(PlayerKeys.SelectAll);
    private void ClearSelection() => Press(PlayerKeys.ClearSelection);
    private void SceneOutside() => Press(PlayerKeys.SceneOutside);
    private void SceneBehind() => Press(PlayerKeys.SceneBehind);

    private void CycleBeliefGroup() => Press(DebugKeys.CycleBeliefGroup);
    private void ToggleFunnel() => Press(DebugKeys.ToggleFunnel);
    private void ToggleDensity() => Press(DebugKeys.ToggleDensity);
    private void ToggleCorridor() => Press(DebugKeys.ToggleCorridor);
    private void ToggleHpaGraph() => Press(DebugKeys.ToggleHpaGraph);
    private void ToggleNavPolygons() => Press(DebugKeys.ToggleNavPolygons);
    private void ToggleTileGrid() => Press(DebugKeys.ToggleTileGrid);
    private void ToggleFlow() => Press(DebugKeys.ToggleFlow);
    private void ToggleObstacles() => Press(DebugKeys.ToggleObstacles);
    private void ToggleBelief() => Press(DebugKeys.ToggleBelief);
    private void ToggleLinks() => Press(DebugKeys.ToggleLinks);

    /// <summary>着色方式是一个四档循环（关 / 队伍 / 兵种 / 优先级），两个开关各自对应其中一档。</summary>
    private void ToggleColorTeam() => PickUnitColor(CrowdUnitColorMode.Team);

    private void ToggleColorPriority() => PickUnitColor(CrowdUnitColorMode.Priority);

    private void PickUnitColor(CrowdUnitColorMode mode)
    {
        CrowdUnitColorMode target = _inspect.UnitColor == mode ? CrowdUnitColorMode.Off : mode;
        Cycle(DebugKeys.CycleUnitColor, _observeUnitColor, (int)target, 4);
    }

    private void Deploy() => Press(DeployKeys.Deploy);
    private void Scatter() => Press(DeployKeys.Scatter);
    private void Stress() => Press(DeployKeys.Stress);
    private void ClearCaches() => Press(DeployKeys.ClearCaches);
    private void Validate() => Press(DeployKeys.Validate);
    private void Fuzz() => Press(DeployKeys.Fuzz);
    private void TogglePause() => Press(DeployKeys.Pause);
    private void SpeedDown() => Press(DeployKeys.SpeedDown);
    private void SpeedUp() => Press(DeployKeys.SpeedUp);
    private void ToolOff() => Press(DeployKeys.ToolOff);
    private void ToolBoom() => Press(DeployKeys.ToolBoom);
    private void ToolSpawn() => Press(DeployKeys.ToolSpawn);
    private void ToolPlace() => Press(DeployKeys.ToolStructure);
    private void ToolRemove() => Press(DeployKeys.ToolDemolish);

    private void ToggleSpawnTool() => Press(_deploy.Tool == CrowdClickTool.Spawn ? DeployKeys.ToolOff : DeployKeys.ToolSpawn);

    private void PickTemplate(int index) => Press(DeployKeys.SelectTemplate[index]);

    private void PickSlice1() => PickSlices(1);
    private void PickSlice2() => PickSlices(2);
    private void PickSlice3() => PickSlices(3);

    private void PickSlices(int slices) => Seek(DeployKeys.SliceDown, DeployKeys.SliceUp, _observeSlices, slices, 4);

    private void PickAvoid(UiActionContext context)
    {
        CrowdHudCatalog c = Catalog;
        if (!_deploy.SessionActive || !(c.AvoidHzStep > 0)) return;
        double max = _deploy.FixedHz;
        double hz = c.AvoidHzMin + CrowdHudCanvas.SliderValueAt(ClickFraction(context)) * (max - c.AvoidHzMin);
        int target = AvoidIndex(hz);
        Seek(DeployKeys.AvoidDown, DeployKeys.AvoidUp, _observeAvoid, target, Math.Abs(target - AvoidIndex(_deploy.AvoidHz)) + 1);
    }

    private int AvoidIndex(double hz)
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || !(c.AvoidHzStep > 0) || !double.IsFinite(hz)) return 0;
        return (int)Math.Round((hz - c.AvoidHzMin) / c.AvoidHzStep);
    }

    private void PickDeployCount(UiActionContext context)
    {
        CrowdHudRange range = Catalog.DeployCount;
        int target = Snap(CrowdHudCanvas.SliderValueAt(ClickFraction(context)), range);
        Seek(PlayerKeys.DeployCountDown, PlayerKeys.DeployCountUp, _observeDeployCount, target, Presses(range, _player.DeployCount, target));
    }

    private void PickSpawnCount(UiActionContext context)
    {
        CrowdHudRange range = Catalog.ClickCount;
        int target = Snap(CrowdHudCanvas.SliderValueAt(ClickFraction(context)), range);
        Seek(PlayerKeys.ClickCountDown, PlayerKeys.ClickCountUp, _observeClickCount, target, Presses(range, _player.ClickCount, target));
    }

    private void PickPlayer(int player) => Cycle(PlayerKeys.NextTeam, _observeTeam, player, Math.Max(1, _player.PlayerCount));

    private void PickFormation(int index) => Cycle(PlayerKeys.CycleShape, _observeShape, index, Math.Max(1, Catalog.Formations.Count));

    /// <summary>先把焦点层循环到 <paramref name="type"/>，再按一次开关。</summary>
    private void ToggleUnitType(string unitTypeId)
    {
        int type = UnitTypeIndex(unitTypeId);
        if (type < 0) return;
        CrowdHudActions.PlayerViewGroup keys = PlayerKeys;
        Cycle(keys.CycleLayer, _observeLayer, type, Math.Max(1, Catalog.UnitTypes.Count), keys.ToggleLayer);
    }

    private void PickSpawnType(string unitTypeId)
    {
        CrowdHudCatalog c = Catalog;
        for (int i = 0; i < c.ClickTemplates.Count; i++)
        {
            if (string.Equals(c.ClickTemplateUnitTypeIds[i], unitTypeId, StringComparison.Ordinal))
            {
                Cycle(PlayerKeys.CycleClickTemplate, _observeClickType, i, Math.Max(1, c.ClickTemplates.Count));
                return;
            }
        }
    }

    private void PickSpawnRadius(int radiusCm)
    {
        CrowdHudCatalog c = Catalog;
        for (int i = 0; i < c.ClickTemplates.Count; i++)
        {
            if (c.ClickTemplateRadiusCm[i] == radiusCm)
            {
                Cycle(PlayerKeys.CycleClickRadius, _observeClickRadius, i, Math.Max(1, c.ClickTemplates.Count));
                return;
            }
        }
    }

    private void PickInspectLayer(int index) => Cycle(DebugKeys.CycleInspectLayer, _observeInspectLayer, index, Math.Max(1, Catalog.AgentTypes.Count));

    private void PickInspectProfile(int index) => Cycle(DebugKeys.CycleInspectRadius, _observeInspectRadius, index, Math.Max(1, Catalog.ProfileIds.Count));

    /// <summary>
    /// 原型 RadiusControl：点区间外的档扩到它，点区间端点收掉它，中间档不响应。
    /// 演示的半径窗键只会走 (0,末) → (1,1) → (1,2) … → (末,末) → (0,末) 这一圈，所以 (0, k&lt;末) 到不了，点了不动。
    /// </summary>
    private void PickDeployRadius(int radiusCm)
    {
        CrowdHudCatalog c = Catalog;
        int lo = WindowLo(), hi = WindowHi();
        int last = c.ClickTemplates.Count - 1;
        if (last < 0) return;
        int nextLo = lo, nextHi = hi;
        int template = -1;
        for (int i = 0; i < c.ClickTemplates.Count; i++)
        {
            if (c.ClickTemplateRadiusCm[i] == radiusCm)
            {
                template = i;
                break;
            }
        }

        if (template < 0) return;
        if (template < lo) nextLo = template;
        else if (template > hi) nextHi = template;
        else if (lo == hi) return;
        else if (template == lo) nextLo = lo + 1;
        else if (template == hi) nextHi = hi - 1;
        else return;
        if (nextLo == 0 && nextHi < last) return;
        int states = (last + 1) * (last + 2) / 2 + 1;
        Cycle(PlayerKeys.CycleRadiusRange, _observeRadiusRange, RadiusKey(nextLo, nextHi), states);
    }

    private static int RadiusKey(int lo, int hi) => lo * 64 + hi;

    private void PickDvPlayer() => PickDv(DvTab.Player);
    private void PickDvLayers() => PickDv(DvTab.Layers);
    private void PickDvInspect() => PickDv(DvTab.Inspect);
    private void PickDvOverlays() => PickDv(DvTab.Overlays);
    private void PickDvCache() => PickDv(DvTab.Cache);

    private void PickDv(DvTab tab)
    {
        if (_dvTab == tab) return;
        _dvTab = tab;
        _typeUnitsReady = false;
        _inspectDue = _navDue = _playerDue = 0;
        ApplyTabs();
    }

    private void PickDockDeploy() => PickDock(DockTab.Deploy);
    private void PickDockTools() => PickDock(DockTab.Tools);
    private void PickDockTests() => PickDock(DockTab.Tests);
    private void PickDockPrefs() => PickDock(DockTab.Prefs);

    /// <summary>原型 CommandDock：点开着的页签收起，点别的页签切过去并展开。</summary>
    private void PickDock(DockTab tab)
    {
        if (!_dockCollapsed && _dockTab == tab) _dockCollapsed = true;
        else
        {
            _dockTab = tab;
            _dockCollapsed = false;
        }

        ApplyTabs();
    }

    /// <summary>收起；展开时回到“部署”页。</summary>
    private void ToggleDock()
    {
        if (_dockCollapsed)
        {
            _dockTab = DockTab.Deploy;
            _dockCollapsed = false;
        }
        else
        {
            _dockCollapsed = true;
        }

        ApplyTabs();
    }

    private static float ClickFraction(UiActionContext context)
    {
        var rect = context.TargetNode.LayoutRect;
        if (context.Event is not UiPointerEvent pointer || rect.Width <= 0) return 0f;
        return (pointer.X - rect.X) / rect.Width;
    }

    private static int Snap(double t, CrowdHudRange range)
    {
        if (t >= 1d) return range.Max;
        int step = Math.Max(1, range.Step);
        int value = range.Min + (int)Math.Round(t * (range.Max - range.Min) / step) * step;
        return Math.Clamp(value, range.Min, range.Max);
    }

    private static int Presses(CrowdHudRange range, int from, int to) =>
        Math.Abs(to - from) / Math.Max(1, range.Step) + 2;
}
