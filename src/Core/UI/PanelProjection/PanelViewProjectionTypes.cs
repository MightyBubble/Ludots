using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// What a collection element template resolves against.
    /// Host panels use <see cref="None"/>.
    /// </summary>
    public enum PanelSubjectKind : byte
    {
        None = 0,
        Entity = 1,
        Task = 2,
        Ability = 3,
        EffectInstance = 4,
        EffectTemplate = 5,
        ItemInstance = 6,
        ItemDefinition = 7,
        AbilitySlot = 8,
        AbilityDefinition = 9,
        Activity = 10,
        Tag = 11,
        ProgressionNode = 12,
    }

    public enum PanelCollectionSourceKind : byte
    {
        SelfGraph = 0,
        Input = 1,
    }

    public enum PanelLayoutControlType : byte
    {
        Label = 0,
        ProgressBar = 1,
        Badge = 2,
        List = 3,
        Image = 4,
        Row = 5,
        Column = 6,
        RichText = 7,
        Repeater = 8,
    }

    public enum PanelPresentMode : byte
    {
        List = 0,
        Aggregate = 1,
        Grid = 2,
        Column = 3,
    }

    /// <summary>
    /// Aggregate present count text (query-graph-collection-outputs §3.7.3 subset).
    /// </summary>
    public sealed class PanelAggregateCountSpec
    {
        public PanelAggregateCountSpec(string from, string prefix)
        {
            From = from;
            Prefix = prefix;
        }

        /// <summary>Only <c>totalCount</c> is supported this slice.</summary>
        public string From { get; }

        /// <summary>Author-owned prefix; empty string allowed but field required.</summary>
        public string Prefix { get; }
    }

    /// <summary>
    /// Explicit parent→child pin (query-graph-collection-outputs §2.4).
    /// </summary>
    public sealed class PanelInputBinding
    {
        public PanelInputBinding(string name, string fromSpace, string fromOutput, string type)
        {
            Name = name;
            FromSpace = fromSpace;
            FromOutput = fromOutput;
            Type = type;
        }

        public string Name { get; }
        public string FromSpace { get; }

        /// <summary>
        /// Parent graph collection outputs consumed through source=input publish
        /// their collectionKey with this exact remapping value.
        /// </summary>
        public string FromOutput { get; }
        public string Type { get; }
    }

    /// <summary>
    /// Container binding: graph collection + reusable element template id.
    /// Membership/order come from the query graph; each member is passed through
    /// as the element template's evaluation scope.
    /// </summary>
    public sealed class PanelCollectionBinding
    {
        public PanelCollectionBinding(
            string name,
            string collectionKey,
            string templateId,
            PanelCollectionSourceKind source = PanelCollectionSourceKind.SelfGraph,
            string? inputName = null)
        {
            Name = name;
            CollectionKey = collectionKey;
            TemplateId = templateId;
            Source = source;
            InputName = inputName;
        }

        public string Name { get; }
        public string CollectionKey { get; }
        public string TemplateId { get; }
        public PanelCollectionSourceKind Source { get; }
        public string? InputName { get; }

        /// <summary>Resolved after catalog load.</summary>
        public PanelTemplate? Template { get; internal set; }
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
            float? viewportHeight = null,
            float? itemExtent = null,
            bool virtualize = false,
            int overscan = 2,
            PanelPresentMode present = PanelPresentMode.List,
            int? columns = null,
            PanelAggregateCountSpec? aggregateCount = null,
            string? src = null,
            float? width = null,
            float? height = null,
            IReadOnlyList<PanelLayoutControl>? children = null,
            float? gap = null,
            string? align = null,
            string? justify = null,
            string? widthBind = null,
            string? heightBind = null,
            float? fontSize = null,
            bool bold = false,
            string? textRunsBind = null,
            string? objectFit = null,
            string? visibleWhenNotEmpty = null,
            string? classBind = null,
            string? colorBind = null,
            string? backgroundBind = null)
        {
            Type = type;
            ClassName = className;
            Text = text;
            Bind = bind;
            Prefix = prefix;
            Current = current;
            Max = max;
            ShowWhen = showWhen;
            ViewportHeight = viewportHeight;
            ItemExtent = itemExtent;
            Virtualize = virtualize;
            Overscan = overscan;
            Present = present;
            Columns = columns;
            AggregateCount = aggregateCount;
            Src = src;
            Width = width;
            Height = height;
            Children = children ?? Array.Empty<PanelLayoutControl>();
            Gap = gap;
            Align = align;
            Justify = justify;
            WidthBind = widthBind;
            HeightBind = heightBind;
            FontSize = fontSize;
            Bold = bold;
            TextRunsBind = textRunsBind;
            ObjectFit = objectFit;
            VisibleWhenNotEmpty = visibleWhenNotEmpty;
            ClassBind = classBind;
            ColorBind = colorBind;
            BackgroundBind = backgroundBind;
        }

        public PanelLayoutControlType Type { get; }
        public string? ClassName { get; }
        public string? Text { get; }
        public string? Bind { get; }
        public string? Prefix { get; }
        public string? Current { get; }
        public string? Max { get; }
        public bool? ShowWhen { get; }

        /// <summary>Literal presentation imageId for <see cref="PanelLayoutControlType.Image"/> (xor <see cref="Bind"/>).</summary>
        public string? Src { get; }

        /// <summary>Pixel width for <see cref="PanelLayoutControlType.Image"/>.</summary>
        public float? Width { get; }

        /// <summary>Pixel height for <see cref="PanelLayoutControlType.Image"/>.</summary>
        public float? Height { get; }

        /// <summary>Fixed scroll viewport height in px; null = grow with content (no scroll).</summary>
        public float? ViewportHeight { get; }

        /// <summary>Fixed row extent for virtualization / grid cell height; required when <see cref="Virtualize"/>.</summary>
        public float? ItemExtent { get; }

        /// <summary>Compose only the visible window (+ overscan). Requires <see cref="ViewportHeight"/>.</summary>
        public bool Virtualize { get; }

        public int Overscan { get; }

        /// <summary>List control presentation: list / grid / column / aggregate.</summary>
        public PanelPresentMode Present { get; }

        /// <summary>Grid column count; required when <see cref="Present"/> is <see cref="PanelPresentMode.Grid"/>.</summary>
        public int? Columns { get; }

        /// <summary>Required when <see cref="Present"/> is <see cref="PanelPresentMode.Aggregate"/>.</summary>
        public PanelAggregateCountSpec? AggregateCount { get; }
        public IReadOnlyList<PanelLayoutControl> Children { get; }
        public float? Gap { get; }
        public string? Align { get; }
        public string? Justify { get; }
        public string? WidthBind { get; }
        public string? HeightBind { get; }
        public float? FontSize { get; }
        public bool Bold { get; }
        public string? TextRunsBind { get; }
        public string? ObjectFit { get; }
        public string? VisibleWhenNotEmpty { get; }
        public string? ClassBind { get; }
        public string? ColorBind { get; }
        public string? BackgroundBind { get; }
    }

    public sealed class PanelLayout
    {
        public PanelLayout(IReadOnlyList<PanelLayoutControl> controls)
        {
            Controls = controls ?? Array.Empty<PanelLayoutControl>();
        }

        public IReadOnlyList<PanelLayoutControl> Controls { get; }
    }

    public sealed class PanelListItemProjection
    {
        public PanelListItemProjection(
            Dictionary<string, float> floats,
            Dictionary<string, bool> bools,
            Dictionary<string, string> strings,
            IReadOnlyList<PanelListProjection>? nestedLists = null,
            int memberIntId = 0)
        {
            Floats = floats;
            Bools = bools;
            Strings = strings;
            NestedLists = nestedLists ?? Array.Empty<PanelListProjection>();
            MemberIntId = memberIntId;
        }

        public IReadOnlyDictionary<string, float> Floats { get; }
        public IReadOnlyDictionary<string, bool> Bools { get; }
        public IReadOnlyDictionary<string, string> Strings { get; }
        public IReadOnlyList<PanelListProjection> NestedLists { get; }
        public int MemberIntId { get; }
    }

    public sealed class PanelListProjection
    {
        public PanelListProjection(
            string name,
            IReadOnlyList<PanelListItemProjection> items,
            int totalCount = -1,
            int startIndex = 0)
        {
            Name = name;
            Items = items;
            TotalCount = totalCount < 0 ? items.Count : totalCount;
            StartIndex = startIndex;
        }

        public string Name { get; }
        public IReadOnlyList<PanelListItemProjection> Items { get; }

        /// <summary>Full collection size (may exceed <see cref="Items"/> when windowed).</summary>
        public int TotalCount { get; }

        /// <summary>Absolute index of <see cref="Items"/>[0] in the collection.</summary>
        public int StartIndex { get; }
    }

    public readonly record struct PanelListViewWindow(int StartIndex, int EndIndexExclusive)
    {
        public static PanelListViewWindow All => new(0, int.MaxValue);

        public int ClampEnd(int totalCount) => Math.Min(EndIndexExclusive, totalCount);
    }

    public readonly record struct PanelProjectionContext(
        Arch.Core.Entity HostScope,
        Arch.Core.Entity MemberScope);

    public static class PanelSubjectKinds
    {
        public static PanelSubjectKind Parse(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} subject is required.");
            }

            return text.Trim() switch
            {
                "Entity" => PanelSubjectKind.Entity,
                "Task" => PanelSubjectKind.Task,
                "Ability" => PanelSubjectKind.Ability,
                "EffectInstance" => PanelSubjectKind.EffectInstance,
                "EffectTemplate" => PanelSubjectKind.EffectTemplate,
                "ItemInstance" => PanelSubjectKind.ItemInstance,
                "ItemDefinition" => PanelSubjectKind.ItemDefinition,
                "AbilitySlot" => PanelSubjectKind.AbilitySlot,
                "AbilityDefinition" => PanelSubjectKind.AbilityDefinition,
                "Activity" => PanelSubjectKind.Activity,
                "Tag" => PanelSubjectKind.Tag,
                "ProgressionNode" => PanelSubjectKind.ProgressionNode,
                _ => throw new InvalidOperationException(
                    $"{context} subject '{text}' is unknown (allowed: Entity, Task, Ability, EffectInstance, EffectTemplate, ItemInstance, ItemDefinition, AbilitySlot, AbilityDefinition, Activity, Tag, ProgressionNode)."),
            };
        }

        public static string ToId(PanelSubjectKind kind) => kind switch
        {
            PanelSubjectKind.None => "None",
            PanelSubjectKind.Entity => "Entity",
            PanelSubjectKind.Task => "Task",
            PanelSubjectKind.Ability => "Ability",
            PanelSubjectKind.EffectInstance => "EffectInstance",
            PanelSubjectKind.EffectTemplate => "EffectTemplate",
            PanelSubjectKind.ItemInstance => "ItemInstance",
            PanelSubjectKind.ItemDefinition => "ItemDefinition",
            PanelSubjectKind.AbilitySlot => "AbilitySlot",
            PanelSubjectKind.AbilityDefinition => "AbilityDefinition",
            PanelSubjectKind.Activity => "Activity",
            PanelSubjectKind.Tag => "Tag",
            PanelSubjectKind.ProgressionNode => "ProgressionNode",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown panel subject kind."),
        };

        public static bool IsEntityBagSubject(PanelSubjectKind kind) =>
            kind is PanelSubjectKind.Entity
                or PanelSubjectKind.EffectInstance
                or PanelSubjectKind.ItemInstance
                or PanelSubjectKind.Task
                or PanelSubjectKind.Activity
                or PanelSubjectKind.Ability;

        public static bool IsIntIdBagSubject(PanelSubjectKind kind) =>
            kind is PanelSubjectKind.EffectTemplate
                or PanelSubjectKind.ItemDefinition
                or PanelSubjectKind.AbilitySlot
                or PanelSubjectKind.AbilityDefinition
                or PanelSubjectKind.Tag
                or PanelSubjectKind.ProgressionNode;

        /// <summary>Entity / effect-instance subject surface available to layout binds (not graph pins).</summary>
        public const string EntityDisplayName = "displayName";

        /// <summary>
        /// Presentation imageId surface for <c>type: image</c> binds (not graph pins).
        /// Portrait / standing / buff icon are all the same control — only the id differs.
        /// </summary>
        public const string ImageId = "imageId";
    }

    public static class PanelPresentModes
    {
        public static PanelPresentMode Parse(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} present is required when declared.");
            }

            return text.Trim() switch
            {
                "list" => PanelPresentMode.List,
                "aggregate" => PanelPresentMode.Aggregate,
                "grid" => PanelPresentMode.Grid,
                "column" => PanelPresentMode.Column,
                _ => throw new InvalidOperationException(
                    $"{context} present '{text}' is unknown (allowed: list, grid, column, aggregate)."),
            };
        }

        public static string ToId(PanelPresentMode mode) => mode switch
        {
            PanelPresentMode.List => "list",
            PanelPresentMode.Aggregate => "aggregate",
            PanelPresentMode.Grid => "grid",
            PanelPresentMode.Column => "column",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown panel present mode."),
        };
    }

    public static class PanelCollectionSources
    {
        public static PanelCollectionSourceKind Parse(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} source is required (selfGraph|input).");
            }

            return text.Trim() switch
            {
                "selfGraph" => PanelCollectionSourceKind.SelfGraph,
                "input" => PanelCollectionSourceKind.Input,
                _ => throw new InvalidOperationException(
                    $"{context} source '{text}' is unknown (allowed: selfGraph, input)."),
            };
        }

        public static string ToId(PanelCollectionSourceKind kind) => kind switch
        {
            PanelCollectionSourceKind.SelfGraph => "selfGraph",
            PanelCollectionSourceKind.Input => "input",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown panel collection source kind."),
        };
    }
}
