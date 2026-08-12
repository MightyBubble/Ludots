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
                            ResolveGraphsRelativePath(modRootPath),
                            $"Upsert graph '{op.DefinitionId}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, ResolveGraphsRelativePath(modRootPath)))));
                        break;
                    case LiveDebugPatchOperationKind.SkillEffectNumeric:
                    case LiveDebugPatchOperationKind.EffectTemplateRef:
                    case LiveDebugPatchOperationKind.EffectGrantedTag:
                        files.Add(new LiveEditSaveFilePlan(
                            ResolveEffectsRelativePath(modRootPath),
                            $"Upsert effect field '{op.DefinitionId}.{op.FieldPath ?? op.Kind.ToString()}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, ResolveEffectsRelativePath(modRootPath)))));
                        break;
                    case LiveDebugPatchOperationKind.AttrConstraintNumeric:
                        files.Add(new LiveEditSaveFilePlan(
                            ResolveAttributesRelativePath(modRootPath),
                            $"Upsert attribute constraint '{op.DefinitionId}.{op.FieldPath}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, ResolveAttributesRelativePath(modRootPath)))));
                        break;
                    case LiveDebugPatchOperationKind.TagRuleBodyReplace:
                        files.Add(new LiveEditSaveFilePlan(
                            ResolveTagRulesRelativePath(modRootPath),
                            $"Upsert tag rule '{op.DefinitionId}'",
                            isNewFile: !File.Exists(Path.Combine(modRootPath, ResolveTagRulesRelativePath(modRootPath)))));
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
                        string rel = ResolveGraphsRelativePath(preview.ModRootPath);
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertGraphDocument(path, op.DefinitionId!, op.DocumentJson!);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else if (op.Kind == LiveDebugPatchOperationKind.TagRuleBodyReplace)
                    {
                        string rel = ResolveTagRulesRelativePath(preview.ModRootPath);
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertTagRule(path, op.DefinitionId!, op.DocumentJson!);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else if (op.Kind == LiveDebugPatchOperationKind.SkillEffectNumeric
                             || op.Kind == LiveDebugPatchOperationKind.EffectTemplateRef
                             || op.Kind == LiveDebugPatchOperationKind.EffectGrantedTag)
                    {
                        string rel = ResolveEffectsRelativePath(preview.ModRootPath);
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertEffectPatch(path, in op);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else if (op.Kind == LiveDebugPatchOperationKind.AttrConstraintNumeric)
                    {
                        string rel = ResolveAttributesRelativePath(preview.ModRootPath);
                        string path = Path.Combine(preview.ModRootPath, rel);
                        UpsertAttributeConstraint(path, in op);
                        if (!written.Contains(rel)) written.Add(rel);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Operation '{op.Kind}' has no save mapping.");
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

        private static string ResolveEffectsRelativePath(string modRootPath)
        {
            const string configs = "assets/Configs/GAS/effects.json";
            const string shortPath = "assets/GAS/effects.json";
            if (File.Exists(Path.Combine(modRootPath, configs))) return configs;
            if (File.Exists(Path.Combine(modRootPath, shortPath))) return shortPath;
            return configs;
        }

        private static string ResolveGraphsRelativePath(string modRootPath)
        {
            const string configs = "assets/Configs/GAS/graphs.json";
            const string shortPath = "assets/GAS/graphs.json";
            if (File.Exists(Path.Combine(modRootPath, configs))) return configs;
            if (File.Exists(Path.Combine(modRootPath, shortPath))) return shortPath;
            return configs;
        }

        private static string ResolveTagRulesRelativePath(string modRootPath)
        {
            const string configs = "assets/Configs/GAS/tag_rules.json";
            const string shortPath = "assets/GAS/tag_rules.json";
            if (File.Exists(Path.Combine(modRootPath, configs))) return configs;
            if (File.Exists(Path.Combine(modRootPath, shortPath))) return shortPath;
            return configs;
        }

        private static string ResolveAttributesRelativePath(string modRootPath)
        {
            const string configs = "assets/Configs/GAS/attributes.json";
            const string shortPath = "assets/GAS/attributes.json";
            if (File.Exists(Path.Combine(modRootPath, configs))) return configs;
            if (File.Exists(Path.Combine(modRootPath, shortPath))) return shortPath;
            return configs;
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

        private void UpsertEffectPatch(string path, in LiveDebugPatchOperation op)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonArray array = LoadOrCreateArray(path);
            JsonObject effect = FindOrCreateObjectById(array, op.DefinitionId!);

            if (op.Kind == LiveDebugPatchOperationKind.SkillEffectNumeric)
            {
                ApplyNumericFieldPath(effect, op.FieldPath!, op.NumericValue);
            }
            else if (op.Kind == LiveDebugPatchOperationKind.EffectTemplateRef)
            {
                ApplyStringFieldPath(effect, op.FieldPath!, op.DocumentJson!);
            }
            else if (op.Kind == LiveDebugPatchOperationKind.EffectGrantedTag)
            {
                ApplyGrantedTag(effect, op.DocumentJson!, (int)Math.Round(op.NumericValue));
            }
            else
            {
                throw new InvalidOperationException($"Effect save mapping does not support '{op.Kind}'.");
            }

            File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
        }

        private void UpsertAttributeConstraint(string path, in LiveDebugPatchOperation op)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonArray array = LoadOrCreateArray(path);
            JsonObject attr = FindOrCreateObjectById(array, op.DefinitionId!);
            ApplyNumericFieldPath(attr, op.FieldPath!, op.NumericValue);
            File.WriteAllText(path, array.ToJsonString(_jsonOptions), Encoding.UTF8);
        }

        private static JsonArray LoadOrCreateArray(string path)
        {
            if (!File.Exists(path))
            {
                return new JsonArray();
            }

            return JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                ?? throw new InvalidOperationException($"Existing '{path}' is not a JSON array.");
        }

        private static JsonObject FindOrCreateObjectById(JsonArray array, string id)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject obj &&
                    string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
                {
                    return obj;
                }
            }

            var created = new JsonObject { ["id"] = id };
            array.Add(created);
            return created;
        }

        private static void ApplyNumericFieldPath(JsonObject root, string fieldPath, double value)
        {
            string path = fieldPath.Trim();
            if (path.Equals("duration.durationTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("DurationTicks", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "duration")["durationTicks"] = (int)Math.Round(value);
                return;
            }

            if (path.Equals("duration.periodTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("PeriodTicks", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "duration")["periodTicks"] = (int)Math.Round(value);
                return;
            }

            if (path.Equals("modifiers.0.value", StringComparison.OrdinalIgnoreCase)
                || path.Equals("modifiers[0].value", StringComparison.OrdinalIgnoreCase))
            {
                JsonArray modifiers = EnsureArray(root, "modifiers");
                if (modifiers.Count == 0)
                {
                    modifiers.Add(new JsonObject());
                }

                if (modifiers[0] is not JsonObject mod0)
                {
                    throw new InvalidOperationException("modifiers[0] must be a JSON object.");
                }

                mod0["value"] = value;
                return;
            }

            if (path.Equals("constraints.min", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "constraints")["min"] = value;
                return;
            }

            if (path.Equals("constraints.max", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "constraints")["max"] = value;
                return;
            }

            throw new InvalidOperationException($"Unsupported numeric save field path '{fieldPath}'.");
        }

        private static void ApplyStringFieldPath(JsonObject root, string fieldPath, string value)
        {
            string path = fieldPath.Trim();
            if (path.Equals("projectile.impactEffect", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "projectile")["impactEffect"] = value;
                return;
            }

            if (path.Equals("projectile.hitEffect", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "projectile")["hitEffect"] = value;
                return;
            }

            if (path.Equals("projectile.presentationEffect", StringComparison.OrdinalIgnoreCase))
            {
                EnsureObject(root, "projectile")["presentationEffect"] = value;
                return;
            }

            throw new InvalidOperationException($"Unsupported string save field path '{fieldPath}'.");
        }

        private static void ApplyGrantedTag(JsonObject root, string tagName, int amount)
        {
            JsonArray granted = EnsureArray(root, "grantedTags");
            for (int i = 0; i < granted.Count; i++)
            {
                if (granted[i] is JsonObject obj &&
                    string.Equals(obj["tag"]?.GetValue<string>(), tagName, StringComparison.OrdinalIgnoreCase))
                {
                    obj["formula"] = "Fixed";
                    obj["amount"] = Math.Clamp(amount, 1, 32);
                    return;
                }
            }

            granted.Add(new JsonObject
            {
                ["tag"] = tagName,
                ["formula"] = "Fixed",
                ["amount"] = Math.Clamp(amount, 1, 32)
            });
        }

        private static JsonObject EnsureObject(JsonObject root, string property)
        {
            if (root[property] is JsonObject existing)
            {
                return existing;
            }

            var created = new JsonObject();
            root[property] = created;
            return created;
        }

        private static JsonArray EnsureArray(JsonObject root, string property)
        {
            if (root[property] is JsonArray existing)
            {
                return existing;
            }

            var created = new JsonArray();
            root[property] = created;
            return created;
        }
    }
}
