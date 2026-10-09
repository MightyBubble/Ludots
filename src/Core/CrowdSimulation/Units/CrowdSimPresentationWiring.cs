using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 会话到呈现/交互基建的接线(宿主在引擎世界跑会话时注入;回放/无头装配注入派生接线——
/// 共享模板数据、全新稳定 id 分配器、空选中属主,不触碰活集合仓)。数据都按 id 预解析,
/// 生成热路径不做字符串解析。
/// </summary>
public sealed class CrowdSimPresentationWiring
{
    /// <summary>呈现稳定 id 分配器(PresentationEntityLifecycleSystem 的投影入口)。</summary>
    public required PresentationStableIdAllocator StableIds { get; init; }
    /// <summary>实体模板注册表(单位模板实例化的数据源;会话激活时已装载 mod 资产)。</summary>
    public required DataRegistry<EntityTemplate> TemplateRegistry { get; init; }
    /// <summary>[unitType][radiusClass] → (模板 id, 模板键 id)。会话激活时按配置的
    /// unitTypes[].templates 声明预解析并做闭包校验(模板存在、profile 的移动类型与半径级一致)——
    /// 映射由模板自身声明 profile,不再按 profile 反查(两兵种共用体型时各拿各的模板键)。</summary>
    public required IReadOnlyList<IReadOnlyList<(string TemplateId, int TemplateKeyId)>> TemplatesByUnitTypeRadius { get; init; }
    /// <summary>单位半径(米)写进单位黑板的键 id(presenter 的 ownerBlackboardFloat 车道读它,如选中环随体型缩放)。</summary>
    public required int RadiusMetersBlackboardKeyId { get; init; }
    /// <summary>选中集合("selected")的属主实体(通常是会话实体);Entity.Null = 不镜像选中(回放/无头)。</summary>
    public required Entity SelectionOwner { get; init; }
    /// <summary>选中集合键 id。</summary>
    public required int SelectedCollectionKeyId { get; init; }
    /// <summary>实体集合仓(presenter 监听集合成员增减来显隐选中环)。</summary>
    public required EntityCollectionStore Collections { get; init; }

    /// <summary>unitTypes[].templates →(兵种 × 半径级)模板表,含闭包校验(模板存在、模板声明
    /// profile 的移动类型与半径级一致、模板键已注册)。引擎装配与无头装配共用同一份口径;
    /// 两者的模板键都经 MapLoader.LoadTemplates 的装载链注册,缺失即装载不一致,直接抛。</summary>
    public static (string TemplateId, int TemplateKeyId)[][] BuildUnitTypeTemplates(
        CrowdSimulationRuntimeConfig config,
        DataRegistry<EntityTemplate> templateRegistry,
        EntityTemplateKeyRegistry templateKeys,
        AgentProfileRegistry agentProfiles,
        string configSource)
    {
        var radiusClasses = CrowdDeployment.DistinctRadiusClasses(config);
        var rows = new (string TemplateId, int TemplateKeyId)[config.UnitTypes.Count][];
        for (int t = 0; t < config.UnitTypes.Count; t++)
        {
            var unitType = config.UnitTypes[t];
            if (unitType.TemplatesByRadiusCm == null || unitType.TemplatesByRadiusCm.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{configSource}: unitTypes.{unitType.Id} 未声明 templates(半径级厘米 → 模板 id);单位生成管线按(兵种 × 半径级)实例化模板。");
            }

            var row = new (string TemplateId, int TemplateKeyId)[radiusClasses.Count];
            for (int r = 0; r < radiusClasses.Count; r++)
            {
                int radiusCm = radiusClasses[r];
                if (!unitType.TemplatesByRadiusCm.TryGetValue(radiusCm, out string? templateId))
                {
                    throw new InvalidOperationException(
                        $"{configSource}: unitTypes.{unitType.Id}.templates 缺半径级 {radiusCm} 的模板声明。");
                }

                var template = templateRegistry.Get(templateId)
                    ?? throw new InvalidOperationException(
                        $"{configSource}: unitTypes.{unitType.Id}.templates[{radiusCm}] 引用未知实体模板 \"{templateId}\"。");
                string? profileId = template.Components.TryGetValue("CrowdSimulationAgent", out var agentNode)
                    ? agentNode?["profileId"]?.GetValue<string>()
                    : null;
                if (string.IsNullOrWhiteSpace(profileId) || !agentProfiles.TryGet(profileId, out var profile))
                {
                    throw new InvalidOperationException(
                        $"Entities/templates.json: 模板 \"{templateId}\" 的 CrowdSimulationAgent 缺有效 profileId(需存在于 Navigation/agent_profiles.json)。");
                }

                int agentTypeLayer = config.AgentTypes[unitType.AgentTypeIndex].Layer;
                if (profile.Layer != agentTypeLayer || (int)profile.RadiusCm != radiusCm)
                {
                    throw new InvalidOperationException(
                        $"{configSource}: unitTypes.{unitType.Id}.templates[{radiusCm}] 的模板 \"{templateId}\" 声明 profile \"{profileId}\"(layer {profile.Layer}, 半径 {profile.RadiusCm}),与本兵种(layer {agentTypeLayer})半径级 {radiusCm} 不一致。");
                }

                int templateKeyId = templateKeys.GetId(templateId);
                if (templateKeyId <= 0)
                {
                    throw new InvalidOperationException(
                        $"CrowdSimulation 单位模板键 '{templateId}' 未注册(应由 CrowdSimulationMod 的 Entities/templates.json 提供)。");
                }

                row[r] = (templateId, templateKeyId);
            }

            rows[t] = row;
        }

        return rows;
    }
}
