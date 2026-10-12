using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Ludots.Core.Config
{
    public readonly record struct ConfigModMount(string Id, string RootPath);

    public sealed class ConfigLoadException : Exception
    {
        public ConfigLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class ConfigProvenanceIdWinner
    {
        public required string Id { get; init; }
        public required string SourceUri { get; init; }
    }

    public sealed class ConfigProvenanceDeletion
    {
        public required string Id { get; init; }
        public required string SourceUri { get; init; }
    }

    public sealed class ConfigProvenanceDuplicate
    {
        public required string Id { get; init; }
        public required string FirstSource { get; init; }
        public required string SecondSource { get; init; }
    }

    public sealed class ConfigProvenanceComponentChain
    {
        public required string TemplateId { get; init; }
        public required string Component { get; init; }
        public required string WriterChain { get; init; }
    }

    public sealed class ConfigProvenanceFile
    {
        public required string RelativePath { get; init; }
        public required IReadOnlyList<string> Fragments { get; init; }
        public string? FileWinner { get; init; }
        public string? FileWinnerKind { get; init; }
        public required IReadOnlyList<ConfigProvenanceIdWinner> Winners { get; init; }
        public required IReadOnlyList<ConfigProvenanceDeletion> Deletions { get; init; }
        public required IReadOnlyList<ConfigProvenanceDuplicate> Duplicates { get; init; }
        public required IReadOnlyList<ConfigProvenanceComponentChain> ComponentOverrideChains { get; init; }
    }

    public sealed class ConfigProvenanceSnapshot
    {
        public required IReadOnlyList<ConfigProvenanceFile> Files { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// 严格装载失败时，把这一份相对路径的合并来源附在新异常上。
    /// 装载器内部抓住的异常保持原样；只有逃出 Wrap 的 JsonException / InvalidOperationException 才换成 ConfigLoadException。
    /// </summary>
    public static class ConfigMergeProvenance
    {
        public const string TemplatesRelativePath = "Entities/templates.json";
        private const string ProvenanceMark = "\n配置路径: ";

        public static T Wrap<T>(string relativePath, ConfigConflictReport? report, Func<ConfigConflictReport, T> load)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("Relative path is required.", nameof(relativePath));
            }

            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }

            report ??= new ConfigConflictReport();
            try
            {
                return load(report);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new ConfigLoadException(ex.Message + Format(relativePath, report), ex);
            }
        }

        public static ConfigProvenanceSnapshot Capture(string coreAssetsRoot, IReadOnlyList<ConfigModMount> modsInLoadOrder)
        {
            if (string.IsNullOrWhiteSpace(coreAssetsRoot))
            {
                throw new ArgumentException("Core assets root is required.", nameof(coreAssetsRoot));
            }

            if (modsInLoadOrder == null)
            {
                throw new ArgumentNullException(nameof(modsInLoadOrder));
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreAssetsRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            for (int i = 0; i < modsInLoadOrder.Count; i++)
            {
                ConfigModMount mount = modsInLoadOrder[i];
                if (string.IsNullOrWhiteSpace(mount.Id))
                {
                    throw new InvalidOperationException($"Config mod mount {i} has an empty id.");
                }

                vfs.Mount(mount.Id, mount.RootPath);
                modLoader.LoadedModIds.Add(mount.Id);
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            return Capture(pipeline, catalog);
        }

        public static ConfigProvenanceSnapshot Capture(ConfigPipeline pipeline, ConfigCatalog catalog)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var report = new ConfigConflictReport();
            foreach (ConfigCatalogEntry entry in catalog.Entries)
            {
                MergeEntry(pipeline, in entry, report);
            }

            string? error = null;
            if (catalog.TryGet(TemplatesRelativePath, out _))
            {
                try
                {
                    var registry = new DataRegistry<EntityTemplate>(pipeline);
                    registry.Load(TemplatesRelativePath, catalog, report: null);
                    var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
                    foreach (EntityTemplate template in registry.GetAll())
                    {
                        if (template == null || string.IsNullOrEmpty(template.Id))
                        {
                            continue;
                        }

                        byId[template.Id] = template;
                    }

                    EntityTemplateInheritance.ExpandAll(byId, report);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            return ToSnapshot(report, error);
        }

        public static ConfigProvenanceSnapshot ToSnapshot(ConfigConflictReport report, string? error = null)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            IReadOnlyList<(string TemplateId, string Component, string WriterChain)> chains = report.GetAllComponentOverrideChains();
            IReadOnlyList<string> paths = report.GetRelativePaths();
            var files = new List<ConfigProvenanceFile>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                IReadOnlyList<string> fragments = report.GetFragments(path);
                var fragmentCopy = new List<string>(fragments.Count);
                for (int f = 0; f < fragments.Count; f++)
                {
                    fragmentCopy.Add(fragments[f]);
                }

                IReadOnlyList<(string Id, string SourceUri)> winners = report.GetWinners(path);
                var winnerDtos = new List<ConfigProvenanceIdWinner>(winners.Count);
                for (int w = 0; w < winners.Count; w++)
                {
                    winnerDtos.Add(new ConfigProvenanceIdWinner
                    {
                        Id = winners[w].Id,
                        SourceUri = winners[w].SourceUri,
                    });
                }

                IReadOnlyList<(string Path, string Id, string SourceUri)> deletions = report.GetDeletions(path);
                var deletionDtos = new List<ConfigProvenanceDeletion>(deletions.Count);
                for (int d = 0; d < deletions.Count; d++)
                {
                    deletionDtos.Add(new ConfigProvenanceDeletion
                    {
                        Id = deletions[d].Id,
                        SourceUri = deletions[d].SourceUri,
                    });
                }

                IReadOnlyList<(string Id, string FirstSource, string SecondSource)> duplicates = report.GetDuplicateIds(path);
                var duplicateDtos = new List<ConfigProvenanceDuplicate>(duplicates.Count);
                for (int d = 0; d < duplicates.Count; d++)
                {
                    duplicateDtos.Add(new ConfigProvenanceDuplicate
                    {
                        Id = duplicates[d].Id,
                        FirstSource = duplicates[d].FirstSource,
                        SecondSource = duplicates[d].SecondSource,
                    });
                }

                var chainDtos = new List<ConfigProvenanceComponentChain>();
                if (string.Equals(path, TemplatesRelativePath, StringComparison.Ordinal))
                {
                    for (int c = 0; c < chains.Count; c++)
                    {
                        chainDtos.Add(new ConfigProvenanceComponentChain
                        {
                            TemplateId = chains[c].TemplateId,
                            Component = chains[c].Component,
                            WriterChain = chains[c].WriterChain,
                        });
                    }
                }

                string? fileWinner = null;
                string? fileWinnerKind = null;
                if (report.TryGetFileWinner(path, out string winnerUri, out string winnerKind))
                {
                    fileWinner = winnerUri;
                    fileWinnerKind = winnerKind;
                }

                files.Add(new ConfigProvenanceFile
                {
                    RelativePath = path,
                    Fragments = fragmentCopy,
                    FileWinner = fileWinner,
                    FileWinnerKind = fileWinnerKind,
                    Winners = winnerDtos,
                    Deletions = deletionDtos,
                    Duplicates = duplicateDtos,
                    ComponentOverrideChains = chainDtos,
                });
            }

            return new ConfigProvenanceSnapshot { Files = files, Error = error };
        }

        private static void MergeEntry(ConfigPipeline pipeline, in ConfigCatalogEntry entry, ConfigConflictReport report)
        {
            switch (entry.MergePolicy)
            {
                case ConfigMergePolicy.DeepObject:
                    pipeline.MergeDeepObjectFromCatalog(in entry, report);
                    return;
                case ConfigMergePolicy.ArrayById:
                    pipeline.MergeArrayByIdFromCatalog(in entry, report);
                    return;
                default:
                    pipeline.MergeFromCatalog(in entry, report);
                    return;
            }
        }

        private static string Format(string relativePath, ConfigConflictReport report)
        {
            var text = new StringBuilder();
            text.Append(ProvenanceMark);
            text.Append(relativePath);
            text.Append("\n合并顺序: ");
            IReadOnlyList<string> fragments = report.GetFragments(relativePath);
            if (fragments.Count == 0)
            {
                text.Append("（无）");
            }
            else
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(" | ");
                    }

                    text.Append(fragments[i]);
                }
            }

            text.Append("\n胜出: ");
            if (report.TryGetFileWinner(relativePath, out string fileWinner, out _))
            {
                text.Append(fileWinner);
            }
            else
            {
                IReadOnlyList<(string Id, string SourceUri)> winners = report.GetWinners(relativePath);
                if (winners.Count == 0)
                {
                    text.Append("（无）");
                }
                else
                {
                    for (int i = 0; i < winners.Count; i++)
                    {
                        if (i > 0)
                        {
                            text.Append("; ");
                        }

                        text.Append(winners[i].Id);
                        text.Append('=');
                        text.Append(winners[i].SourceUri);
                    }
                }
            }

            return text.ToString();
        }
    }
}
