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

    public enum PanelAttributeFilterOp : byte
    {
        Gt = 0,
        Gte = 1,
        Lt = 2,
        Lte = 3,
        Eq = 4,
    }

    public enum PanelLayoutControlType : byte
    {
        Label = 0,
        ProgressBar = 1,
        Badge = 2,
        List = 3,
    }

    public sealed class PanelListFilter
    {
        public PanelListFilter(string attribute, PanelAttributeFilterOp op, float value)
        {
            Attribute = attribute;
            Op = op;
            Value = value;
        }

        public string Attribute { get; }
        public PanelAttributeFilterOp Op { get; }
        public float Value { get; }
        public int AttributeId { get; internal set; } = -1;
    }

    public sealed class PanelListSort
    {
        public PanelListSort(string attribute, bool descending)
        {
            Attribute = attribute;
            Descending = descending;
        }

        public string Attribute { get; }
        public bool Descending { get; }
        public int AttributeId { get; internal set; } = -1;
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

    public sealed class PanelListDeclaration
    {
        public PanelListDeclaration(
            string name,
            string collectionKey,
            IReadOnlyList<PanelListFilter> filters,
            IReadOnlyList<PanelListSort> sorts,
            IReadOnlyList<PanelItemField> fields)
        {
            Name = name;
            CollectionKey = collectionKey;
            Filters = filters;
            Sorts = sorts;
            Fields = fields;
        }

        public string Name { get; }
        public string CollectionKey { get; }
        public IReadOnlyList<PanelListFilter> Filters { get; }
        public IReadOnlyList<PanelListSort> Sorts { get; }
        public IReadOnlyList<PanelItemField> Fields { get; }
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
            bool? showWhen,
            IReadOnlyList<PanelLayoutControl>? itemControls)
        {
            Type = type;
            ClassName = className;
            Text = text;
            Bind = bind;
            Prefix = prefix;
            Current = current;
            Max = max;
            ShowWhen = showWhen;
            ItemControls = itemControls ?? Array.Empty<PanelLayoutControl>();
        }

        public PanelLayoutControlType Type { get; }
        public string? ClassName { get; }
        public string? Text { get; }
        public string? Bind { get; }
        public string? Prefix { get; }
        public string? Current { get; }
        public string? Max { get; }
        public bool? ShowWhen { get; }
        public IReadOnlyList<PanelLayoutControl> ItemControls { get; }
    }

    public sealed class PanelLayout
    {
        public PanelLayout(IReadOnlyList<PanelLayoutControl> controls)
        {
            Controls = controls;
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
