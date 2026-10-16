namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 部署演示里 HUD 控件能按的动作号，取自 CrowdSimulationDebug.json、CrowdSimulationDebugView.json、
/// CrowdSimulationPlayerView.json 的 actions。控件点下去注入的就是这些动作的按下沿，和键盘走同一条输入链。
/// 没装调试视图或玩家视图时，对应的组为 null。
/// </summary>
public sealed class CrowdHudActions
{
    internal CrowdHudActions(CrowdDeployActions deploy, IReadOnlyList<string> templates, CrowdDebugViewActions? debugView, CrowdPlayerViewActions? playerView)
    {
        Deploy = new DeployGroup(deploy, templates);
        DebugView = debugView == null ? null : new DebugViewGroup(debugView);
        PlayerView = playerView == null ? null : new PlayerViewGroup(playerView);
    }

    public DeployGroup Deploy { get; }
    public DebugViewGroup? DebugView { get; }
    public PlayerViewGroup? PlayerView { get; }

    public sealed class DeployGroup
    {
        internal DeployGroup(CrowdDeployActions a, IReadOnlyList<string> templates)
        {
            CycleView = a.CycleView;
            ToggleBuild = a.ToggleBuild;
            Remove = a.Remove;
            Place = a.Place;
            Validate = a.Validate;
            ToolOff = a.ToolOff;
            ToolBoom = a.ToolBoom;
            ToolSpawn = a.ToolSpawn;
            ToolStructure = a.ToolStructure;
            ToolDemolish = a.ToolDemolish;
            Deploy = a.Deploy;
            Scatter = a.Scatter;
            Stress = a.Stress;
            ClearCaches = a.ClearCaches;
            AvoidDown = a.AvoidDown;
            AvoidUp = a.AvoidUp;
            SliceDown = a.SliceDown;
            SliceUp = a.SliceUp;
            Pause = a.Pause;
            SpeedDown = a.SpeedDown;
            SpeedUp = a.SpeedUp;
            Fuzz = a.Fuzz;
            SelectTemplate = templates;
        }

        public string CycleView { get; }
        public string ToggleBuild { get; }
        public string Remove { get; }
        public string Place { get; }
        public string Validate { get; }
        public string ToolOff { get; }
        public string ToolBoom { get; }
        public string ToolSpawn { get; }
        public string ToolStructure { get; }
        public string ToolDemolish { get; }
        public string Deploy { get; }
        public string Scatter { get; }
        public string Stress { get; }
        public string ClearCaches { get; }
        public string AvoidDown { get; }
        public string AvoidUp { get; }
        public string SliceDown { get; }
        public string SliceUp { get; }
        public string Pause { get; }
        public string SpeedDown { get; }
        public string SpeedUp { get; }
        public string Fuzz { get; }
        /// <summary>按面板格下标的选择键，与 <see cref="CrowdHudCatalog.Templates"/> 一一对应。</summary>
        public IReadOnlyList<string> SelectTemplate { get; }
    }

    public sealed class DebugViewGroup
    {
        internal DebugViewGroup(CrowdDebugViewActions a)
        {
            ToggleFunnel = a.ToggleFunnel;
            ToggleCorridor = a.ToggleCorridor;
            ToggleHpaGraph = a.ToggleHpaGraph;
            ToggleNavPolygons = a.ToggleNavPolygons;
            ToggleTileGrid = a.ToggleTileGrid;
            ToggleFlow = a.ToggleFlow;
            ToggleDensity = a.ToggleDensity;
            ToggleObstacles = a.ToggleObstacles;
            ToggleLinks = a.ToggleLinks;
            ToggleBelief = a.ToggleBelief;
            CycleUnitColor = a.CycleUnitColor;
            CycleBeliefGroup = a.CycleBeliefGroup;
            CycleInspectLayer = a.CycleInspectLayer;
            CycleInspectRadius = a.CycleInspectRadius;
            ToggleNavPanel = a.ToggleNavPanel;
            ToggleTelemetryPanel = a.ToggleTelemetryPanel;
            ProbePathFault = a.ProbePathFault;
        }

        public string ToggleFunnel { get; }
        public string ToggleCorridor { get; }
        public string ToggleHpaGraph { get; }
        public string ToggleNavPolygons { get; }
        public string ToggleTileGrid { get; }
        public string ToggleFlow { get; }
        public string ToggleDensity { get; }
        public string ToggleObstacles { get; }
        public string ToggleLinks { get; }
        public string ToggleBelief { get; }
        public string CycleUnitColor { get; }
        public string CycleBeliefGroup { get; }
        public string CycleInspectLayer { get; }
        public string CycleInspectRadius { get; }
        public string ToggleNavPanel { get; }
        public string ToggleTelemetryPanel { get; }
        public string ProbePathFault { get; }
    }

    public sealed class PlayerViewGroup
    {
        internal PlayerViewGroup(CrowdPlayerViewActions a)
        {
            NextTeam = a.NextTeam;
            SelectAll = a.SelectAll;
            ClearSelection = a.ClearSelection;
            ToggleLayer = a.ToggleLayer;
            CycleLayer = a.CycleLayer;
            ToggleFog = a.ToggleFog;
            SceneOutside = a.SceneOutside;
            SceneBehind = a.SceneBehind;
            ToggleLos = a.ToggleLos;
            ToggleRoute = a.ToggleRoute;
            DeployCountDown = a.DeployCountDown;
            DeployCountUp = a.DeployCountUp;
            CycleClickTemplate = a.CycleClickTemplate;
            CycleClickRadius = a.CycleClickRadius;
            ToggleMagicBox = a.ToggleMagicBox;
            CycleShape = a.CycleShape;
            ClickCountDown = a.ClickCountDown;
            ClickCountUp = a.ClickCountUp;
            CycleRadiusRange = a.CycleRadiusRange;
        }

        public string NextTeam { get; }
        public string SelectAll { get; }
        public string ClearSelection { get; }
        public string ToggleLayer { get; }
        public string CycleLayer { get; }
        public string ToggleFog { get; }
        public string SceneOutside { get; }
        public string SceneBehind { get; }
        public string ToggleLos { get; }
        public string ToggleRoute { get; }
        public string DeployCountDown { get; }
        public string DeployCountUp { get; }
        public string CycleClickTemplate { get; }
        public string CycleClickRadius { get; }
        public string ToggleMagicBox { get; }
        public string CycleShape { get; }
        public string ClickCountDown { get; }
        public string ClickCountUp { get; }
        public string CycleRadiusRange { get; }
    }
}
