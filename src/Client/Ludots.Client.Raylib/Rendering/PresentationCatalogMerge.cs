using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Core 侧目录合并胶水：渲染器自身只消费 MergedConfigEntry，
    /// ConfigCatalog/ConfigPipeline 的解析集中在此宿主入口。
    /// </summary>
    public static class PresentationCatalogMerge
    {
        public static IReadOnlyList<MergedConfigEntry> MergeEntries(
            ConfigCatalog? catalog,
            ConfigPipeline configs,
            ConfigConflictReport? report,
            string relativePath)
        {
            if (configs == null)
            {
                throw new ArgumentNullException(nameof(configs));
            }

            if (catalog != null && catalog.TryGet(relativePath, out ConfigCatalogEntry found))
            {
                return report == null
                    ? configs.MergeArrayByIdFromCatalog(in found)
                    : configs.MergeArrayByIdFromCatalog(in found, report);
            }

            ConfigCatalogEntry fallback = new(relativePath, ConfigMergePolicy.ArrayById, "id");
            return report == null
                ? configs.MergeArrayByIdFromCatalog(in fallback)
                : configs.MergeArrayByIdFromCatalog(in fallback, report);
        }
    }
}
