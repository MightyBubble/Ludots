using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using RtsCncFullShowcaseMod.Systems;

namespace RtsCncFullShowcaseMod;

public sealed class RtsCncFullShowcaseModEntry : IMod
{
    public static readonly ServiceKey<WebUiDataPlaneRuntime> DataPlaneRuntimeKey =
        new("RtsCncFullShowcase.DataPlaneRuntime");

    public static readonly ServiceKey<RtsCncFullShowcaseTopicProducer> DataPlaneTopicKey =
        new("RtsCncFullShowcase.DataPlaneTopic");

    private GameEngine? _engine;
    private WebUiDataPlaneRuntime? _dataPlaneRuntime;
    private WebUiQueuedCommandDispatcher? _commandDispatcher;
    private RtsCncFullDataPlaneSystem? _dataPlaneSystem;
    private RtsCncFullMatchRuntime? _matchRuntime;
    private RtsCncFullMatchFlowSystem? _matchFlowSystem;
    private RtsCncFullMatchHudSystem? _matchHudSystem;
    private TeamRelationshipSnapshot? _teamRelationshipSnapshot;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log("[RtsCncFullShowcaseMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
        _dataPlaneSystem?.Dispose();
        _dataPlaneSystem = null;
        _matchFlowSystem?.Dispose();
        _matchFlowSystem = null;
        _matchHudSystem?.Dispose();
        _matchHudSystem = null;
        _matchRuntime = null;

        if (_engine != null)
        {
            _engine.RemoveService(DataPlaneRuntimeKey);
            _engine.RemoveService(DataPlaneTopicKey);
            _engine = null;
        }

        if (_dataPlaneRuntime != null)
        {
            _dataPlaneRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dataPlaneRuntime = null;
        }

        _commandDispatcher?.Dispose();
        _commandDispatcher = null;

        if (_teamRelationshipSnapshot != null)
        {
            TeamManager.RestoreSnapshot(_teamRelationshipSnapshot);
            _teamRelationshipSnapshot = null;
        }
    }

    private Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine engine = context.Get(CoreServiceKeys.Engine);
        _engine = engine;

        _teamRelationshipSnapshot = TeamManager.CaptureSnapshot();
        InstallTeamRelationships();

        _matchRuntime = new RtsCncFullMatchRuntime();
        var topic = new RtsCncFullShowcaseTopicProducer(engine, _matchRuntime);
        var router = new WebUiCommandRouter(
            new RtsCncFullGenerationResolver(),
            new RtsCncFullPermissionValidator());
        router.Register("selectFaction", new RtsCncFullCommandHandler(topic));
        router.Register("startHarvest", new RtsCncFullCommandHandler(topic));
        router.Register("trainArmy", new RtsCncFullCommandHandler(topic));
        router.Register("attackEnemy", new RtsCncFullCommandHandler(topic));
        router.Register("resetMatch", new RtsCncFullCommandHandler(topic));

        _commandDispatcher = new WebUiQueuedCommandDispatcher(router);
        _dataPlaneRuntime = new WebUiDataPlaneRuntime(_commandDispatcher);
        _dataPlaneRuntime.RegisterTopic(topic);

        var pump = new WebUiDataPlaneTickPump(_dataPlaneRuntime, _commandDispatcher);
        pump.TrackTopic(RtsCncFullShowcaseTopicProducer.TopicName);
        _dataPlaneSystem = new RtsCncFullDataPlaneSystem(pump);
        engine.RegisterSystem(_dataPlaneSystem, SystemGroup.InputCollection);
        _matchFlowSystem = new RtsCncFullMatchFlowSystem(engine, _matchRuntime);
        engine.RegisterSystem(_matchFlowSystem, SystemGroup.InputCollection);
        _matchHudSystem = new RtsCncFullMatchHudSystem(engine, _matchRuntime);
        engine.RegisterSystem(_matchHudSystem, SystemGroup.EventDispatch);
        engine.SetService(DataPlaneRuntimeKey, _dataPlaneRuntime);
        engine.SetService(DataPlaneTopicKey, topic);

        return Task.CompletedTask;
    }

    private static void InstallTeamRelationships()
    {
        for (int teamA = 1; teamA <= 5; teamA++)
        {
            for (int teamB = teamA + 1; teamB <= 5; teamB++)
            {
                TeamManager.SetRelationshipSymmetric(teamA, teamB, TeamRelationship.Hostile);
            }
        }
    }
}
