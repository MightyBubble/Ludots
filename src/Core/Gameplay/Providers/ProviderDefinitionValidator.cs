using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ludots.Core.Gameplay.Providers
{
    public sealed class ProviderDefinitionReference
    {
        public ProviderDefinitionReference(
            string definitionId,
            string fieldPath,
            ProviderKind kind,
            string key)
        {
            DefinitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Kind = kind;
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public string DefinitionId { get; }
        public string FieldPath { get; }
        public ProviderKind Kind { get; }
        public string Key { get; }
    }

    public sealed class ProviderValidationIssue
    {
        public ProviderValidationIssue(
            string failureCode,
            string key,
            string definitionId,
            string fieldPath,
            string message)
        {
            FailureCode = failureCode;
            Key = key;
            DefinitionId = definitionId;
            FieldPath = fieldPath;
            Message = message;
        }

        public string FailureCode { get; }
        public string Key { get; }
        public string DefinitionId { get; }
        public string FieldPath { get; }
        public string Message { get; }
    }

    public sealed class ProviderValidationReport
    {
        public ProviderValidationReport(
            IReadOnlyList<ProviderDefinitionReference> referencedKeys,
            IReadOnlyList<ProviderValidationIssue> issues)
        {
            ReferencedKeys = referencedKeys;
            Issues = issues;
        }

        public IReadOnlyList<ProviderDefinitionReference> ReferencedKeys { get; }
        public IReadOnlyList<ProviderValidationIssue> Issues { get; }
        public bool Passed => Issues.Count == 0;
    }

    public sealed class ProviderDefinitionValidator
    {
        private readonly SourceProviderRegistry _sources;
        private readonly SelectorProviderRegistry _selectors;
        private readonly ConditionProviderRegistry _conditions;
        private readonly EffectHandlerRegistry _effects;
        private readonly ProviderGapCatalog _gaps;

        public ProviderDefinitionValidator(
            SourceProviderRegistry sources,
            SelectorProviderRegistry selectors,
            ConditionProviderRegistry conditions,
            EffectHandlerRegistry effects,
            ProviderGapCatalog gaps)
        {
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _selectors = selectors ?? throw new ArgumentNullException(nameof(selectors));
            _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _gaps = gaps ?? throw new ArgumentNullException(nameof(gaps));
        }

        public ProviderValidationReport Validate(IEnumerable<ProviderDefinitionReference> references)
        {
            ArgumentNullException.ThrowIfNull(references);
            var referenced = new List<ProviderDefinitionReference>();
            var issues = new List<ProviderValidationIssue>();

            foreach (ProviderDefinitionReference reference in references)
            {
                referenced.Add(reference);
                ValidateOne(reference, issues);
            }

            return new ProviderValidationReport(referenced, issues);
        }

        public ProviderValidationReport ValidateAndThrow(IEnumerable<ProviderDefinitionReference> references)
        {
            ProviderValidationReport report = Validate(references);
            if (report.Passed)
            {
                return report;
            }

            ProviderValidationIssue first = report.Issues[0];
            throw new InvalidOperationException(
                $"{first.FailureCode}: key '{first.Key}' at {first.DefinitionId}:{first.FieldPath} — {first.Message}");
        }

        public static IReadOnlyList<ProviderDefinitionReference> CollectFromJsonDocument(
            string definitionId,
            JsonElement root)
        {
            var list = new List<ProviderDefinitionReference>();
            Walk(definitionId, "$", root, list);
            return list;
        }

        private void ValidateOne(ProviderDefinitionReference reference, List<ProviderValidationIssue> issues)
        {
            if (!ProviderKey.TryParse(reference.Key, out _, out string formCode, out string formReason))
            {
                issues.Add(new ProviderValidationIssue(
                    formCode,
                    reference.Key,
                    reference.DefinitionId,
                    reference.FieldPath,
                    formReason));
                return;
            }

            ProviderLookupResult<object> lookup = reference.Kind switch
            {
                ProviderKind.Source => AsObject(_sources.TryGet(reference.Key)),
                ProviderKind.Selector => AsObject(_selectors.TryGet(reference.Key)),
                ProviderKind.Condition => AsObject(_conditions.TryGet(reference.Key)),
                ProviderKind.Effect => AsObject(_effects.TryGet(reference.Key)),
                _ => ProviderLookupResult<object>.Miss(
                    ProviderFailureCodes.UnknownProviderKey,
                    $"Unsupported provider kind '{reference.Kind}'."),
            };

            if (lookup.Found)
            {
                return;
            }

            if (_gaps.Contains(reference.Key))
            {
                issues.Add(new ProviderValidationIssue(
                    ProviderFailureCodes.NeedsProviderRegistration,
                    reference.Key,
                    reference.DefinitionId,
                    reference.FieldPath,
                    $"Referenced gap entry '{reference.Key}' is not resolvable."));
                return;
            }

            issues.Add(new ProviderValidationIssue(
                lookup.FailureCode,
                reference.Key,
                reference.DefinitionId,
                reference.FieldPath,
                lookup.Reason));
        }

        private static ProviderLookupResult<object> AsObject<T>(ProviderLookupResult<T> result)
            where T : class
        {
            if (result.Found)
            {
                return ProviderLookupResult<object>.Hit(result.Implementation!, result.Schema!);
            }

            return ProviderLookupResult<object>.Miss(result.FailureCode, result.Reason);
        }

        private static void Walk(
            string definitionId,
            string path,
            JsonElement element,
            List<ProviderDefinitionReference> list)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        string childPath = path + "." + property.Name;
                        if (TryMapKeyField(property.Name, out ProviderKind kind) &&
                            property.Value.ValueKind == JsonValueKind.String)
                        {
                            list.Add(new ProviderDefinitionReference(
                                definitionId,
                                childPath,
                                kind,
                                property.Value.GetString() ?? string.Empty));
                        }

                        Walk(definitionId, childPath, property.Value, list);
                    }

                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement child in element.EnumerateArray())
                    {
                        Walk(definitionId, $"{path}[{index}]", child, list);
                        index++;
                    }

                    break;
            }
        }

        private static bool TryMapKeyField(string fieldName, out ProviderKind kind)
        {
            switch (fieldName)
            {
                case "source_key":
                    kind = ProviderKind.Source;
                    return true;
                case "selector_key":
                    kind = ProviderKind.Selector;
                    return true;
                case "condition_key":
                    kind = ProviderKind.Condition;
                    return true;
                case "effect_key":
                    kind = ProviderKind.Effect;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }
    }
}
