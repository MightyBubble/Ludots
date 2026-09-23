using System;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// One panel variable binding contract. Fail-closed at resolve time when refs are missing.
    /// </summary>
    public readonly struct PanelVariableBinding
    {
        public PanelVariableBinding(
            string variableId,
            PanelBindingSourceKind sourceKind,
            string? attributeId,
            string? graphOutputKey,
            string? lookupTable = null,
            string? lookupField = null,
            string? keyAttribute = null,
            PanelTemplateVariableKind valueKind = PanelTemplateVariableKind.Float)
        {
            if (string.IsNullOrWhiteSpace(variableId))
            {
                throw new ArgumentException("variableId is required.", nameof(variableId));
            }

            VariableId = variableId.Trim();
            SourceKind = sourceKind;
            ValueKind = valueKind;

            switch (sourceKind)
            {
                case PanelBindingSourceKind.SingleAttribute:
                case PanelBindingSourceKind.DerivedAttribute:
                case PanelBindingSourceKind.AttributeBase:
                    if (string.IsNullOrWhiteSpace(attributeId))
                    {
                        throw new ArgumentException(
                            $"Binding '{VariableId}' with sourceKind '{sourceKind}' requires attributeId.",
                            nameof(attributeId));
                    }

                    if (!string.IsNullOrWhiteSpace(graphOutputKey))
                    {
                        throw new ArgumentException(
                            $"Binding '{VariableId}' with sourceKind '{sourceKind}' must not declare graphOutputKey.",
                            nameof(graphOutputKey));
                    }

                    AttributeId = attributeId.Trim();
                    GraphOutputKey = null;
                    break;

                case PanelBindingSourceKind.AggregateProjection:
                case PanelBindingSourceKind.GraphOutput:
                    if (string.IsNullOrWhiteSpace(graphOutputKey))
                    {
                        throw new ArgumentException(
                            $"Binding '{VariableId}' with sourceKind '{sourceKind}' requires graphOutputKey.",
                            nameof(graphOutputKey));
                    }

                    if (!string.IsNullOrWhiteSpace(attributeId))
                    {
                        throw new ArgumentException(
                            $"Binding '{VariableId}' with sourceKind '{sourceKind}' must not declare attributeId.",
                            nameof(attributeId));
                    }

                    GraphOutputKey = graphOutputKey.Trim();
                    AttributeId = null;
                    break;

                case PanelBindingSourceKind.TableLookup:
                    if (string.IsNullOrWhiteSpace(lookupTable) ||
                        string.IsNullOrWhiteSpace(lookupField) ||
                        string.IsNullOrWhiteSpace(keyAttribute))
                    {
                        throw new ArgumentException(
                            $"Binding '{VariableId}' with sourceKind '{sourceKind}' requires lookupTable, lookupField and keyAttribute.",
                            nameof(lookupTable));
                    }

                    foreach ((string field, string? value) in new[] { (nameof(attributeId), attributeId), (nameof(graphOutputKey), graphOutputKey) })
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            throw new ArgumentException(
                                $"Binding '{VariableId}' with sourceKind '{sourceKind}' must not declare {field}.",
                                field);
                        }
                    }

                    LookupTable = lookupTable.Trim();
                    LookupField = lookupField.Trim();
                    KeyAttribute = keyAttribute.Trim();
                    AttributeId = null;
                    GraphOutputKey = null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unknown panel binding source kind.");
            }
        }

        public string VariableId { get; }
        public PanelBindingSourceKind SourceKind { get; }
        public string? AttributeId { get; }
        public string? GraphOutputKey { get; }
        public string? LookupTable { get; }
        public string? LookupField { get; }
        public string? KeyAttribute { get; }
        public PanelTemplateVariableKind ValueKind { get; }
    }
}
