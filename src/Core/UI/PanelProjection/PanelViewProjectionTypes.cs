using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>Item field leaf kind: always produces a scalar for controls.</summary>
    public enum PanelItemFieldKind : byte
    {
        Attribute = 0,
        AttributeBase = 1,
        Tag = 2,
        Name = 3,
    }

    public enum PanelLayoutControlType : byte
    {
        Label = 0,
        ProgressBar = 1,
        Badge = 2,
        List = 3,
    }

    public sealed class PanelItemField
    {
        public PanelItemField(string name, PanelItemFieldKind kind, string? symbol)
        {
            Name = name;
            Kind = kind;
            Symbol = symbol;
        }

        public string Name { get; }
        public PanelItemFieldKind Kind { get; }
        public string? Symbol { get; }
        public int SymbolId { get; internal set; } = -1;
    }

    /// <summary>
    /// Reusable one-entity presentation template. Does not know list/grid parents
    /// or which collection it will be bound to.
    /// </summary>
    public sealed class PanelItemTemplate
    {
        public PanelItemTemplate(string id, IReadOnlyList<PanelItemField> fields, PanelLayout layout)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Item template id is required.", nameof(id));
            }

            if (fields == null || fields.Count == 0)
            {
                throw new ArgumentException($"Item template '{id}' must declare at least one field.", nameof(fields));
            }

            if (layout == null || layout.Controls.Count == 0)
            {
                throw new ArgumentException($"Item template '{id}' requires a non-empty layout.", nameof(layout));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PanelItemField field in fields)
            {
                if (field == null)
                {
                    throw new ArgumentException($"Item template '{id}' has a null field.", nameof(fields));
                }

                if (!seen.Add(field.Name))
                {
                    throw new ArgumentException($"Item template '{id}' declares duplicate field '{field.Name}'.", nameof(fields));
                }
            }

            Id = id.Trim();
            Fields = fields;
            Layout = layout;
        }

        public string Id { get; }
        public IReadOnlyList<PanelItemField> Fields { get; }
        public PanelLayout Layout { get; }
    }

    /// <summary>
    /// Container binding: graph collection + which reusable item template fills each row.
    /// Membership and order are owned by the query graph.
    /// </summary>
    public sealed class PanelCollectionBinding
    {
        public PanelCollectionBinding(string name, string collectionKey, string itemTemplateId)
        {
            Name = name;
            CollectionKey = collectionKey;
            ItemTemplateId = itemTemplateId;
        }

        public string Name { get; }
        public string CollectionKey { get; }
        public string ItemTemplateId { get; }

        /// <summary>Resolved after catalog load; null until <see cref="PanelItemTemplateBinder"/> runs.</summary>
        public PanelItemTemplate? Item { get; internal set; }
    }

    public sealed class PanelLayoutControl
    {
        public PanelLayoutControl(
            PanelLayoutControlType type,
            string? className,
            string? text,
            string? bind,
            string? prefix,
            string? current,
            string? max,
            bool? showWhen)
        {
            Type = type;
            ClassName = className;
            Text = text;
            Bind = bind;
            Prefix = prefix;
            Current = current;
            Max = max;
            ShowWhen = showWhen;
        }

        public PanelLayoutControlType Type { get; }
        public string? ClassName { get; }
        public string? Text { get; }
        public string? Bind { get; }
        public string? Prefix { get; }
        public string? Current { get; }
        public string? Max { get; }
        public bool? ShowWhen { get; }
    }

    public sealed class PanelLayout
    {
        public PanelLayout(IReadOnlyList<PanelLayoutControl> controls)
        {
            Controls = controls ?? Array.Empty<PanelLayoutControl>();
        }

        public IReadOnlyList<PanelLayoutControl> Controls { get; }
    }

    /// <summary>One projected list item: scalar bags only (no Entity exposed to controls).</summary>
    public sealed class PanelListItemProjection
    {
        public PanelListItemProjection(
            Dictionary<string, float> floats,
            Dictionary<string, bool> bools,
            Dictionary<string, string> strings)
        {
            Floats = floats;
            Bools = bools;
            Strings = strings;
        }

        public IReadOnlyDictionary<string, float> Floats { get; }
        public IReadOnlyDictionary<string, bool> Bools { get; }
        public IReadOnlyDictionary<string, string> Strings { get; }
    }

    public sealed class PanelListProjection
    {
        public PanelListProjection(string name, IReadOnlyList<PanelListItemProjection> items)
        {
            Name = name;
            Items = items;
        }

        public string Name { get; }
        public IReadOnlyList<PanelListItemProjection> Items { get; }
    }
}
