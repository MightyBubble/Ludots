using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace HudCssStylingMod;

public sealed class HudCssStylingModEntry : IMod
{
    public static volatile int DiagHealthId = -1;
    public static volatile int DiagQueued;

    private const string ShowcaseMapId = "hud_css_styling_map";
    private const string UnitTemplateId = "hud_css_styling_unit";
    private const int LocalPlayerId = 1;
    private const int UnitCount = 16;
    private const float GridStepCm = 900f;
    private const float GridOriginCm = 2000f;

    // 16 个单位各自的当前血量:展示不同填充比例,验收断言按此对账。
    private static readonly int[] UnitHealth = { 95, 72, 48, 12, 88, 33, 66, 51, 20, 77, 90, 41, 59, 8, 84, 64 };

    public void OnLoad(IModContext context)
    {
        context.Log("[HudCssStylingMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is GameEngine engine)
            {
                engine.RegisterSystem(new HudCssStylingKnowledgeProjectionSystem(engine), SystemGroup.ClearPresentationFlags);
            }

            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            if (ctx.GetEngine() is GameEngine engine &&
                engine.CurrentMapSession?.MapId.Value == ShowcaseMapId)
            {
                EnqueueUnitGrid(engine);
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }

    /// <summary>
    /// 单位走运行时批量生成通道(与散布基准同路径):地图实体批建通道对
    /// retained HUD presenter 的首帧置脏尚未接通,见 PR 说明的引擎缺口记录。
    /// </summary>
    private static void EnqueueUnitGrid(GameEngine engine)
    {
        var queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("HudCssStyling showcase requires RuntimeEntitySpawnQueue.");
        var mapId = engine.CurrentMapSession!.MapId;
        var requests = new RuntimeEntitySpawnRequest[UnitCount];
        for (int i = 0; i < UnitCount; i++)
        {
            float x = GridOriginCm + (i % 4) * GridStepCm;
            float y = GridOriginCm + (i / 4) * GridStepCm;
            requests[i] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = UnitTemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromFloat(x, y),
                HasWorldPosition = 1,
                HasFacing = 1,
                FacingAngleRad = 0f,
                ComponentPatches = BuildHealthPatch(UnitHealth[i]),
            };
        }

        DiagQueued += queue.EnqueueMany(requests);
    }

    private static RuntimeEntitySpawnComponentPatch[] BuildHealthPatch(int currentHealth)
    {
        return new[]
        {
            new RuntimeEntitySpawnComponentPatch(
                "AttributeBuffer",
                new System.Text.Json.Nodes.JsonObject
                {
                    ["base"] = new System.Text.Json.Nodes.JsonObject { ["Health"] = System.Text.Json.Nodes.JsonValue.Create(100f) },
                    ["current"] = new System.Text.Json.Nodes.JsonObject { ["Health"] = System.Text.Json.Nodes.JsonValue.Create((float)currentHealth) },
                }),
        };
    }

    /// <summary>
    /// HUD 显示属性值必须走知识披露(迷雾合同):每帧把当前图上单位的
    /// LiveVisible + Health 属性掩码披露给唯一本地观察者,血条/数字才被放行。
    /// 与铁匠铺/海量导航 showcase 的同形系统一致。
    /// </summary>
    private sealed class HudCssStylingKnowledgeProjectionSystem : ISystem<float>
    {
        private static readonly QueryDescription KnowledgeTargetQuery = new QueryDescription()
            .WithAll<MapEntity, AttributeBuffer>();

        private readonly GameEngine _engine;
        private Entity _viewer;
        private int _healthAttributeId;

        public HudCssStylingKnowledgeProjectionSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize()
        {
            _healthAttributeId = AttributeRegistry.GetId("Health");
        }

        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.CurrentMapSession?.MapId.Value != ShowcaseMapId)
            {
                return;
            }

            var knowledge = _engine.GetService(CoreServiceKeys.KnowledgeProjectionStore);
            if (knowledge == null || _healthAttributeId < 0)
            {
                return;
            }

            Entity viewer = ResolveViewer();
            var healthMask = KnowledgeIdMask256.Empty.WithId(_healthAttributeId);
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
            var mapId = _engine.CurrentMapSession.MapId;

            _engine.World.Query(in KnowledgeTargetQuery, (Entity target, ref MapEntity mapEntity, ref AttributeBuffer attributes) =>
            {
                if (mapEntity.MapId != mapId || !attributes.HasAttribute(_healthAttributeId))
                {
                    return;
                }

                knowledge.Upsert(viewer, target, new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    in healthMask,
                    KnowledgeIdMask256.Empty,
                    KnowledgeIdMask256.Empty,
                    viewer,
                    observedTick,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 0));
            });
        }

        private Entity ResolveViewer()
        {
            if (ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out Entity possessed) &&
                _engine.World.IsAlive(possessed))
            {
                return possessed;
            }

            if (_viewer != Entity.Null && _engine.World.IsAlive(_viewer))
            {
                return _viewer;
            }

            _viewer = _engine.World.Create(
                new Ludots.Core.Gameplay.Components.PlayerIdentity { PlayerId = LocalPlayerId });
            ClientLocalSeatBindings.BindSoleSeat(_engine, _viewer, LocalPlayerId, "seat.0");
            return _viewer;
        }
    }
}
