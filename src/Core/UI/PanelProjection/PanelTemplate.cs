using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Author-facing panel contract (#1010): declared variables plus control binds.
    /// Each variable maps onto a <see cref="PanelVariableBinding"/>; every value flows
    /// through <see cref="PanelProjectionReader"/> — surfaces never fetch data themselves.
    /// </summary>
    public sealed class PanelTemplate
    {
        public PanelTemplate(string id, IReadOnlyList<PanelTemplateVariable> variables, IReadOnlyList<PanelTemplateBind>? binds = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Panel template id is required.", nameof(id));
            }

            if (variables == null || variables.Count == 0)
            {
                throw new ArgumentException($"Panel template '{id}' must declare at least one variable.", nameof(variables));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelTemplateVariable variable in variables)
            {
                if (variable == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null variable entry.", nameof(variables));
                }

                if (!seen.Add(variable.Name))
                {
                    throw new ArgumentException($"Panel template '{id}' declares duplicate variable '{variable.Name}'.", nameof(variables));
                }
            }

            List<PanelTemplateBind> safeBinds = new List<PanelTemplateBind>(binds ?? Array.Empty<PanelTemplateBind>());
            foreach (PanelTemplateBind bind in safeBinds)
            {
                if (bind == null)
                {
                    throw new ArgumentException($"Panel template '{id}' has a null bind entry.", nameof(binds));
                }

                if (!seen.Contains(bind.Variable))
                {
                    throw new ArgumentException(
                        $"Panel template '{id}' bind '{bind.Control}' references undeclared variable '{bind.Variable}'.",
                        nameof(binds));
                }
            }

            Id = id.Trim();
            Variables = variables;
            Binds = safeBinds;
        }

        public string Id { get; }
        public IReadOnlyList<PanelTemplateVariable> Variables { get; }
        public IReadOnlyList<PanelTemplateBind> Binds { get; }

        public PanelVariableBinding ResolveBinding(string variableName)
        {
            foreach (PanelTemplateVariable variable in Variables)
            {
                if (string.Equals(variable.Name, variableName, StringComparison.Ordinal))
                {
                    return variable.ToBinding();
                }
            }

            throw new InvalidOperationException($"Panel template '{Id}' has no variable '{variableName}'.");
        }
    }

    public sealed class PanelTemplateVariable
    {
        public PanelTemplateVariable(
            string name,
            PanelTemplateVariableKind kind,
            PanelBindingSourceKind sourceKind,
            string? attributeId = null,
            string? graphOutputKey = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Variable name is required.", nameof(name));
            }

            Name = name.Trim();
            Kind = kind;
            SourceKind = sourceKind;

            // Reuse the binding contract's own fail-closed field rules.
            Binding = new PanelVariableBinding(Name, sourceKind, attributeId, graphOutputKey);
            AttributeId = Binding.AttributeId;
            GraphOutputKey = Binding.GraphOutputKey;
        }

        public string Name { get; }
        public PanelTemplateVariableKind Kind { get; }
        public PanelBindingSourceKind SourceKind { get; }
        public string? AttributeId { get; }
        public string? GraphOutputKey { get; }

        internal PanelVariableBinding Binding { get; }

        public PanelVariableBinding ToBinding() => Binding;
    }

    public enum PanelTemplateVariableKind : byte
    {
        Float = 0,
        Int = 1,
    }

    public sealed class PanelTemplateBind
    {
        public PanelTemplateBind(string control, string variable)
        {
            if (string.IsNullOrWhiteSpace(control))
            {
                throw new ArgumentException("Bind control id is required.", nameof(control));
            }

            if (string.IsNullOrWhiteSpace(variable))
            {
                throw new ArgumentException($"Bind '{control}' requires a variable name.", nameof(variable));
            }

            Control = control.Trim();
            Variable = variable.Trim();
        }

        public string Control { get; }
        public string Variable { get; }
    }
}
