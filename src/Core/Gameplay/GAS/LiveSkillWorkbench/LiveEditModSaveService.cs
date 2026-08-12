using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    public sealed class LiveEditSaveFilePlan
    {
        public LiveEditSaveFilePlan(string relativePath, string summary, bool isNewFile)
        {
            RelativePath = relativePath;
            Summary = summary;
            IsNewFile = isNewFile;
        }

        public string RelativePath { get; }
        public string Summary { get; }
        public bool IsNewFile { get; }
    }

    public sealed class LiveEditSavePreview
    {
        public LiveEditSavePreview(
            string targetModId,
            string modRootPath,
            IReadOnlyList<LiveEditSaveFilePlan> files,
            IReadOnlyList<string> excludedImmediateOps,
            IReadOnlyList<LiveEditDiagnostic> diagnostics,
            bool canSave)
        {
            TargetModId = targetModId;
            ModRootPath = modRootPath;
            Files = files;
            ExcludedImmediateOps = excludedImmediateOps;
            Diagnostics = diagnostics;
            CanSave = canSave;
        }

        public string TargetModId { get; }
        public string ModRootPath { get; }
        public IReadOnlyList<LiveEditSaveFilePlan> Files { get; }
        public IReadOnlyList<string> ExcludedImmediateOps { get; }
        public IReadOnlyList<LiveEditDiagnostic> Diagnostics { get; }
        public bool CanSave { get; }
    }

    public sealed class LiveEditSaveResult
    {
        public LiveEditSaveResult(bool succeeded, IReadOnlyList<string> writtenRelativePaths, IReadOnlyList<LiveEditDiagnostic> diagnostics)
        {
            Succeeded = succeeded;
            WrittenRelativePaths = writtenRelativePaths;
            Diagnostics = diagnostics;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<string> WrittenRelativePaths { get; }
        public IReadOnlyList<LiveEditDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// #624: Promote validated session patches into Mod config files (no silent overwrite).
    /// Immediate attribute commands are excluded by default.
    /// </summary>
    public sealed class LiveEditModSaveService
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public LiveEditSavePreview Preview(LiveEditSession session, string targetModId, string modRootPath)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(targetModId))
            {
                throw new ArgumentException("targetModId is required.", nameof(targetModId));
            }

            if (string.IsNullOrWhiteSpace(modRootPath))
            {
                throw new ArgumentException("modRootPath is required.", nameof(modRootPath));
            }

            var diags = new List<LiveEditDiagnostic>();
            var excluded = new List<string>();
            var files = new List<LiveEditSaveFilePlan>();

            if (!Directory.Exists(modRootPath))
            {
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    "LSWSAVE0001",
                    $"Target Mod root does not exist: {modRootPath}",
                    targetModId));
                return new LiveEditSavePreview(targetModId, modRootPath, files, excluded, diags, canSave: false);
            }

            bool hasPersistable = false;
            for (int i = 0; i < session.Patch.Count; i++)
            {
                LiveDebugPatchOperation op = session.Patch.Operations[i];
                if (op.Kind == LiveDebugPatchOperationKind.SelectedActorAttribute)
                {
                    excluded.Add($"{op.AttributeName}:{op.AttributeMutation}");
                    continue;
                }

                hasPersistable = true;
                switch (op.Kind)
                {
                    case LiveDebugPatchOperationKind.GraphBodyReplace:
                        files.Add(new LiveEditSaveFilePlan(
                            "assets/Configs/GAS/graphs.json",
                            $"Upsert graph '{op.DefinitionId}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, "assets/Configs/GAS/graphs.json"))));
                        break;
                    case LiveDebugPatchOperationKind.SkillEffectNumeric:
                    case LiveDebugPatchOperationKind.AttrConstraintNumeric:
                        files.Add(new LiveEditSaveFilePlan(
                            "assets/Configs/GAS/lsw_accepted_patches.json",
                            $"Record numeric patch '{op.DefinitionId}.{op.FieldPath}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, "assets/Configs/GAS/lsw_accepted_patches.json"))));
                        break;
                    case LiveDebugPatchOperationKind.TagRuleBodyReplace:
                        files.Add(new LiveEditSaveFilePlan(
                            "assets/Configs/GAS/tag_rules.json",
                            $"Upsert tag rule '{op.DefinitionId}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, "assets/Configs/GAS/tag_rules.json"))));
                        break;
                    default:
                        diags.Add(new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Error,
                            "LSWSAVE0002",
                            $"Operation '{op.Kind}' has no save mapping.",
                            op.DefinitionId));
                        break;
                }
            }

            if (!hasPersistable)
            {
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    "LSWSAVE0003",
                    "No persistable patches in session (Immediate attribute commands are excluded by default).",
                    targetModId));
            }

            // Deduplicate file plans by path
            var unique = new Dictionary<string, LiveEditSaveFilePlan>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                unique[files[i].RelativePath] = files[i];
            }

            bool canSave = hasPersistable && diags.Count == 0;
            return new LiveEditSavePreview(
                targetModId,
                modRootPath,
                new List<LiveEditSaveFilePlan>(unique.Values),
                excluded,
                diags,
                canSave);
        }

        public LiveEditSaveResult Save(LiveEditSession session, LiveEditSavePreview preview)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (!preview.CanSave)
            {
                return new LiveEditSaveResult(false, Array.Empty<string>(), preview.Diagnostics);
            }

            var written = new List<string>();
            var diags = new List<LiveEditDiagnostic>();

            // Persist graphs via upsert into graphs.json array
            for (int i = 0; i < session.Patch.Count; i++)
            {
                LiveDebugPatchOperation op = session.Patch.Operations[i];
                if (op.Kind == LiveDebugPatchOperationKind.SelectedActorAttribute)
                {
                    continue;
                }

                try
                {
                    if (op.Kind == LiveDebugPatchOperationKind.GraphBodyReplace)
                    {
                        string rel = "assets/Configs/GAS/graphs.json";
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertGraphDocument(path, op.DefinitionId!, op.DocumentJson!);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else if (op.Kind == LiveDebugPatchOperationKind.TagRuleBodyReplace)
                    {
                        string rel = "assets/Configs/GAS/tag_rules.json";
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertTagRule(path, op.DefinitionId!, op.DocumentJson!);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else
                    {
                        string rel = "assets/Configs/GAS/lsw_accepted_patches.json";
                        string path = Path.Combine(preview.ModRootPath, rel);
                        AppendNumericPatch(path, in op);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                }
                catch (Exception ex)
                {
                    diags.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        "LSWSAVE0004",
                        ex.Message,
                        op.DefinitionId));
                }
            }

            if (diags.Count > 0)
            {
                return new LiveEditSaveResult(false, written, diags);
            }

            return new LiveEditSaveResult(true, written, Array.Empty<LiveEditDiagnostic>());
        }

        private void UpsertGraphDocument(string path, string graphId, string documentJson)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonNode incoming = JsonNode.Parse(documentJson)
                ?? throw new InvalidOperationException("Graph document JSON is null.");
            if (incoming is not JsonObject incomingObj)
            {
                throw new InvalidOperationException("Graph document must be a JSON object.");
            }

            incomingObj["id"] = graphId;
            JsonArray array;
            if (File.Exists(path))
            {
                array = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                    ?? throw new InvalidOperationException($"Existing '{path}' is not a JSON array.");
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonObject obj &&
                        string.Equals(obj["id"]?.GetValue<string>(), graphId, StringComparison.OrdinalIgnoreCase))
                    {
                        array[i] = incomingObj.DeepClone();
                        File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
                        return;
                    }
                }

                array.Add(incomingObj.DeepClone());
            }
            else
            {
                array = new JsonArray { incomingObj.DeepClone() };
            }

            File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
        }

        private void UpsertTagRule(string path, string tagId, string documentJson)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonNode incoming = JsonNode.Parse(documentJson)
                ?? throw new InvalidOperationException("Tag rule JSON is null.");
            if (incoming is not JsonObject incomingObj)
            {
                throw new InvalidOperationException("Tag rule document must be a JSON object.");
            }

            incomingObj["id"] = tagId;
            JsonArray array;
            if (File.Exists(path))
            {
                array = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                    ?? throw new InvalidOperationException($"Existing '{path}' is not a JSON array.");
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonObject obj &&
                        string.Equals(obj["id"]?.GetValue<string>(), tagId, StringComparison.OrdinalIgnoreCase))
                    {
                        array[i] = incomingObj.DeepClone();
                        File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
                        return;
                    }
                }

                array.Add(incomingObj.DeepClone());
            }
            else
            {
                array = new JsonArray { incomingObj.DeepClone() };
            }

            File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
        }

        private void AppendNumericPatch(string path, in LiveDebugPatchOperation op)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonArray array;
            if (File.Exists(path))
            {
                array = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                    ?? new JsonArray();
            }
            else
            {
                array = new JsonArray();
            }

            array.Add(new JsonObject
            {
                ["kind"] = op.Kind.ToString(),
                ["definitionId"] = op.DefinitionId,
                ["fieldPath"] = op.FieldPath,
                ["numericValue"] = op.NumericValue,
                ["sourceUri"] = op.Provenance.SourceUri,
                ["savedUtc"] = DateTime.UtcNow.ToString("O")
            });
            File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
        }
    }
}
