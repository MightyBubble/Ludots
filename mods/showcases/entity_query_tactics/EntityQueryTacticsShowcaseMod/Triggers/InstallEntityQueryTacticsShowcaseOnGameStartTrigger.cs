using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using EntityQueryTacticsShowcaseMod.Runtime;
using EntityQueryTacticsShowcaseMod.Systems;

namespace EntityQueryTacticsShowcaseMod.Triggers
{
    internal sealed class InstallEntityQueryTacticsShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;

        public InstallEntityQueryTacticsShowcaseOnGameStartTrigger(IModContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(EntityQueryTacticsShowcaseIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            EntityQueryTacticsShowcaseConfig config = LoadConfig(engine);
            var state = new EntityQueryTacticsScenarioState(config)
            {
                FrontendConfig = LoadFrontendConfig(engine)
            };

            engine.GlobalContext[EntityQueryTacticsShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[EntityQueryTacticsShowcaseIds.StateKey] = state;

            TeamEntityLookup lookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("TeamEntityLookup is missing.");
            RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, lookup, config.Scenario.PlayerTeamId, config.Scenario.PlayerTeamName);
            RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, lookup, config.Scenario.EnemyTeamId, config.Scenario.EnemyTeamName);
            TeamManager.SetRelationshipSymmetric(config.Scenario.PlayerTeamId, config.Scenario.EnemyTeamId, TeamRelationship.Hostile);

            engine.InsertSystemBeforeRequired<CurrentSelectionApplySystem>(
                new EntityQueryTacticsSelectionBindingSystem(engine, state),
                SystemGroup.InputCollection);
            engine.RegisterSystem(new EntityQueryTacticsSimulationSystem(engine, state), SystemGroup.PostMovement);
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new EntityQueryTacticsPresentationSystem(engine, state));

            _context.Log(config.Logs.SystemInstalled);
            return Task.CompletedTask;
        }

        private static EntityQueryTacticsShowcaseConfig LoadConfig(GameEngine engine)
        {
            RequireConfigPipeline(engine);
            return new EntityQueryTacticsShowcaseConfigLoader(engine.ConfigPipeline).Load(
                RequireCatalog(engine),
                RequireConflictReport(engine));
        }

        private static EntityQueryTacticsFrontendConfig LoadFrontendConfig(GameEngine engine)
        {
            RequireConfigPipeline(engine);
            return new EntityQueryTacticsFrontendConfigLoader(engine.ConfigPipeline).Load(
                RequireCatalog(engine),
                RequireConflictReport(engine));
        }

        private static void RequireConfigPipeline(GameEngine engine)
        {
            if (engine.ConfigPipeline == null)
            {
                throw new InvalidOperationException("Entity query tactics showcase requires ConfigPipeline before loading config.");
            }
        }

        private static ConfigCatalog RequireCatalog(GameEngine engine)
        {
            return engine.ConfigCatalog
                ?? throw new InvalidOperationException("Entity query tactics showcase requires ConfigCatalog before loading config.");
        }

        private static ConfigConflictReport RequireConflictReport(GameEngine engine)
        {
            return engine.ConfigConflictReport
                ?? throw new InvalidOperationException("Entity query tactics showcase requires ConfigConflictReport before loading config.");
        }
    }
}
