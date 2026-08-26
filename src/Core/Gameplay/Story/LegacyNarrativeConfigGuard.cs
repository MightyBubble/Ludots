using System;
using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// 旧 Narrative 配置入口失败关闭：不允许双轨兼容。
    /// </summary>
    public static class LegacyNarrativeConfigGuard
    {
        public static readonly string[] ForbiddenCatalogPaths =
        {
            "Narrative/variables.json",
            "Narrative/dialogues.json",
            "Narrative/cinematics.json",
        };

        public const string MigrationMessage =
            "Narrative runtime has been retired. Migrate content to Dialogue/dialogues.json, Sequencer/sequences.json, Story/lines.json, Story/presentation_profiles.json, and Graph (Query/TriggerGraph + MapVariable/Blackboard). See docs/architecture/story_runtime_dialogue_sequencer.md.";

        public static void RejectIfPresent(ConfigCatalog? catalog)
        {
            if (catalog == null)
            {
                return;
            }

            for (int i = 0; i < ForbiddenCatalogPaths.Length; i++)
            {
                string path = ForbiddenCatalogPaths[i];
                if (catalog.TryGet(path, out _))
                {
                    throw new InvalidOperationException(
                        $"Config catalog still declares forbidden path '{path}'. {MigrationMessage}");
                }
            }
        }

        public static void RejectLegacyTaskFields(IReadOnlyList<(string TaskId, IReadOnlyDictionary<string, object?> Raw)>? rawTasks)
        {
            // Reserved for loaders that surface raw JSON members; TaskDefinition uses unmapped-member disallow.
        }
    }
}
