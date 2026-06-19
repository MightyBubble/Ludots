using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.UI;
using ParticipantViewCapabilityMod.Systems;
using ParticipantViewCapabilityMod.UI;

namespace ParticipantViewCapabilityMod.Runtime;

internal sealed class ParticipantViewCapabilityRuntime
{
    private readonly ParticipantViewPanelController _panelController;
    private bool _presentationSystemInstalled;
    private ParticipantViewMode _mode = ParticipantViewMode.Players;
    private int _selectedPlayerId;
    private int _selectedTeamId;

    public ParticipantViewCapabilityRuntime()
    {
        _panelController = new ParticipantViewPanelController(this);
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!ParticipantViewCapabilityIds.IsParticipantViewMap(engine.CurrentMapSession?.MapConfig))
        {
            ClearPanelIfOwned(engine);
            return Task.CompletedTask;
        }

        EnsurePresentationSystemInstalled(engine);
        EnsureSelectedParticipant(engine);
        ApplySelectionView(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        ClearPanelIfOwned(engine);
        return Task.CompletedTask;
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (!ParticipantViewCapabilityIds.IsParticipantViewMap(engine.CurrentMapSession?.MapConfig))
        {
            ClearPanelIfOwned(engine);
            return;
        }

        EnsureSelectedParticipant(engine);
        ApplySelectionView(engine);

        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.MountOrSync(root, engine);
    }

    public ParticipantViewMode Mode => _mode;
    public int SelectedPlayerId => _selectedPlayerId;
    public int SelectedTeamId => _selectedTeamId;

    public void SelectMode(GameEngine engine, ParticipantViewMode mode)
    {
        _mode = mode;
        EnsureSelectedParticipant(engine);
        ApplySelectionView(engine);
        RefreshMountedPanel(engine);
    }

    public void SelectPlayer(GameEngine engine, int playerId)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId), "Player id must be positive.");
        }

        _mode = ParticipantViewMode.Players;
        _selectedPlayerId = playerId;
        ApplySelectionView(engine);
        RefreshMountedPanel(engine);
    }

    public void SelectTeam(GameEngine engine, int teamId)
    {
        if (teamId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(teamId), "Team id must be positive.");
        }

        _mode = ParticipantViewMode.Teams;
        _selectedTeamId = teamId;
        ApplySelectionView(engine);
        RefreshMountedPanel(engine);
    }

    private void EnsurePresentationSystemInstalled(GameEngine engine)
    {
        if (_presentationSystemInstalled)
        {
            return;
        }

        engine.RegisterPresentationSystem(new ParticipantViewPanelPresentationSystem(engine, this));
        _presentationSystemInstalled = true;
    }

    private void EnsureSelectedParticipant(GameEngine engine)
    {
        var session = engine.CurrentMapSession;
        if (session == null)
        {
            _selectedPlayerId = 0;
            _selectedTeamId = 0;
            return;
        }

        if (!TryGetAlivePlayer(engine, _selectedPlayerId, out _))
        {
            _selectedPlayerId = ResolveFirstAlivePlayerId(engine);
        }

        if (!TryGetAliveTeam(engine, _selectedTeamId, out _))
        {
            _selectedTeamId = ResolveFirstAliveTeamId(engine);
        }

        if (_mode == ParticipantViewMode.Players && _selectedPlayerId <= 0 && _selectedTeamId > 0)
        {
            _mode = ParticipantViewMode.Teams;
        }
        else if (_mode == ParticipantViewMode.Teams && _selectedTeamId <= 0 && _selectedPlayerId > 0)
        {
            _mode = ParticipantViewMode.Players;
        }
    }

    private void ApplySelectionView(GameEngine engine)
    {
        var session = engine.CurrentMapSession;
        if (session == null ||
            engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime selection)
        {
            return;
        }

        if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity viewer ||
            viewer == Entity.Null ||
            !engine.World.IsAlive(viewer))
        {
            return;
        }

        Entity[] members = _mode == ParticipantViewMode.Players
            ? _selectedPlayerId > 0
                ? ParticipantViewProjection.ResolvePlayerMembers(engine.World, session, _selectedPlayerId)
                : Array.Empty<Entity>()
            : _selectedTeamId > 0
                ? ParticipantViewProjection.ResolveTeamMembers(engine.World, session, _selectedTeamId)
                : Array.Empty<Entity>();

        ReplaceSelectionIfChanged(selection, viewer, members);
        if (!SelectionContextRuntime.TrySetCurrentView(
                engine.World,
                engine.GlobalContext,
                selection,
                viewer,
                SelectionViewKeys.Primary,
                viewer,
                SelectionSetKeys.LivePrimary,
                out _))
        {
            throw new InvalidOperationException("ParticipantViewCapabilityMod failed to bind LivePrimary as the primary selection view.");
        }
    }

    private void RefreshMountedPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.SyncIfMounted(root, engine);
        }
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private static int ResolveFirstAlivePlayerId(GameEngine engine)
    {
        var session = engine.CurrentMapSession;
        if (session == null)
        {
            return 0;
        }

        int selected = 0;
        foreach (var entry in session.PlayerEntityLookup.Entries)
        {
            if (entry.Key > 0 && engine.World.IsAlive(entry.Value) && (selected == 0 || entry.Key < selected))
            {
                selected = entry.Key;
            }
        }

        return selected;
    }

    private static int ResolveFirstAliveTeamId(GameEngine engine)
    {
        var session = engine.CurrentMapSession;
        if (session == null)
        {
            return 0;
        }

        int selected = 0;
        foreach (var entry in session.TeamEntityLookup.Entries)
        {
            if (entry.Key > 0 && engine.World.IsAlive(entry.Value) && (selected == 0 || entry.Key < selected))
            {
                selected = entry.Key;
            }
        }

        return selected;
    }

    private static bool TryGetAlivePlayer(GameEngine engine, int playerId, out Entity entity)
    {
        entity = Entity.Null;
        return playerId > 0 &&
               engine.CurrentMapSession?.PlayerEntityLookup.TryGet(playerId, out entity) == true &&
               engine.World.IsAlive(entity);
    }

    private static bool TryGetAliveTeam(GameEngine engine, int teamId, out Entity entity)
    {
        entity = Entity.Null;
        return teamId > 0 &&
               engine.CurrentMapSession?.TeamEntityLookup.TryGet(teamId, out entity) == true &&
               engine.World.IsAlive(entity);
    }

    private static void ReplaceSelectionIfChanged(
        SelectionRuntime selection,
        Entity viewer,
        Entity[] members)
    {
        int currentCount = selection.GetSelectionCount(viewer, SelectionSetKeys.LivePrimary);
        if (currentCount == members.Length)
        {
            var current = new Entity[currentCount];
            int written = selection.CopySelection(viewer, SelectionSetKeys.LivePrimary, current);
            if (written == members.Length)
            {
                bool equal = true;
                for (int i = 0; i < members.Length; i++)
                {
                    if (current[i] != members[i])
                    {
                        equal = false;
                        break;
                    }
                }

                if (equal)
                {
                    return;
                }
            }
        }

        selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, members);
    }
}
