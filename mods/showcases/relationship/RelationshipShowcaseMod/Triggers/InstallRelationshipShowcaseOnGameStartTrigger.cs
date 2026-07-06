using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using RelationshipShowcaseMod.Runtime;
using RelationshipShowcaseMod.Systems;

namespace RelationshipShowcaseMod.Triggers
{
    internal sealed class InstallRelationshipShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly RelationshipShowcaseScenarioState _state;
        private readonly RelationshipShowcaseConfig _config;

        public InstallRelationshipShowcaseOnGameStartTrigger(IModContext context, RelationshipShowcaseScenarioState state, RelationshipShowcaseConfig config)
        {
            _context = context;
            _state = state;
            _config = config;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(RelationshipShowcaseIds.InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[RelationshipShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[RelationshipShowcaseIds.StateKey] = _state;
            TeamEntityLookup lookup = engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("TeamEntityLookup is missing.");
            foreach (TeamDefinition team in _config.Scenario.Teams)
            {
                RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, lookup, team.Id, team.Name);
            }

            foreach (var relation in _config.TeamRelations)
            {
                TeamRelationship parsed = Enum.Parse<TeamRelationship>(relation.Relationship, ignoreCase: true);
                TeamManager.SetRelationshipSymmetric(relation.TeamA, relation.TeamB, parsed);
            }

            engine.RegisterSystem(new RelationshipShowcaseSimulationSystem(engine, _state), SystemGroup.InputCollection);
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new RelationshipShowcasePresentationSystem(engine, _state));

            _context.Log(_config.Logs.SystemInstalled);
            return Task.CompletedTask;
        }
    }
}
