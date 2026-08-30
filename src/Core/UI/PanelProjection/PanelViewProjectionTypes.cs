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
    }

    public enum PanelLayoutControlType : byte
    {
        Label = 0,
        ProgressBar = 1,
        Badge = 2,
        List = 3,
        Row = 4,
        Column = 5,
        Image = 6,
        RichText = 7,
        Repeater = 8,
    }

    /// <summary>
    /// Container binding: graph collection + reusable element template id.
    /// Membership/order come from the query graph; each member is passed through
    /// as the element template's evaluation scope.
    /// </summary>
    public sealed class PanelCollectionBinding
    {
        public PanelCollectionBinding(string name, string collectionKey, string templateId)
        {
            Name = name;
            CollectionKey = collectionKey;
            TemplateId = templateId;
        }

        public string Name { get; }
        public string CollectionKey { get; }
        public string TemplateId { get; }

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
            IReadOnlyList<PanelLayoutControl>? children = null,
            float? gap = null,
            string? align = null,
            string? justify = null,
            float? width = null,
            float? height = null,
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
            Children = children ?? Array.Empty<PanelLayoutControl>();
            Gap = gap;
            Align = align;
            Justify = justify;
            Width = width;
            Height = height;
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

        /// <summary>Fixed scroll viewport height in px; null = grow with content (no scroll).</summary>
        public float? ViewportHeight { get; }

        /// <summary>Fixed row extent for virtualization; required when <see cref="Virtualize"/>.</summary>
        public float? ItemExtent { get; }

        /// <summary>Compose only the visible window (+ overscan). Requires <see cref="ViewportHeight"/>.</summary>
        public bool Virtualize { get; }

        public int Overscan { get; }
        public IReadOnlyList<PanelLayoutControl> Children { get; }
        public float? Gap { get; }
        public string? Align { get; }
        public string? Justify { get; }
        public float? Width { get; }
        public float? Height { get; }
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
                _ => throw new InvalidOperationException(
                    $"{context} subject '{text}' is unknown (allowed: Entity, Task, Ability)."),
            };
        }

        public static string ToId(PanelSubjectKind kind) => kind switch
        {
            PanelSubjectKind.Entity => "Entity",
            PanelSubjectKind.Task => "Task",
            PanelSubjectKind.Ability => "Ability",
            _ => "None",
        };

        /// <summary>Entity subject surface available to layout binds (not graph pins).</summary>
        public const string EntityDisplayName = "displayName";
    }
}
