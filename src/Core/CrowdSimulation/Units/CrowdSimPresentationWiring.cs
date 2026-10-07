using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Presentation;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>
/// 会话到呈现/交互基建的可选接线(宿主在引擎世界跑会话时注入;无头对拍与回放传 null,
/// 此时单位不挂任何呈现组件,行为与校验码逐位不变)。数据都按 id 预解析,
/// 生成热路径不做字符串解析。
/// </summary>
public sealed class CrowdSimPresentationWiring
{
    /// <summary>呈现稳定 id 分配器(PresentationEntityLifecycleSystem 的投影入口)。</summary>
    public required PresentationStableIdAllocator StableIds { get; init; }
    /// <summary>代理体型 id(如 foot_r100)→ 实体模板键 id(0/缺失 = 该体型不投影)。</summary>
    public required IReadOnlyDictionary<string, int> TemplateKeyByProfileId { get; init; }
    /// <summary>单位半径(米)写进单位黑板的键 id(presenter 的 ownerBlackboardFloat 车道读它,如选中环随体型缩放)。</summary>
    public required int RadiusMetersBlackboardKeyId { get; init; }
    /// <summary>选中集合("selected")的属主实体(通常是会话实体)。</summary>
    public required Entity SelectionOwner { get; init; }
    /// <summary>选中集合键 id。</summary>
    public required int SelectedCollectionKeyId { get; init; }
    /// <summary>实体集合仓(presenter 监听集合成员增减来显隐选中环)。</summary>
    public required EntityCollectionStore Collections { get; init; }
}
