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
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;

namespace NightRaidShowcaseMod;

internal sealed class NightRaidShowcasePresentationSystem : ISystem<float>
{
    private const string MapId = "night_raid";
    private const string SelectionOverlayKey = "night_raid.showcase.selection";
    private const int SelectionScope = 1;
    private const int HeroMarkerStableId = 910_001;
    private const int RaidCircleStableId = 910_002;
    private const float RaidCircleRadiusCm = 250f;
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
        PublishReadabilityOverlays(selected);
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

    /// <summary>
    /// Player-readability anchors: the raid circle the HUD talks about and a constant
    /// marker under the hero, so a first-time player can answer "where am I, where do I go".
    /// </summary>
    private void PublishReadabilityOverlays(Entity selected)
    {
        if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer ground)
        {
            return;
        }

        ground.TryAdd(new GroundOverlayItem
        {
            StableId = RaidCircleStableId,
            Shape = GroundOverlayShape.Ring,
            Center = new Vector3(0f, 0.02f, 0f),
            Radius = RaidCircleRadiusCm * 0.01f,
            InnerRadius = (RaidCircleRadiusCm - 22f) * 0.01f,
            FillColor = new Vector4(1f, 0.85f, 0.35f, 0.10f),
            BorderColor = new Vector4(1f, 0.85f, 0.35f, 0.95f),
            BorderWidth = 0.10f,
        });

        if (ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity hero) &&
            _engine.World.IsAlive(hero) &&
            _engine.World.TryGet(hero, out WorldPositionCm heroPos))
        {
            ground.TryAdd(new GroundOverlayItem
            {
                StableId = HeroMarkerStableId,
                Shape = GroundOverlayShape.Ring,
                Center = new Vector3(heroPos.Value.X.ToFloat() * 0.01f, 0.03f, heroPos.Value.Y.ToFloat() * 0.01f),
                Radius = 1.5f,
                InnerRadius = 1.25f,
                FillColor = new Vector4(0.2f, 0.95f, 0.95f, 0.12f),
                BorderColor = new Vector4(0.2f, 0.95f, 0.95f, 0.95f),
                BorderWidth = 0.09f,
            });
        }
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
            0 => "YOU = the cube inside the cyan ring. Move it into the big GOLD RING at the center (right click inside it) to start wave 1.",
            1 => "Press Tab to lock the nearest enemy (white ring), then right click to strike it for 20 damage. Clear all raiders.",
            _ => "Wave 2: Tab-focus the big boss (largest cube), right click repeatedly to defeat it and win."
        };

        overlay.AddRect(18, 18, 720, 188, new Vector4(0.03f, 0.05f, 0.08f, 0.9f), new Vector4(0.25f, 0.65f, 0.82f, 0.95f), 7400, wave * 10 + phase);
        overlay.AddText(34, 32, "NIGHT RAID", 24, new Vector4(0.45f, 0.9f, 1f, 1f), 7401, 1);
        overlay.AddText(34, 64, "Cyan ring = YOU   |   Gold ring = raid circle   |   White ring = your target   |   Tab: next enemy   |   Right click: move / strike", 14, new Vector4(0.92f, 0.95f, 0.98f, 1f), 7402, 1);
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
