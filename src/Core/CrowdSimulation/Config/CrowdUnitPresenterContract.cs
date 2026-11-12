using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 引擎里的群体会话要求绑定的每个单位模板生成时都有表现，否则单位照常跑却看不见。
/// 无头会话（<see cref="Units.CrowdSimPresentationWiring.Spawns"/> 为 null）声明没有呈现消费者，不走这条检查。
/// </summary>
public static class CrowdUnitPresenterContract
{
    public const string TemplatesFile = "Entities/templates.json";

    public static void Require(CrowdSimulationRuntimeConfig config, PresenterSpawnLinks links, ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder? missing = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (RuntimeUnitType unitType in config.UnitTypes)
        {
            if (unitType.TemplatesByRadiusCm == null)
            {
                continue;
            }

            foreach (KeyValuePair<int, string> pair in unitType.TemplatesByRadiusCm.OrderBy(pair => pair.Key))
            {
                string templateId = pair.Value;
                if (!seen.Add(templateId) || links.For(templateId).Count > 0)
                {
                    continue;
                }

                string source = report.TryGetWinner(TemplatesFile, templateId, out string winner) ? winner : "来源没有登记";
                missing ??= new StringBuilder();
                missing.Append('\n')
                    .Append("单位模板 \"").Append(templateId).Append("\"（").Append(source).Append("）生成时没有表现：")
                    .Append(PresenterDefinitionConfigLoader.PresentersFile)
                    .Append(" 里没有 EntitySpawned 键为 \"").Append(templateId).Append("\" 或 \"")
                    .Append(PresenterSpawnLinks.WildcardTemplate)
                    .Append("\" 的 CreatePresenter 规则，也没有表现定义用 bindSpawn.template 指向它。");
            }
        }

        if (missing == null)
        {
            return;
        }

        IReadOnlyList<string> fragments = report.GetFragments(PresenterDefinitionConfigLoader.PresentersFile);
        throw new InvalidOperationException(
            "CrowdSimulation 会话绑定的单位模板缺表现。" + missing
            + "\n" + PresenterDefinitionConfigLoader.PresentersFile + " 合并顺序: "
            + (fragments.Count == 0 ? "（无）" : string.Join(" | ", fragments)));
    }
}
