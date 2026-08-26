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
