using System;
using System.Collections.Generic;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// An EntitySpawned → CreatePresenter rule after extends / bindSpawn expansion.
    /// <see cref="Rule"/> is <c>rules[i]</c> or <c>bindSpawn</c>; <see cref="Source"/> is the fragment that last wrote <see cref="OwnerId"/>.
    /// </summary>
    public readonly record struct PresenterSpawnLink(string OwnerId, string Rule, string TemplateId, string DefinitionId, string Source);

    public sealed class PresenterSpawnLinks
    {
        public const string WildcardTemplate = "*";

        public static readonly PresenterSpawnLinks Empty = new(Array.Empty<PresenterSpawnLink>());

        private readonly Dictionary<string, List<PresenterSpawnLink>> _byTemplate = new(StringComparer.Ordinal);
        private readonly List<PresenterSpawnLink> _wildcard = new();

        public PresenterSpawnLinks(IReadOnlyList<PresenterSpawnLink> links)
        {
            All = links ?? throw new ArgumentNullException(nameof(links));
            for (int i = 0; i < links.Count; i++)
            {
                PresenterSpawnLink link = links[i];
                if (string.Equals(link.TemplateId, WildcardTemplate, StringComparison.Ordinal))
                {
                    _wildcard.Add(link);
                    continue;
                }

                if (!_byTemplate.TryGetValue(link.TemplateId, out List<PresenterSpawnLink>? list))
                {
                    list = new List<PresenterSpawnLink>();
                    _byTemplate[link.TemplateId] = list;
                }

                list.Add(link);
            }
        }

        public IReadOnlyList<PresenterSpawnLink> All { get; }

        /// <summary>Rules keyed on this template plus rules keyed on <c>*</c>.</summary>
        public IReadOnlyList<PresenterSpawnLink> For(string templateId)
        {
            bool keyed = _byTemplate.TryGetValue(templateId, out List<PresenterSpawnLink>? list);
            if (_wildcard.Count == 0)
            {
                return keyed ? list! : Array.Empty<PresenterSpawnLink>();
            }

            var result = new List<PresenterSpawnLink>((keyed ? list!.Count : 0) + _wildcard.Count);
            if (keyed)
            {
                result.AddRange(list!);
            }

            result.AddRange(_wildcard);
            return result;
        }
    }
}
