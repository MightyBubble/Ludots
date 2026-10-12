using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Ludots.Core.Config
{
    public readonly record struct ConfigModMount(string Id, string RootPath);

    public sealed class ConfigProvenanceIdWinner
    {
        public required string Id { get; init; }
        public required string SourceUri { get; init; }
    }

    public sealed class ConfigProvenanceDeletion
    {
        public required string Id { get; init; }
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
        public required IReadOnlyList<ConfigProvenanceIdWinner> Winners { get; init; }
        public required IReadOnlyList<ConfigProvenanceDeletion> Deletions { get; init; }
        public required IReadOnlyList<ConfigProvenanceDuplicate> Duplicates { get; init; }
        public required IReadOnlyList<ConfigProvenanceComponentChain> ComponentOverrideChains { get; init; }
    }

    public sealed class ConfigProvenanceSnapshot
    {
        public required IReadOnlyList<ConfigProvenanceFile> Files { get; init; }
    }

    /// <summary>
    /// 严格装载失败时，把合并来源写进异常文本。
    /// 装载器会在 catch 里用 ex.Message 再包一层，后缀必须先写在原始异常上，否则这层包装把溯源丢掉。
    /// 只改 Open 包住的调用；其它线程上的异常不动。
    /// </summary>
    public static class ConfigMergeProvenance
    {
        public const string TemplatesRelativePath = "Entities/templates.json";
        private const string ProvenanceMark = "\n配置路径: ";
        private const string MarkerKey = "ConfigMergeProvenance";

        private static readonly AsyncLocal<Scope?> Current = new();
        private static readonly AsyncLocal<int> HandlerDepth = new();
        // JsonException 自己还有一份 _message，Message 读的是它，不是 Exception._message。
        // 只改基类字段的话，反序列化错误的文本上看不到溯源。
        private static readonly FieldInfo ExceptionMessageField = typeof(Exception).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException("Exception message field is required to attach config provenance.");
        private static readonly FieldInfo? JsonExceptionMessageField = typeof(JsonException).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        private static int _handlerInstalled;

        public static Scope Open(string relativePath, ConfigConflictReport? report)
        {
            EnsureHandler();
            if (report == null)
            {
                for (Scope? existing = Current.Value; existing != null; existing = existing.Parent)
                {
                    if (string.Equals(existing.RelativePath, relativePath, StringComparison.Ordinal))
                    {
                        report = existing.Report;
                        break;
                    }
                }
            }

            report ??= new ConfigConflictReport();
            var scope = new Scope(relativePath, report, Current.Value);
            Current.Value = scope;
            return scope;
        }

        public static ConfigConflictReport? ResolveReport(string relativePath, ConfigConflictReport? report)
        {
            if (report != null)
            {
                return report;
            }

            for (Scope? scope = Current.Value; scope != null; scope = scope.Parent)
            {
                if (string.Equals(scope.RelativePath, relativePath, StringComparison.Ordinal))
                {
                    return scope.Report;
                }
            }

            return null;
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

            if (catalog.TryGet(TemplatesRelativePath, out _))
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

                using Scope expand = Open(TemplatesRelativePath, report);
                EntityTemplateInheritance.ExpandAll(byId, report);
            }

            return ToSnapshot(report);
        }

        public static ConfigProvenanceSnapshot ToSnapshot(ConfigConflictReport report)
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

                IReadOnlyList<(string Path, string Id)> deletions = report.GetDeletions(path);
                var deletionDtos = new List<ConfigProvenanceDeletion>(deletions.Count);
                for (int d = 0; d < deletions.Count; d++)
                {
                    deletionDtos.Add(new ConfigProvenanceDeletion { Id = deletions[d].Id });
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

                files.Add(new ConfigProvenanceFile
                {
                    RelativePath = path,
                    Fragments = fragmentCopy,
                    FileWinner = report.TryGetFileWinner(path, out string fileWinner) ? fileWinner : null,
                    Winners = winnerDtos,
                    Deletions = deletionDtos,
                    Duplicates = duplicateDtos,
                    ComponentOverrideChains = chainDtos,
                });
            }

            return new ConfigProvenanceSnapshot { Files = files };
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

        private static void EnsureHandler()
        {
            if (Interlocked.Exchange(ref _handlerInstalled, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        }

        private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (HandlerDepth.Value != 0)
            {
                return;
            }

            Scope? scope = Current.Value;
            if (scope == null)
            {
                return;
            }

            Exception ex = args.Exception;
            if (ex is not JsonException and not InvalidOperationException)
            {
                return;
            }

            if (ex.Data.Contains(MarkerKey))
            {
                return;
            }

            HandlerDepth.Value = 1;
            try
            {
                string message = ex.Message;
                if (message.Contains(ProvenanceMark, StringComparison.Ordinal))
                {
                    ex.Data[MarkerKey] = scope.RelativePath;
                    return;
                }

                SetMessage(ex, message + Format(scope));
                ex.Data[MarkerKey] = scope.RelativePath;
            }
            finally
            {
                HandlerDepth.Value = 0;
            }
        }

        private static void SetMessage(Exception ex, string message)
        {
            if (ex is JsonException && JsonExceptionMessageField != null)
            {
                JsonExceptionMessageField.SetValue(ex, message);
            }

            ExceptionMessageField.SetValue(ex, message);
        }

        private static string Format(Scope scope)
        {
            var text = new StringBuilder();
            text.Append(ProvenanceMark);
            text.Append(scope.RelativePath);
            text.Append("\n合并顺序: ");
            IReadOnlyList<string> fragments = scope.Report.GetFragments(scope.RelativePath);
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
            if (scope.Report.TryGetFileWinner(scope.RelativePath, out string fileWinner))
            {
                text.Append(fileWinner);
            }
            else
            {
                IReadOnlyList<(string Id, string SourceUri)> winners = scope.Report.GetWinners(scope.RelativePath);
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

        public sealed class Scope : IDisposable
        {
            private bool _disposed;

            internal Scope(string relativePath, ConfigConflictReport report, Scope? parent)
            {
                RelativePath = relativePath;
                Report = report;
                Parent = parent;
            }

            public string RelativePath { get; }
            public ConfigConflictReport Report { get; }
            internal Scope? Parent { get; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (ReferenceEquals(Current.Value, this))
                {
                    Current.Value = Parent;
                }
            }
        }
    }
}
