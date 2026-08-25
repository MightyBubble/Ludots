using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPresenterCommandShowcaseMod;

public sealed class CapabilityStandardPresenterCommandShowcaseModEntry : IMod
{
    public const string MapId = "capability_standard_presenter_command_showcase";
    public const string FlashUnitDefinitionKey = "pcmd.flash_unit";
    public const string LampPostDefinitionKey = "pcmd.lamp_post";
    public const string BoilerDefinitionKey = "pcmd.boiler";
    public const string ChimneySmokeDefinitionKey = "pcmd.chimney_smoke";
    public const string PortalDefinitionKey = "pcmd.portal";
    public const string PortalTargetDefinitionKey = "pcmd.portal_target";
    public const string FieldDirectorDefinitionKey = "pcmd.field_director";
    public const string GroundPadDefinitionKey = "pcmd.ground.pad";

    public const string FlashUnitOwnerStableIdSeed = "pcmd.owner.flash";
    public const int FlashUnit0OwnerStableId = 51201;
    public const int Lamp0OwnerStableId = 51301;
    public const int Lamp1OwnerStableId = 51302;
    public const int Lamp2OwnerStableId = 51303;
    public const int RefreshPillarOwnerStableId = 51304;
    public const int BoilerOwnerStableId = 51401;
    public const int PortalOwnerStableId = 51501;
    public const int FieldDirectorOwnerStableId = 51502;
    public const int SummonOwnerStableIdBase = 51601;

    private static readonly Vector3 FlashPlazaOrigin = new(-9f, 0f, 5f);
    private static readonly Vector3 LampRowOrigin = new(0f, 0f, 5f);
    private static readonly Vector3 BoilerOrigin = new(8f, 0f, 5f);
    private static readonly Vector3 FieldOrigin = new(0f, 0f, -4f);
    private static readonly Vector3[] PortalStops =
    {
        new Vector3(0f, 0f, -4f),
        new Vector3(3f, 0f, -4f),
        new Vector3(-3f, 0f, -4f),
    };
    private static readonly Vector3[] TargetSlots =
    {
        new Vector3(-1.5f, 0f, -6.5f),
        new Vector3(0f, 0f, -6.5f),
        new Vector3(1.5f, 0f, -6.5f),
        new Vector3(3f, 0f, -6.5f),
        new Vector3(-3f, 0f, -6.5f),
        new Vector3(0f, 0f, -8.5f),
    };

    private static readonly string[] ColorEventKeys = { "pcmd.lamp.color.0", "pcmd.lamp.color.1", "pcmd.lamp.color.2" };
    private static readonly string[] ColorNames = { "amber", "cyan", "violet" };
    private static readonly string[] ScaleEventKeys = { "pcmd.lamp.scale.0", "pcmd.lamp.scale.1", "pcmd.lamp.scale.2" };
    private static readonly float[] ScaleValues = { 0.8f, 1.2f, 1.6f };

    private readonly Queue<Entity> _summonedOwners = new();
    private readonly List<Entity> _lampOwners = new();
    private PresenterCommandShowcaseRuntime? _runtime;
    private Entity _flashUnit0Owner = Entity.Null;
    private Entity _boilerOwner = Entity.Null;
    private Entity _portalOwner = Entity.Null;
    private Entity _fieldDirectorOwner = Entity.Null;
    private int _colorPaletteIndex = -1;
    private int _scalePaletteIndex = -1;
    private int _portalStopIndex;
    private int _summonCounter;
    private bool _boilerWorking;
    private bool _portalInitialResyncPublished;

    public void OnLoad(IModContext context)
    {
        RegisterEventKeys();
        var runtime = new PresenterCommandShowcaseRuntime(MapId, "Presenter command showcase ready.");
        _runtime = runtime;

        string[] buttonIds =
        {
            "pcmd-btn-hit", "pcmd-btn-suppress", "pcmd-btn-color", "pcmd-btn-scale",
            "pcmd-btn-refresh", "pcmd-btn-boiler", "pcmd-btn-summon", "pcmd-btn-remove",
            "pcmd-btn-clear", "pcmd-btn-vanish", "pcmd-btn-portal",
        };
        string[] buttonLabels =
        {
            "A·受击闪烁", "A·压制复原", "B·循环灯色", "B·循环缩放",
            "B·强制刷新对照柱", "C·烟囱开关", "D·召唤靶标", "D·精确拆除",
            "D·整域清场", "D·路由销毁", "D·传送门",
        };
        string[] proofLines =
        {
            "A 闪烁广场: SetParam/TimerSet/TimerExpired/TimerKill",
            "B 灯柱参数 sink: SetParam vec4/float + SinkParamToAsset",
            "C 烟囱开关: ActivateBehavior/DeactivateBehavior",
            "D 传送与清场: Create/Destroy/DestroyScoped/DestroyScope/InitializeTransform",
        };

        runtime.RegisterAction("pcmd-btn-hit", (r, e) => PublishHit(r, e));
        runtime.RegisterAction("pcmd-btn-suppress", (r, e) => PublishSuppress(r, e));
        runtime.RegisterAction("pcmd-btn-color", (r, e) => PublishColorCycle(r, e));
        runtime.RegisterAction("pcmd-btn-scale", (r, e) => PublishScaleCycle(r, e));
        runtime.RegisterAction("pcmd-btn-refresh", (r, e) => PublishRefresh(r, e));
        runtime.RegisterAction("pcmd-btn-boiler", (r, e) => ToggleBoiler(r, e));
        runtime.RegisterAction("pcmd-btn-summon", (r, e) => SummonTarget(r, e));
        runtime.RegisterAction("pcmd-btn-remove", (r, e) => RemoveOldestTarget(r, e));
        runtime.RegisterAction("pcmd-btn-clear", (r, e) => ClearField(r, e));
        runtime.RegisterAction("pcmd-btn-vanish", (r, e) => VanishNewestTarget(r, e));
        runtime.RegisterAction("pcmd-btn-portal", (r, e) => MovePortal(r, e));

        runtime.Activated += ActivateShowcase;
        runtime.Ticked += UpdateMetrics;

        var panel = new PresenterCommandShowcasePanelController(
            runtime,
            "Presenter Command 全息",
            buttonIds,
            buttonLabels,
            proofLines);
        var stationOrigins = new Vector2[]
        {
            new(FlashPlazaOrigin.X, FlashPlazaOrigin.Z),
            new(LampRowOrigin.X, LampRowOrigin.Z),
            new(BoilerOrigin.X, BoilerOrigin.Z),
            new(FieldOrigin.X, FieldOrigin.Z),
        };
        PresenterCommandShowcaseInstall.Install(context, runtime, panel, stationOrigins, nameof(CapabilityStandardPresenterCommandShowcaseMod));
    }

    public void OnUnload()
    {
    }

    private static void RegisterEventKeys()
    {
        TagRegistry.Register("pcmd.hit");
        TagRegistry.Register("pcmd.suppressed");
        TagRegistry.Register("pcmd.working");
        TagRegistry.Register("pcmd.lamp.refresh");
        TagRegistry.Register("pcmd.summon");
        TagRegistry.Register("pcmd.remove.scoped");
        TagRegistry.Register("pcmd.clear.field");
        TagRegistry.Register("pcmd.vanish");
        TagRegistry.Register("pcmd.portal.resync");
        foreach (string key in ColorEventKeys)
        {
            TagRegistry.Register(key);
        }

        foreach (string key in ScaleEventKeys)
        {
            TagRegistry.Register(key);
        }
    }

    private void ActivateShowcase(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        _colorPaletteIndex = -1;
        _scalePaletteIndex = -1;
        _portalStopIndex = 0;
        _summonCounter = 0;
        _boilerWorking = false;
        _portalInitialResyncPublished = false;
        _summonedOwners.Clear();
        _lampOwners.Clear();

        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Presenter command showcase requires PresenterDefinitionRegistry.");
        VerifyDefinitions(definitions);

        int groundPadDefId = definitions.GetId(GroundPadDefinitionKey);
        int flashUnitDefId = definitions.GetId(FlashUnitDefinitionKey);
        int lampDefId = definitions.GetId(LampPostDefinitionKey);
        int boilerDefId = definitions.GetId(BoilerDefinitionKey);
        int portalDefId = definitions.GetId(PortalDefinitionKey);

        CreateGroundPad(engine, groundPadDefId, FlashPlazaOrigin, 51211);
        CreateGroundPad(engine, groundPadDefId, LampRowOrigin, 51212);
        CreateGroundPad(engine, groundPadDefId, BoilerOrigin, 51213);
        CreateGroundPad(engine, groundPadDefId, FieldOrigin, 51214);

        for (int i = 0; i < 5; i++)
        {
            Entity owner = CreateOwner(engine, FlashUnit0OwnerStableId + i, FlashPlazaOrigin + new Vector3(-1.6f + (i * 0.8f), 0f, 0.8f));
            if (i == 0)
            {
                _flashUnit0Owner = owner;
            }

            EnqueuePresenterCreate(engine, flashUnitDefId, "pcmd.unit.flash", owner);
        }

        int[] lampStableIds = { Lamp0OwnerStableId, Lamp1OwnerStableId, Lamp2OwnerStableId };
        for (int i = 0; i < lampStableIds.Length; i++)
        {
            Entity owner = CreateOwner(engine, lampStableIds[i], LampRowOrigin + new Vector3(-1.2f + (i * 1.2f), 0f, 0.8f));
            _lampOwners.Add(owner);
            EnqueuePresenterCreate(engine, lampDefId, $"pcmd.lamp.pillar.{i}", owner);
        }

        Entity refreshPillarOwner = CreateOwner(engine, RefreshPillarOwnerStableId, LampRowOrigin + new Vector3(2.6f, 0f, 0.8f));
        EnqueuePresenterCreate(engine, lampDefId, "pcmd.lamp.pillar.refresh", refreshPillarOwner);

        _boilerOwner = CreateOwner(engine, BoilerOwnerStableId, BoilerOrigin);
        EnqueuePresenterCreate(engine, boilerDefId, "pcmd.boiler.root", _boilerOwner);

        _portalOwner = CreateOwner(engine, PortalOwnerStableId, PortalStops[0]);
        EnqueuePresenterCreate(engine, portalDefId, "pcmd.portal.root", _portalOwner);

        _fieldDirectorOwner = CreateOwner(engine, FieldDirectorOwnerStableId, FieldOrigin);

        runtime.SetMetricA("Flash", "base");
        runtime.SetMetricB("Targets", "0");
        runtime.SetLastEvent("四个站点已就绪：受击、参数 sink、行为开关、传送与清场。");
    }

    private static void VerifyDefinitions(PresenterDefinitionRegistry definitions)
    {
        string[] requiredKeys =
        {
            FlashUnitDefinitionKey, LampPostDefinitionKey, BoilerDefinitionKey, ChimneySmokeDefinitionKey,
            PortalDefinitionKey, PortalTargetDefinitionKey, FieldDirectorDefinitionKey, GroundPadDefinitionKey,
        };
        foreach (string key in requiredKeys)
        {
            if (definitions.GetId(key) <= 0)
            {
                throw new InvalidOperationException($"Presenter definition '{key}' is not registered.");
            }
        }

        int lampDefId = definitions.GetId(LampPostDefinitionKey);
        if (!definitions.TryGet(lampDefId, out PresenterDefinition lampDefinition))
        {
            throw new InvalidOperationException($"Presenter definition '{LampPostDefinitionKey}' is not resolvable.");
        }

        bool hasColorSinkKey = false;
        for (int i = 0; i < lampDefinition.Behaviors.Length; i++)
        {
            if (lampDefinition.Behaviors[i].AssetBinding.ColorParamKey > 0)
            {
                hasColorSinkKey = true;
                break;
            }
        }

        if (!hasColorSinkKey)
        {
            throw new InvalidOperationException($"Presenter definition '{LampPostDefinitionKey}' must declare an AssetBinding color sink key for the programmatic SinkParamToAsset demo.");
        }
    }

    private static Entity CreateOwner(GameEngine engine, int stableId, Vector3 position)
    {
        return engine.World.Create(
            new VisualTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = stableId });
    }

    private static void CreateGroundPad(GameEngine engine, int defId, Vector3 position, int stableId)
    {
        Entity owner = CreateOwner(engine, stableId, position);
        EnqueuePresenterCreate(engine, defId, "pcmd.ground.pad", owner);
    }

    private static void EnqueuePresenterCreate(GameEngine engine, int definitionId, string scopeTagName, Entity source)
    {
        if (engine.GetService(CoreServiceKeys.PresenterCommandBuffer) is not PresenterCommandBuffer commands)
        {
            throw new InvalidOperationException("Presenter command showcase requires PresenterCommandBuffer.");
        }

        if (!commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.CreatePresenter,
            CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
            RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
            PresenterDefinitionId = definitionId,
            ScopeTag = PresenterScopeTagRegistry.Register(scopeTagName),
            ScopeSource = PresenterCommandScopeSource.Fixed,
            AnchorKind = PresentationAnchorKind.Entity,
            Source = source,
        }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the presenter command showcase hierarchy.");
        }
    }

    private void PublishHit(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        PublishGameplayEvent(engine, "pcmd.hit", _flashUnit0Owner);
        runtime.SetLastEvent("A 站受击：SetParam 变黄 + TimerSet 0.6s。");
    }

    private void PublishSuppress(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        PublishTagEvent(engine, "pcmd.suppressed", _flashUnit0Owner, gained: true);
        runtime.SetLastEvent("A 站压制：TimerKill \"*\" 立即复原，无 TimerExpired。");
    }

    private void PublishColorCycle(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        _colorPaletteIndex = (_colorPaletteIndex + 1) % ColorEventKeys.Length;
        foreach (Entity owner in _lampOwners)
        {
            PublishGameplayEvent(engine, ColorEventKeys[_colorPaletteIndex], owner);
        }

        runtime.SetMetricA("Lamp color", ColorNames[_colorPaletteIndex]);
        runtime.SetLastEvent($"B 站 SetParam vec4：三根灯柱循环到 {ColorNames[_colorPaletteIndex]}。");
    }

    private void PublishScaleCycle(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        _scalePaletteIndex = (_scalePaletteIndex + 1) % ScaleEventKeys.Length;
        foreach (Entity owner in _lampOwners)
        {
            PublishGameplayEvent(engine, ScaleEventKeys[_scalePaletteIndex], owner);
        }

        runtime.SetMetricA("Lamp scale", ScaleValues[_scalePaletteIndex].ToString("0.0"));
        runtime.SetLastEvent($"B 站 SetParam float：灯柱缩放倍率 {ScaleValues[_scalePaletteIndex]:0.0}。");
    }

    private void PublishRefresh(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        Entity refreshPillarOwner = FindOwnerByStableId(engine, RefreshPillarOwnerStableId)
            ?? throw new InvalidOperationException("Presenter command showcase lost the refresh pillar owner.");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Presenter definition registry missing.");
        if (engine.GetService(CoreServiceKeys.PresenterCommandBuffer) is not PresenterCommandBuffer commands)
        {
            throw new InvalidOperationException("Presenter command showcase requires PresenterCommandBuffer.");
        }

        if (!commands.TryAdd(new PresenterCommand
        {
            CommandKind = PresenterCommandKind.SinkParamToAsset,
            CommandKindId = (byte)PresenterCommandKind.SinkParamToAsset,
            RouteStrategy = PresenterCommandRouteStrategy.SingleRuntime,
            PresenterDefinitionId = definitions.GetId(LampPostDefinitionKey),
            ScopeTag = PresenterScopeTagRegistry.Register("pcmd.lamp.pillar.refresh"),
            AnchorKind = PresentationAnchorKind.Entity,
            Source = refreshPillarOwner,
            ParamKey = PresenterParamKeyRegistry.Register("pcmd.lamp.color"),
            ParamLane = ParamLane.Vector,
            // 灯柱定义唯一行为槽 body 即索引 0；槽注册表不对外，程序化路径按定义槽序寻址
            TargetBehaviorSlot = 0,
        }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while sinking refresh pillar params.");
        }

        runtime.SetLastEvent("B 站 SinkParamToAsset：对照柱槽位同步重写入（程序化直发，scoped 解析）。");
    }

    private void ToggleBoiler(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        _boilerWorking = !_boilerWorking;
        PublishTagEvent(engine, "pcmd.working", _boilerOwner, _boilerWorking);
        runtime.SetMetricA("Chimney", _boilerWorking ? "on" : "off");
        runtime.SetLastEvent(_boilerWorking
            ? "C 站 ActivateBehavior：烟囱 VFX slot 开启。"
            : "C 站 DeactivateBehavior：烟囱 VFX slot 关闭。");
    }

    private void SummonTarget(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        Vector3 slot = TargetSlots[_summonCounter % TargetSlots.Length];
        Entity owner = CreateOwner(engine, SummonOwnerStableIdBase + _summonCounter, slot);
        _summonCounter++;
        _summonedOwners.Enqueue(owner);

        PublishGameplayEvent(engine, "pcmd.summon", owner, slot);
        runtime.SetMetricB("Targets", _summonedOwners.Count.ToString());
        runtime.SetLastEvent("D 站 CreatePresenter：召唤 scoped 靶标。");
    }

    private void RemoveOldestTarget(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        if (!TryDequeueTarget(engine, out Entity owner))
        {
            runtime.SetLastEvent("D 站没有可拆除的靶标。");
            return;
        }

        PublishGameplayEvent(engine, "pcmd.remove.scoped", owner);
        runtime.SetMetricB("Targets", _summonedOwners.Count.ToString());
        runtime.SetLastEvent("D 站 DestroyScopedPresenter：按 definition+owner+scope 精确拆除。");
    }

    private void ClearField(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        if (_summonedOwners.Count == 0)
        {
            runtime.SetLastEvent("D 站靶标域已是空的。");
            return;
        }

        _summonedOwners.Clear();
        PublishGameplayEvent(engine, "pcmd.clear.field", _fieldDirectorOwner);
        runtime.SetMetricB("Targets", "0");
        runtime.SetLastEvent("D 站 DestroyPresenterScope：整域清场。");
    }

    private void VanishNewestTarget(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        if (!TryDequeueNewest(engine, out Entity owner))
        {
            runtime.SetLastEvent("D 站没有可路由销毁的靶标。");
            return;
        }

        PublishGameplayEvent(engine, "pcmd.vanish", owner);
        runtime.SetMetricB("Targets", _summonedOwners.Count.ToString());
        runtime.SetLastEvent("D 站 DestroyPresenter：ExistingInstances 单体路由销毁。");
    }

    private void MovePortal(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        _portalStopIndex = (_portalStopIndex + 1) % PortalStops.Length;
        Entity portalOwner = FindOwnerByStableId(engine, PortalOwnerStableId)
            ?? throw new InvalidOperationException("Presenter command showcase lost the portal owner.");
        if (engine.World.IsAlive(portalOwner) && engine.World.Has<VisualTransform>(portalOwner))
        {
            engine.World.Get<VisualTransform>(portalOwner).Position = PortalStops[_portalStopIndex];
        }

        PublishGameplayEvent(engine, "pcmd.portal.resync", portalOwner);
        runtime.SetMetricA("Portal", $"stop {_portalStopIndex + 1}");
        runtime.SetLastEvent("D 站 InitializeTransform：改 owner 变换后重同步传送门。");
    }

    private void UpdateMetrics(PresenterCommandShowcaseRuntime runtime, GameEngine engine)
    {
        if (!_portalInitialResyncPublished)
        {
            // Static-mobility portal 不跟随每帧 transform tick；首帧实例就绪后用 InitializeTransform 应用 anchor 偏移。
            Entity? portalOwner = FindOwnerByStableId(engine, PortalOwnerStableId);
            if (portalOwner.HasValue)
            {
                _portalInitialResyncPublished = true;
                PublishGameplayEvent(engine, "pcmd.portal.resync", portalOwner.Value);
            }
        }

        runtime.SetMetricB("Targets", _summonedOwners.Count.ToString());
    }

    private bool TryDequeueTarget(GameEngine engine, out Entity owner)
    {
        while (_summonedOwners.Count > 0)
        {
            Entity candidate = _summonedOwners.Dequeue();
            if (engine.World.IsAlive(candidate))
            {
                owner = candidate;
                return true;
            }
        }

        owner = Entity.Null;
        return false;
    }

    private bool TryDequeueNewest(GameEngine engine, out Entity owner)
    {
        while (_summonedOwners.Count > 0)
        {
            Entity newest = DequeueAt(engine, index: _summonedOwners.Count - 1);
            if (newest != Entity.Null)
            {
                owner = newest;
                return true;
            }
        }

        owner = Entity.Null;
        return false;
    }

    private Entity DequeueAt(GameEngine engine, int index)
    {
        var items = new Entity[_summonedOwners.Count];
        _summonedOwners.CopyTo(items, 0);
        Entity candidate = items[index];
        var rebuilt = new Queue<Entity>();
        for (int i = 0; i < items.Length; i++)
        {
            if (i != index)
            {
                rebuilt.Enqueue(items[i]);
            }
        }

        _summonedOwners.Clear();
        foreach (Entity item in rebuilt)
        {
            _summonedOwners.Enqueue(item);
        }

        return engine.World.IsAlive(candidate) ? candidate : Entity.Null;
    }

    private static Entity? FindOwnerByStableId(GameEngine engine, int stableId)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<PresentationStableId>();
        engine.World.Query(in query, (Entity entity, ref PresentationStableId id) =>
        {
            if (id.Value == stableId)
            {
                found = entity;
            }
        });

        return found == Entity.Null ? null : found;
    }

    private static void PublishGameplayEvent(GameEngine engine, string key, Entity source, Vector3? position = null)
    {
        PublishEvent(engine, new PresentationEvent
        {
            Kind = PresentationEventKind.GameplayEvent,
            KeyId = TagRegistry.Register(key),
            Source = source,
            Target = source,
            Position = position ?? Vector3.Zero,
        });
    }

    private static void PublishTagEvent(GameEngine engine, string key, Entity source, bool gained)
    {
        PublishEvent(engine, new PresentationEvent
        {
            Kind = PresentationEventKind.TagEffectiveChanged,
            KeyId = TagRegistry.Register(key),
            Source = source,
            Target = source,
            Magnitude = gained ? 1f : 0f,
        });
    }

    private static void PublishEvent(GameEngine engine, in PresentationEvent evt)
    {
        if (engine.GetService(CoreServiceKeys.PresentationEventStream) is not PresentationEventStream events)
        {
            throw new InvalidOperationException("Presenter command showcase requires PresentationEventStream.");
        }

        if (!events.TryAdd(in evt))
        {
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the presenter command showcase event.");
        }
    }
}
