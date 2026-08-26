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
    }

    public enum PanelPresentMode : byte
    {
        List = 0,
        Aggregate = 1,
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
            PanelPresentMode present = PanelPresentMode.List)
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

        /// <summary>List control presentation: full rows or aggregate head+count.</summary>
        public PanelPresentMode Present { get; }
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
            _ => "None",
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
    }

    public static class PanelPresentModes
    {
        public static PanelPresentMode Parse(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return PanelPresentMode.List;
            }

            return text.Trim() switch
            {
                "list" => PanelPresentMode.List,
                "aggregate" => PanelPresentMode.Aggregate,
                _ => throw new InvalidOperationException(
                    $"{context} present '{text}' is unknown (allowed: list, aggregate)."),
            };
        }

        public static string ToId(PanelPresentMode mode) => mode switch
        {
            PanelPresentMode.Aggregate => "aggregate",
            _ => "list",
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
            PanelCollectionSourceKind.Input => "input",
            _ => "selfGraph",
        };
    }
}
