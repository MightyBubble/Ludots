using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace NightRaidShowcaseMod;

internal sealed class NightRaidShowcasePresentationSystem : ISystem<float>
{
    private const string MapId = "night_raid";
    private const string SelectionOverlayKey = "night_raid.showcase.selection";
    private const int SelectionScope = 1;
    private readonly GameEngine _engine;
    private readonly PresentationWorldFactPublisher _facts;
    private Entity _highlighted = Entity.Null;

    public NightRaidShowcasePresentationSystem(GameEngine engine)
    {
        _engine = engine;
        if (!PresentationWorldFactPublisher.TryCreate(engine.GlobalContext, out _facts))
        {
            throw new InvalidOperationException("Night Raid showcase requires PresentationEventStream.");
        }
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.OrdinalIgnoreCase))
        {
            EndHighlight();
            return;
        }

        Entity selected = ResolveSelected();
        PublishHighlight(selected);
        PublishHud(selected);
    }

    public void AfterUpdate(in float dt) { }
    public void Dispose() => EndHighlight();

    private Entity ResolveSelected()
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity owner) || !_engine.World.IsAlive(owner))
        {
            return Entity.Null;
        }

        if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out object? targetObj) &&
            targetObj is Entity target &&
            target != Entity.Null &&
            _engine.World.IsAlive(target))
        {
            return target;
        }

        return EntityCollectionContextRuntime.TryGetPrimary(_engine.World, _engine.GlobalContext, owner, EntityCollectionKeys.CommandSource, out Entity primary)
            ? primary
            : Entity.Null;
    }

    private void PublishHighlight(Entity selected)
    {
        if (selected == Entity.Null || !_engine.World.IsAlive(selected) || !_engine.World.TryGet(selected, out WorldPositionCm position))
        {
            EndHighlight();
            return;
        }

        if (_highlighted != selected)
        {
            EndHighlight();
            _highlighted = selected;
        }

        bool boss = _engine.World.TryGet(selected, out Name name) && name.Value.Contains("Boss", StringComparison.OrdinalIgnoreCase);
        _facts.PublishWorldOverlayUpdated(SelectionOverlayKey, selected, SelectionScope,
            new Vector3(position.Value.X.ToFloat() * 0.01f, 0.08f, position.Value.Y.ToFloat() * 0.01f),
            boss ? 2.0f : 1.25f, boss ? 1.7f : 1.0f, 0.09f);
    }

    private void PublishHud(Entity selected)
    {
        ScreenOverlayBuffer? overlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
        if (overlay == null) return;

        MapVariableStore? variables = _engine.CurrentMapSession?.Variables;
        int wave = variables?.ReadInt("wave") ?? 0;
        int phase = variables?.ReadInt("phase") ?? 0;
        string selectedText = selected != Entity.Null && _engine.World.TryGet(selected, out Name name) ? name.Value : "none - click a hero or enemy";
        string details = selected != Entity.Null ? DescribeSelected(selected) : "Left click an entity to inspect it.";
        string flow = phase >= 2 ? "VICTORY - phase 2 reached" : wave switch
        {
            0 => "Select the cyan hero, then right click to enter the center circle and start wave 1.",
            1 => "Select an enemy or press Tab. Right click strikes for 20 damage.",
            _ => "Wave 2 is active. Use Tab to focus the boss, then right click to defeat it."
        };

        overlay.AddRect(18, 18, 720, 188, new Vector4(0.03f, 0.05f, 0.08f, 0.9f), new Vector4(0.25f, 0.65f, 0.82f, 0.95f), 7400, wave * 10 + phase);
        overlay.AddText(34, 32, "NIGHT RAID", 24, new Vector4(0.45f, 0.9f, 1f, 1f), 7401, 1);
        overlay.AddText(34, 64, "Left click: select   |   Tab: focus enemy   |   Right click: advance or strike", 14, new Vector4(0.92f, 0.95f, 0.98f, 1f), 7402, 1);
        overlay.AddText(34, 88, $"Wave {wave}/2   Phase {phase}/2   |   Raiders alive {CountTeam(2) + CountTeam(3)}   |   Boss {(CountTeam(4) > 0 ? "alive" : "down")}", 16, new Vector4(1f, 0.82f, 0.42f, 1f), 7403, wave * 10 + phase);
        overlay.AddText(34, 116, $"Selected: {selectedText}", 15, new Vector4(0.75f, 0.95f, 1f, 1f), 7404, StringHash(selectedText));
        overlay.AddText(34, 142, details, 14, new Vector4(0.82f, 0.86f, 0.92f, 1f), 7405, StringHash(details));
        overlay.AddText(34, 168, flow, 14, phase >= 2 ? new Vector4(0.45f, 1f, 0.62f, 1f) : new Vector4(0.95f, 0.95f, 0.95f, 1f), 7406, StringHash(flow));
    }

    private string DescribeSelected(Entity selected)
    {
        int healthId = AttributeRegistry.GetId("Health");
        float health = _engine.World.TryGet(selected, out AttributeBuffer attributes) && healthId >= 0 ? attributes.GetCurrent(healthId) : 0f;
        int team = _engine.World.TryGet(selected, out Team teamValue) ? teamValue.Id : 0;
        return $"Team {team}   Health {health:0}";
    }

    private int CountTeam(int teamId)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<Team>();
        _engine.World.Query(in query, (Entity entity, ref Team team) => { if (team.Id == teamId && _engine.World.IsAlive(entity)) count++; });
        return count;
    }

    private void EndHighlight()
    {
        if (_highlighted != Entity.Null)
        {
            _facts.PublishWorldOverlayEnded(SelectionOverlayKey, _highlighted, SelectionScope);
            _highlighted = Entity.Null;
        }
    }

    private static int StringHash(string value) => StringComparer.Ordinal.GetHashCode(value);
}
