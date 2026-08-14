using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Scripting;

namespace RtsStarCraftFullShowcaseMod.Systems;

internal sealed class RtsScFullScenarioSystem : ISystem<float>
{
    public const string PhaseKey = "scf.scenario.phase";
    public const string LastEventKey = "scf.scenario.lastEvent";
    public const string MiningWorkersKey = "scf.scenario.miningWorkers";
    public const string ArmyCountKey = "scf.scenario.armyCount";
    public const string EnemyHqNameKey = "scf.scenario.enemyHqName";
    public const string EnemyHqHealthKey = "scf.scenario.enemyHqHealth";
    public const string EnemyHqMaxHealthKey = "scf.scenario.enemyHqMaxHealth";
    public const string EnemyTeamAliveKey = "scf.scenario.enemyTeamAlive";
    public const string VictoryKey = "scf.scenario.victory";

    private static readonly QueryDescription NameQuery = new QueryDescription().WithAll<Name>();
    private static readonly QueryDescription TeamAttributeQuery = new QueryDescription().WithAll<Team, AttributeBuffer>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<Team, EntityTemplateKeyRef>();

    private readonly GameEngine _engine;
    private readonly EffectRequestQueue _requests;
    private readonly EntityTemplateKeyRegistry _templateKeys;
    private readonly Dictionary<int, int> _damageEffectByTemplateKeyId = new();
    private int _healthAttributeId;
    private int _assaultSignalAttributeId;
    private int _miningEffectTemplateId;
    private int _volleyFrame;
    private bool _assaultStarted;
    private bool _victory;
    private bool _zergTeamDestroyed;
    private string _lastEvent = "Opening: select SCV and start mineral harvesting.";

    public RtsScFullScenarioSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _requests = engine.GetService(CoreServiceKeys.EffectRequestQueue) as EffectRequestQueue
            ?? throw new InvalidOperationException("EffectRequestQueue is required.");
        _templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry) as EntityTemplateKeyRegistry
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry is required.");
    }

    public void Initialize()
    {
        _healthAttributeId = EnsureAttribute("Health");
        _assaultSignalAttributeId = EnsureAttribute("AssaultSignal");
        _miningEffectTemplateId = EffectTemplateIdRegistry.GetId("Effect.Scf.Mining.Minerals");
        if (_miningEffectTemplateId <= 0)
        {
            throw new InvalidOperationException("Effect.Scf.Mining.Minerals must be registered for the StarCraft full scenario.");
        }
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!IsStarCraftFullMapActive())
        {
            return;
        }

        ConsumeAssaultSignals();

        Entity enemyHq = FindEntityByTeamAndName(2, "Hatchery");
        int miningWorkers = CountMiningWorkers();
        int armyCount = CountPlayerArmy();
        int enemyTeamAlive = CountAliveTeamEntities(2);
        float enemyHealth = ReadCurrent(enemyHq, _healthAttributeId);
        float enemyMaxHealth = ReadBase(enemyHq, _healthAttributeId);

        if (!_victory && _assaultStarted && enemyHq != Entity.Null)
        {
            PublishAssaultVolley(enemyHq);
        }

        if (!_victory && enemyHq != Entity.Null && ReadCurrent(enemyHq, _healthAttributeId) <= 0f)
        {
            _victory = true;
            _lastEvent = "Victory: Zerg Hatchery destroyed and opponent eliminated.";
            enemyHealth = 0f;
            DestroyZergTeam();
            enemyTeamAlive = 0;
        }

        string phase = ResolvePhase(miningWorkers, armyCount);
        WriteScenarioState(phase, miningWorkers, armyCount, enemyHq, enemyHealth, enemyMaxHealth, enemyTeamAlive);
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private bool IsStarCraftFullMapActive()
    {
        var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
        return tags != null && tags.Any(static t => t.Contains("starcraft_full", StringComparison.OrdinalIgnoreCase));
    }

    private void ConsumeAssaultSignals()
    {
        World world = _engine.World;
        world.Query(in TeamAttributeQuery, (ref Team team, ref AttributeBuffer attributes) =>
        {
            if (team.Id != 1 ||
                !attributes.HasAttribute(_assaultSignalAttributeId) ||
                attributes.GetCurrent(_assaultSignalAttributeId) <= 0f)
            {
                return;
            }

            attributes.SetCurrent(_assaultSignalAttributeId, 0f);
            if (!_assaultStarted)
            {
                _assaultStarted = true;
                _lastEvent = "Assault: Terran force is attacking the Zerg Hatchery through GAS volleys.";
            }
        });
    }

    private void PublishAssaultVolley(Entity enemyHq)
    {
        _volleyFrame++;
        if (_volleyFrame < 30)
        {
            return;
        }

        _volleyFrame = 0;
        int published = 0;
        World world = _engine.World;
        world.Query(in CombatQuery, (Entity attacker, ref Team team, ref EntityTemplateKeyRef templateRef) =>
        {
            if (published >= 8 ||
                team.Id != 1 ||
                !world.IsAlive(attacker) ||
                !world.TryGet(attacker, out AttributeBuffer attributes) ||
                ReadAttribute(in attributes, _healthAttributeId) <= 0f)
            {
                return;
            }

            int damageEffectId = ResolveDamageEffectTemplateId(templateRef.TemplateKeyId);
            if (damageEffectId <= 0)
            {
                return;
            }

            _requests.Publish(new EffectRequest
            {
                TemplateId = damageEffectId,
                Source = attacker,
                Target = enemyHq,
                TargetContext = enemyHq,
            });
            published++;
        });
    }

    private int CountMiningWorkers()
    {
        int count = 0;
        World world = _engine.World;
        world.Query(in TeamAttributeQuery, (Entity entity, ref Team team, ref AttributeBuffer attributes) =>
        {
            if (team.Id == 1 &&
                attributes.HasAttribute(_healthAttributeId) &&
                attributes.GetCurrent(_healthAttributeId) > 0f &&
                HasActiveEffect(world, entity, _miningEffectTemplateId))
            {
                count++;
            }
        });
        return count;
    }

    private int CountPlayerArmy()
    {
        int count = 0;
        World world = _engine.World;
        world.Query(in CombatQuery, (Entity entity, ref Team team, ref EntityTemplateKeyRef templateRef) =>
        {
            if (team.Id != 1)
            {
                return;
            }

            if (!world.TryGet(entity, out AttributeBuffer attributes) ||
                ReadAttribute(in attributes, _healthAttributeId) <= 0f)
            {
                return;
            }

            if (ResolveDamageEffectTemplateId(templateRef.TemplateKeyId) > 0)
            {
                count++;
            }
        });

        return count;
    }

    private int CountAliveTeamEntities(int teamId)
    {
        int count = 0;
        _engine.World.Query(in TeamAttributeQuery, (ref Team team, ref AttributeBuffer attributes) =>
        {
            if (team.Id == teamId && ReadAttribute(in attributes, _healthAttributeId) > 0f)
            {
                count++;
            }
        });
        return count;
    }

    private Entity FindEntityByTeamAndName(int teamId, string name)
    {
        Entity result = Entity.Null;
        World world = _engine.World;
        world.Query(in NameQuery, (Entity entity, ref Name entityName) =>
        {
            if (result != Entity.Null ||
                !world.TryGet(entity, out Team team) ||
                team.Id != teamId ||
                !string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result = entity;
        });
        return result;
    }

    private void DestroyZergTeam()
    {
        if (_zergTeamDestroyed)
        {
            return;
        }

        _zergTeamDestroyed = true;
        var toDestroy = new List<Entity>(64);
        _engine.World.Query(in TeamAttributeQuery, (Entity entity, ref Team team, ref AttributeBuffer _) =>
        {
            if (team.Id == 2)
            {
                toDestroy.Add(entity);
            }
        });

        foreach (Entity entity in toDestroy)
        {
            if (_engine.World.IsAlive(entity))
            {
                _engine.World.Destroy(entity);
            }
        }
    }

    private string ResolvePhase(int miningWorkers, int armyCount)
    {
        if (_victory)
        {
            return "Victory";
        }

        if (_assaultStarted)
        {
            return "Assault";
        }

        if (armyCount > 18)
        {
            return "Production";
        }

        return miningWorkers > 0 ? "Mining" : "Opening";
    }

    private void WriteScenarioState(
        string phase,
        int miningWorkers,
        int armyCount,
        Entity enemyHq,
        float enemyHealth,
        float enemyMaxHealth,
        int enemyTeamAlive)
    {
        string enemyName = enemyHq != Entity.Null &&
                           _engine.World.IsAlive(enemyHq) &&
                           _engine.World.TryGet(enemyHq, out Name name)
            ? name.Value
            : "Hatchery";

        _engine.GlobalContext[PhaseKey] = phase;
        _engine.GlobalContext[LastEventKey] = _lastEvent;
        _engine.GlobalContext[MiningWorkersKey] = miningWorkers;
        _engine.GlobalContext[ArmyCountKey] = armyCount;
        _engine.GlobalContext[EnemyHqNameKey] = enemyName;
        _engine.GlobalContext[EnemyHqHealthKey] = MathF.Max(0f, enemyHealth);
        _engine.GlobalContext[EnemyHqMaxHealthKey] = MathF.Max(1f, enemyMaxHealth);
        _engine.GlobalContext[EnemyTeamAliveKey] = enemyTeamAlive;
        _engine.GlobalContext[VictoryKey] = _victory;

        if (miningWorkers > 0 && !_assaultStarted && !_victory)
        {
            _lastEvent = "Mining: SCV is harvesting minerals through periodic GAS ticks.";
        }
    }

    private int ResolveDamageEffectTemplateId(int templateKeyId)
    {
        if (templateKeyId <= 0)
        {
            return 0;
        }

        if (_damageEffectByTemplateKeyId.TryGetValue(templateKeyId, out int cachedEffectTemplateId) &&
            cachedEffectTemplateId > 0)
        {
            return cachedEffectTemplateId;
        }

        int effectTemplateId = 0;
        string templateId = _templateKeys.GetName(templateKeyId);
        if (!string.IsNullOrWhiteSpace(templateId) && templateId.StartsWith("scf_", StringComparison.Ordinal))
        {
            string slug = templateId["scf_".Length..].Replace('_', '.');
            int resolved = EffectTemplateIdRegistry.GetId($"Effect.Scf.Damage.{slug}");
            if (resolved != EffectTemplateIdRegistry.InvalidId)
            {
                effectTemplateId = resolved;
            }
        }

        if (effectTemplateId > 0)
        {
            _damageEffectByTemplateKeyId[templateKeyId] = effectTemplateId;
        }

        return effectTemplateId;
    }

    private float ReadCurrent(Entity entity, int attributeId)
    {
        return entity != Entity.Null &&
               _engine.World.IsAlive(entity) &&
               _engine.World.Has<AttributeBuffer>(entity)
            ? _engine.World.Get<AttributeBuffer>(entity).GetCurrent(attributeId)
            : 0f;
    }

    private float ReadBase(Entity entity, int attributeId)
    {
        return entity != Entity.Null &&
               _engine.World.IsAlive(entity) &&
               _engine.World.Has<AttributeBuffer>(entity)
            ? _engine.World.Get<AttributeBuffer>(entity).GetBase(attributeId)
            : 0f;
    }

    private static float ReadAttribute(in AttributeBuffer attributes, int attributeId)
    {
        return attributeId == AttributeRegistry.InvalidId || !attributes.HasAttribute(attributeId)
            ? 0f
            : attributes.GetCurrent(attributeId);
    }

    private static bool HasActiveEffect(World world, Entity entity, int effectTemplateId)
    {
        if (!world.IsAlive(entity) || !world.Has<ActiveEffectContainer>(entity))
        {
            return false;
        }

        ref ActiveEffectContainer active = ref world.Get<ActiveEffectContainer>(entity);
        for (int i = 0; i < active.Count; i++)
        {
            Entity effectEntity = active.GetEntity(i);
            if (world.IsAlive(effectEntity) &&
                world.Has<EffectTemplateRef>(effectEntity) &&
                world.Get<EffectTemplateRef>(effectEntity).TemplateId == effectTemplateId)
            {
                return true;
            }
        }

        return false;
    }

    private static int EnsureAttribute(string attributeName)
    {
        int id = AttributeRegistry.GetId(attributeName);
        return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(attributeName);
    }
}
