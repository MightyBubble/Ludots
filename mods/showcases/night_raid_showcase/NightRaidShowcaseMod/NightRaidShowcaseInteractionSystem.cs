using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace NightRaidShowcaseMod;

internal sealed class NightRaidShowcaseInteractionSystem : ISystem<float>
{
    private const string MapId = "night_raid";
    private const string CommandActionId = "Command";
    private const float StrikeDamage = 20f;
    private readonly GameEngine _engine;
    private readonly int _healthAttributeId;

    public NightRaidShowcaseInteractionSystem(GameEngine engine)
    {
        _engine = engine;
        _healthAttributeId = AttributeRegistry.GetId("Health");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.OrdinalIgnoreCase) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input ||
            !input.PressedThisFrame(CommandActionId))
        {
            return;
        }

        Entity target = ResolveTarget();
        if (target == Entity.Null || !_engine.World.IsAlive(target))
        {
            return;
        }

        if (_engine.World.TryGet(target, out Name name) && string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
        {
            _engine.World.Set(target, new WorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) });
            return;
        }

        if (_healthAttributeId < 0 ||
            !_engine.World.TryGet(target, out Team team) ||
            team.Id is < 2 or > 4 ||
            !_engine.World.Has<AttributeBuffer>(target))
        {
            return;
        }

        ref AttributeBuffer attributes = ref _engine.World.Get<AttributeBuffer>(target);
        float nextHealth = MathF.Max(0f, attributes.GetCurrent(_healthAttributeId) - StrikeDamage);
        attributes.SetCurrent(_healthAttributeId, nextHealth);
        if (nextHealth <= 0f)
        {
            if (!_engine.World.Has<PresentationStableId>(target))
            {
                throw new InvalidOperationException("Night Raid strike target is missing PresentationStableId.");
            }

            PresentationEntityLifecycle.RequestDestroy(_engine.World, target, "Night Raid showcase strike");
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out object? activeTarget) && activeTarget is Entity tabTarget && tabTarget == target)
            {
                _engine.GlobalContext.Remove(CoreServiceKeys.TabTargetEntity.Name);
            }
        }
    }

    private Entity ResolveTarget()
    {
        if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out object? targetObj) &&
            targetObj is Entity tabTarget &&
            tabTarget != Entity.Null &&
            _engine.World.IsAlive(tabTarget))
        {
            return tabTarget;
        }

        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity owner))
        {
            return Entity.Null;
        }

        return EntityCollectionContextRuntime.TryGetPrimary(_engine.World, _engine.GlobalContext, owner, EntityCollectionKeys.CommandSource, out Entity primary)
            ? primary
            : Entity.Null;
    }
}
