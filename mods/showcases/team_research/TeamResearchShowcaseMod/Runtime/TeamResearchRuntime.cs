using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using TeamResearchShowcaseMod.Input;
using TeamResearchShowcaseMod.UI;

namespace TeamResearchShowcaseMod.Runtime;

public sealed class TeamResearchRuntime
{
    private readonly IModContext _context;
    private readonly TeamResearchPanelController _panelController;
    private readonly List<MemberRuntimeEntry> _members = new(8);
    private readonly Entity[] _memberBuffer = new Entity[16];
    private readonly string[] _memberLines = new string[8];
    private readonly string[] _unlockLines = new string[6];
    private TeamResearchConfig? _config;
    private GameEngine? _engine;
    private Entity _teamHost = Entity.Null;
    private Entity _researcher = Entity.Null;
    private bool _scenarioReady;
    private int _activeMemberCount;
    private int _progress;
    private int _lastContribution;
    private bool _unlocked;
    private string _status = "Load the team research showcase map.";

    public TeamResearchRuntime(IModContext context)
    {
        _context = context;
        _panelController = new TeamResearchPanelController(this);
    }

    public TeamResearchSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!TeamResearchIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        _engine = engine;
        EnsureConfig();
        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine);
        RefreshPanelInternal(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (TeamResearchIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void AddNextMember(GameEngine engine)
    {
        EnsureScenario(engine);
        if (_activeMemberCount >= _members.Count)
        {
            _status = "All configured members are already assigned.";
            RefreshPanelInternal(engine);
            return;
        }

        _activeMemberCount++;
        PublishMemberCollection(engine);
        _status = $"Added {_members[_activeMemberCount - 1].Config.Label}; team has {_activeMemberCount} active member(s).";
        RefreshPanelInternal(engine);
    }

    public void ResearchPulse(GameEngine engine)
    {
        EnsureScenario(engine);
        if (_config == null)
        {
            return;
        }

        TeamResearchSnapshot before = BuildSnapshot();
        if (!before.RequirementSatisfied)
        {
            _lastContribution = 0;
            _status = $"Need {_config.RequiredMembers} active member(s) before {_config.UnlockLabel} can progress.";
            RefreshPanelInternal(engine);
            return;
        }

        int contribution = ComputeContribution();
        _lastContribution = contribution;
        _progress = Math.Min(_config.ResearchCost, _progress + contribution);
        if (_progress >= _config.ResearchCost && !_unlocked)
        {
            CompleteProgression(engine);
            _unlocked = true;
            _status = $"{_config.UnlockLabel} unlocked for every team member.";
        }
        else
        {
            _status = $"Research pulse contributed {contribution}; {_progress}/{_config.ResearchCost}.";
        }

        RefreshPanelInternal(engine);
    }

    public void ResetResearch(GameEngine engine)
    {
        EnsureScenario(engine);
        _activeMemberCount = 1;
        _progress = 0;
        _lastContribution = 0;
        _unlocked = false;
        if (engine.World.IsAlive(_teamHost) && engine.World.Has<ProgressionStateBuffer>(_teamHost))
        {
            engine.World.Set(_teamHost, new ProgressionStateBuffer());
        }

        PublishMemberCollection(engine);
        _status = "Research reset to one active member.";
        RefreshPanelInternal(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (TeamResearchIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal TeamResearchPanelState BuildPanelState()
    {
        TeamResearchSnapshot snapshot = BuildSnapshot();
        return new TeamResearchPanelState(
            Header: _config?.Header ?? "Team Research",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            ProgressLine: $"Progress {snapshot.Progress}/{snapshot.ResearchCost} | last +{snapshot.LastContribution}",
            RequirementLine: $"Requirement {snapshot.ActiveMemberCount}/{snapshot.RequiredMemberCount} active members => {(snapshot.RequirementSatisfied ? "satisfied" : "blocked")}",
            MemberLines: snapshot.MemberLines,
            UnlockLines: snapshot.UnlockLines,
            Status: snapshot.Status);
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/Progression/team_research_config.json");
        _config = TeamResearchConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Team research config was not loaded.");
        }

        BuildScenario(engine);
        PublishMemberCollection(engine);
        _scenarioReady = true;
        _status = "Team research cell ready; add members or pulse research.";
    }

    private void BuildScenario(GameEngine engine)
    {
        World world = engine.World;
        _teamHost = world.Create(
            new Name { Value = _config!.TeamHostName },
            new MapEntity { MapId = TeamResearchIds.ShowcaseMap },
            new ProgressionStateBuffer(),
            new ScopeMembershipRevision());
        _researcher = world.Create(
            new Name { Value = _config.ResearcherName },
            new MapEntity { MapId = TeamResearchIds.ShowcaseMap },
            new ScopeRefBuffer(),
            new ScopeMemberTag());

        ScopeKeyRegistry scopeKeys = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
            ?? throw new InvalidOperationException("ScopeKeyRegistry missing.");
        int teamScopeId = scopeKeys.GetId(_config.TeamScope);
        if (teamScopeId <= 0)
        {
            throw new InvalidOperationException($"Team research scope '{_config.TeamScope}' is not registered.");
        }

        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        if (!evaluator.TryBindScope(_researcher, teamScopeId, _teamHost))
        {
            throw new InvalidOperationException("Team research actor could not bind to the configured team scope.");
        }

        int memberTagId = TagRegistry.Register(_config.MemberTag);
        _members.Clear();
        for (int i = 0; i < _config.Members.Length; i++)
        {
            MemberConfig memberConfig = _config.Members[i];
            var tags = default(GameplayTagContainer);
            tags.AddTag(memberTagId);
            Entity member = world.Create(
                new Name { Value = memberConfig.Label },
                new MapEntity { MapId = TeamResearchIds.ShowcaseMap },
                tags,
                new TagCountContainer());
            _members.Add(new MemberRuntimeEntry(memberConfig, member));
            if (memberConfig.StartsActive)
            {
                _activeMemberCount++;
            }
        }

        if (_activeMemberCount <= 0)
        {
            _activeMemberCount = 1;
        }
    }

    private void PublishMemberCollection(GameEngine engine)
    {
        if (_config == null || _teamHost == Entity.Null)
        {
            return;
        }

        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore missing.");
        for (int i = 0; i < _activeMemberCount; i++)
        {
            _memberBuffer[i] = _members[i].Entity;
        }

        collections.Replace(
            _teamHost,
            EntityCollectionDescriptor.Create(
                _config.CollectionKey,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                contextEntity: _teamHost,
                primaryEntity: _teamHost,
                title: _config.UnlockLabel,
                summary: "Team research membership"),
            _memberBuffer.AsSpan(0, _activeMemberCount));

        ScopeResolver resolver = engine.GetService(CoreServiceKeys.ScopeResolver)
            ?? throw new InvalidOperationException("ScopeResolver missing.");
        resolver.BumpMembershipRevision(_teamHost);
    }

    private int ComputeContribution()
    {
        if (_config == null)
        {
            return 0;
        }

        int totalMultiplier = 0;
        for (int i = 0; i < _activeMemberCount; i++)
        {
            totalMultiplier += Math.Max(1, _members[i].Config.ContributionMultiplier);
        }

        return totalMultiplier * _config.ContributionPerMember;
    }

    private void CompleteProgression(GameEngine engine)
    {
        if (_config == null)
        {
            return;
        }

        int progressionId = ProgressionIdRegistry.GetId(_config.ProgressionId);
        if (progressionId <= 0)
        {
            throw new InvalidOperationException($"Progression '{_config.ProgressionId}' is not registered.");
        }

        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        if (!evaluator.TryComplete(_teamHost, progressionId))
        {
            throw new InvalidOperationException($"Failed to complete team research progression '{_config.ProgressionId}'.");
        }
    }

    private bool EvaluateRequirement()
    {
        if (_config == null || _researcher == Entity.Null)
        {
            return false;
        }

        GameEngine? engine = _engine;
        if (engine == null)
        {
            return false;
        }

        int requirementId = ProgressionRequirementIdRegistry.GetId(_config.RequirementId);
        if (requirementId <= 0)
        {
            return false;
        }

        ProgressionRequirementEvaluator evaluator = engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator)
            ?? throw new InvalidOperationException("ProgressionRequirementEvaluator missing.");
        var roleContext = new RoleResolverContext(actor: _researcher, subject: _researcher);
        return evaluator.Evaluate(requirementId, in roleContext);
    }

    private bool HasUnlocked()
    {
        if (_config == null || _teamHost == Entity.Null)
        {
            return false;
        }

        int progressionId = ProgressionIdRegistry.GetId(_config.ProgressionId);
        if (progressionId <= 0 || _engine is not GameEngine engine || !engine.World.Has<ProgressionStateBuffer>(_teamHost))
        {
            return false;
        }

        ref readonly ProgressionStateBuffer state = ref engine.World.Get<ProgressionStateBuffer>(_teamHost);
        return state.HasCompleted(progressionId);
    }

    private TeamResearchSnapshot BuildSnapshot()
    {
        if (_config == null)
        {
            return new TeamResearchSnapshot(0, 0, 0, 0, 0, 0, false, false, _status, Array.Empty<string>(), Array.Empty<string>());
        }

        bool requirementSatisfied = EvaluateRequirement();
        bool unlocked = _unlocked || HasUnlocked();
        int memberLineCount = BuildMemberLines();
        int unlockLineCount = BuildUnlockLines(unlocked);
        return new TeamResearchSnapshot(
            _activeMemberCount,
            _members.Count,
            _config.RequiredMembers,
            _progress,
            _config.ResearchCost,
            _lastContribution,
            requirementSatisfied,
            unlocked,
            _status,
            CopyLines(_memberLines, memberLineCount),
            CopyLines(_unlockLines, unlockLineCount));
    }

    private int BuildMemberLines()
    {
        int count = 0;
        for (int i = 0; i < _members.Count && count < _memberLines.Length; i++)
        {
            MemberRuntimeEntry entry = _members[i];
            string marker = i < _activeMemberCount ? "*" : " ";
            _memberLines[count++] = $"{marker} {entry.Config.Label} x{Math.Max(1, entry.Config.ContributionMultiplier)}";
        }

        return count;
    }

    private int BuildUnlockLines(bool unlocked)
    {
        if (_config == null)
        {
            return 0;
        }

        int count = 0;
        _unlockLines[count++] = $"{_config.UnlockLabel}: {(unlocked ? "unlocked" : "locked")}";
        _unlockLines[count++] = $"Source: ScopeResolver -> EntityCollectionStore[{_config.CollectionKey}]";
        _unlockLines[count++] = $"DAG: {_config.RequirementId}";
        return count;
    }

    private static string[] CopyLines(string[] source, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        var output = new string[count];
        Array.Copy(source, output, count);
        return output;
    }

    private void RefreshPanelInternal(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ActivateInputContext(PlayerInputHandler? input)
    {
        if (input == null || !input.HasContext(TeamResearchInputContexts.Showcase))
        {
            return;
        }

        input.PushContext(TeamResearchInputContexts.Showcase);
    }

    private void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext(TeamResearchInputContexts.Showcase);
    }

    private void ResetScenario()
    {
        _scenarioReady = false;
        _engine = null;
        _teamHost = Entity.Null;
        _researcher = Entity.Null;
        _members.Clear();
        _activeMemberCount = 0;
        _progress = 0;
        _lastContribution = 0;
        _unlocked = false;
        _status = "Load the team research showcase map.";
    }

    private readonly record struct MemberRuntimeEntry(MemberConfig Config, Entity Entity);
}
