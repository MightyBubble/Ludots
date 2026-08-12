using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using ScopeSwitchShowcaseMod.Input;
using ScopeSwitchShowcaseMod.UI;

namespace ScopeSwitchShowcaseMod.Runtime;

public sealed class ScopeSwitchRuntime
{
    private readonly IModContext _context;
    private readonly ScopeSwitchPanelController _panelController;
    private readonly List<ScopeRuntimeEntry> _scopes = new(8);
    private readonly List<EntityRuntimeEntry> _entities = new(16);
    private readonly Entity[] _scopeBuffer = new Entity[32];
    private readonly Entity[] _visibleBuffer = new Entity[32];
    private readonly string[] _visibleLines = new string[32];
    private readonly string[] _selectedLines = new string[32];
    private readonly string[] _scopeLines = new string[8];
    private ScopeSwitchConfig? _config;
    private GameEngine? _engine;
    private Entity _viewer = Entity.Null;
    private bool _scenarioReady;
    private int _activeScopeIndex;
    private string _status = "Load the scope switch showcase map.";

    public ScopeSwitchRuntime(IModContext context)
    {
        _context = context;
        _panelController = new ScopeSwitchPanelController(this);
    }

    public ScopeSwitchSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!ScopeSwitchIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
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

        if (ScopeSwitchIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine)
    {
        if (!ScopeSwitchIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureScenario(engine);
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            for (int i = 0; i < _scopes.Count; i++)
            {
                if (input.PressedThisFrame(_scopes[i].Config.ActionId))
                {
                    SelectScope(engine, i);
                    break;
                }
            }
        }

        RefreshPanelInternal(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (ScopeSwitchIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    public void SelectScope(GameEngine engine, int index)
    {
        EnsureScenario(engine);
        if ((uint)index >= (uint)_scopes.Count)
        {
            return;
        }

        _activeScopeIndex = index;
        ScopeRuntimeEntry scope = _scopes[index];
        ScopeSwitchSnapshot snapshot = BuildSnapshot();
        _status = $"Scope {scope.Config.Label}: visible {snapshot.VisibleCount}, selected {snapshot.SelectedCount}.";
        RefreshPanelInternal(engine);
    }

    internal ScopeSwitchPanelState BuildPanelState()
    {
        ScopeSwitchSnapshot snapshot = BuildSnapshot();
        return new ScopeSwitchPanelState(
            Header: _config?.Header ?? "Scope Switch Showcase",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            ActiveLine: $"Active scope {snapshot.ActiveScopeLabel} | visible {snapshot.VisibleCount} | selected {snapshot.SelectedCount}",
            ScopeLines: snapshot.ScopeLines,
            VisibleLines: snapshot.VisibleLabels,
            SelectedLines: snapshot.SelectedLabels,
            Status: snapshot.Status);
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/Association/scope_switch_config.json");
        _config = ScopeSwitchConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Scope switch config was not loaded.");
        }

        BuildEntities(engine);
        BuildScopes(engine);
        PublishKnowledge(engine);
        _scenarioReady = true;
        SelectScope(engine, 0);
    }

    private void BuildEntities(GameEngine engine)
    {
        _entities.Clear();
        for (int i = 0; i < _config!.Entities.Length; i++)
        {
            ScopeSwitchEntityConfig cfg = _config.Entities[i];
            Entity entity = engine.World.Create();
            engine.World.Add(entity, new Name { Value = cfg.Label });
            engine.World.Add(entity, new MapEntity { MapId = ScopeSwitchIds.ShowcaseMap });
            engine.World.Add(entity, new ScopeRefBuffer());
            _entities.Add(new EntityRuntimeEntry(cfg, entity));
            if (string.Equals(cfg.Id, _config.ViewerEntity, StringComparison.Ordinal))
            {
                _viewer = entity;
            }
        }

        if (_viewer == Entity.Null)
        {
            throw new InvalidOperationException($"Scope switch viewer entity '{_config.ViewerEntity}' was not found.");
        }

        ClientLocalSeatBindings.BindSoleSeat(engine, _viewer);
    }

    private void BuildScopes(GameEngine engine)
    {
        _scopes.Clear();
        for (int i = 0; i < _config!.Scopes.Length; i++)
        {
            ScopeSwitchScopeConfig cfg = _config.Scopes[i];
            int scopeKeyId = i + 1;
            Entity host = ResolveEntity(cfg.Host);
            if (!engine.World.Has<ScopeMembershipRevision>(host))
            {
                engine.World.Add(host, new ScopeMembershipRevision());
            }

            ScopeKey key = string.Equals(cfg.Kind, "Self", StringComparison.Ordinal)
                ? ScopeKey.Self
                : ScopeKey.Named(scopeKeyId, host);
            _scopes.Add(new ScopeRuntimeEntry(cfg, scopeKeyId, host, key));

            if (!string.Equals(cfg.Kind, "Named", StringComparison.Ordinal))
            {
                continue;
            }

            BindScopeRef(engine, host, scopeKeyId, host);
            for (int j = 0; j < cfg.Members.Length; j++)
            {
                BindScopeRef(engine, ResolveEntity(cfg.Members[j]), scopeKeyId, host);
            }
        }
    }

    private void PublishKnowledge(GameEngine engine)
    {
        KnowledgeProjectionStore store = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("KnowledgeProjectionStore missing.");
        var empty = KnowledgeIdMask256.Empty;
        for (int i = 0; i < _scopes.Count; i++)
        {
            ScopeRuntimeEntry scope = _scopes[i];
            Entity source = scope.Key.Kind == ScopeKind.Self ? _viewer : scope.Host;
            for (int j = 0; j < scope.Config.Visible.Length; j++)
            {
                Entity target = ResolveEntity(scope.Config.Visible[j]);
                store.Upsert(
                    source,
                    target,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        empty,
                        empty,
                        empty,
                        source,
                        observedTick: i + 1,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            }
        }
    }

    private ScopeSwitchSnapshot BuildSnapshot()
    {
        if (_config == null || _scopes.Count == 0)
        {
            return new ScopeSwitchSnapshot(string.Empty, "n/a", 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), _status);
        }

        ScopeRuntimeEntry scope = _scopes[_activeScopeIndex];
        int visibleCount = ResolveVisible(scope);
        int selectedCount = ResolveSelected(scope);
        int scopeLineCount = BuildScopeLines(scope);
        return new ScopeSwitchSnapshot(
            scope.Config.Id,
            scope.Config.Label,
            visibleCount,
            selectedCount,
            CopyLines(_visibleLines, visibleCount),
            CopyLines(_selectedLines, selectedCount),
            CopyLines(_scopeLines, scopeLineCount),
            _status);
    }

    private int ResolveVisible(in ScopeRuntimeEntry scope)
    {
        if (_viewer == Entity.Null)
        {
            return 0;
        }

        KnowledgeProjectionResolver resolver = _engine?.GetService(CoreServiceKeys.KnowledgeProjectionResolver)
            ?? throw new InvalidOperationException("KnowledgeProjectionResolver missing.");
        var roleContext = new RoleResolverContext(
            actor: _viewer,
            subject: _viewer,
            viewer: _viewer,
            explicitScopeHost: scope.Host);
        Span<Entity> scopeMembers = _scopeBuffer;
        int count = 0;
        for (int i = 0; i < _entities.Count && count < _visibleBuffer.Length; i++)
        {
            Entity target = _entities[i].Entity;
            ScopeKey key = scope.Key;
            if (resolver.TryResolve(
                    _viewer,
                    target,
                    currentTick: 1,
                    in key,
                    in roleContext,
                    scopeMembers,
                    out _))
            {
                _visibleBuffer[count] = target;
                _visibleLines[count] = _entities[i].Config.Label;
                count++;
            }
        }

        return count;
    }

    private int ResolveSelected(in ScopeRuntimeEntry scope)
    {
        ScopeResolver resolver = _engine?.GetService(CoreServiceKeys.ScopeResolver)
            ?? throw new InvalidOperationException("ScopeResolver missing.");
        var roleContext = new RoleResolverContext(
            actor: _viewer,
            subject: _viewer,
            viewer: _viewer,
            explicitScopeHost: scope.Host);
        ScopeKey key = scope.Key;
        int count = resolver.ResolveMembers(in key, in roleContext, _scopeBuffer);
        int written = 0;
        for (int entityIndex = 0; entityIndex < _entities.Count && written < _selectedLines.Length; entityIndex++)
        {
            Entity candidate = _entities[entityIndex].Entity;
            for (int scopeIndex = 0; scopeIndex < count; scopeIndex++)
            {
                if (_scopeBuffer[scopeIndex] != candidate)
                {
                    continue;
                }

                _selectedLines[written++] = _entities[entityIndex].Config.Label;
                break;
            }
        }

        return written;
    }

    private int BuildScopeLines(in ScopeRuntimeEntry activeScope)
    {
        int written = 0;
        for (int i = 0; i < _scopes.Count && written < _scopeLines.Length; i++)
        {
            ScopeRuntimeEntry scope = _scopes[i];
            string marker = scope.Config.Id == activeScope.Config.Id ? "*" : " ";
            _scopeLines[written++] = $"{marker} {scope.Config.Label}: {scope.Config.Members.Length} member(s)";
        }

        return written;
    }

    private Entity ResolveEntity(string id)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if (string.Equals(_entities[i].Config.Id, id, StringComparison.Ordinal))
            {
                return _entities[i].Entity;
            }
        }

        throw new InvalidOperationException($"Scope switch entity '{id}' was not found.");
    }

    private string ResolveLabel(Entity entity)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].Entity == entity)
            {
                return _entities[i].Config.Label;
            }
        }

        return $"Entity#{entity.Id}";
    }

    private static void BindScopeRef(GameEngine engine, Entity entity, int scopeKeyId, Entity host)
    {
        ref ScopeRefBuffer refs = ref engine.World.Get<ScopeRefBuffer>(entity);
        if (!refs.TryAdd(scopeKeyId, host))
        {
            throw new InvalidOperationException($"ScopeRefBuffer capacity exceeded for entity {entity.Id}.");
        }
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
        if (input == null || !input.HasContext(ScopeSwitchInputContexts.Showcase))
        {
            return;
        }

        input.PushContext(ScopeSwitchInputContexts.Showcase);
    }

    private void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext(ScopeSwitchInputContexts.Showcase);
    }

    private void ResetScenario()
    {
        _scenarioReady = false;
        _engine = null;
        _viewer = Entity.Null;
        _activeScopeIndex = 0;
        _entities.Clear();
        _scopes.Clear();
        _status = "Load the scope switch showcase map.";
    }

    private readonly record struct EntityRuntimeEntry(ScopeSwitchEntityConfig Config, Entity Entity);

    private readonly record struct ScopeRuntimeEntry(ScopeSwitchScopeConfig Config, int ScopeKeyId, Entity Host, ScopeKey Key);
}
