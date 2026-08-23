using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Built-in association expand kinds (RFC-0065 §5.2). Registry keys, not a closed enum.</summary>
    public static class FilterAssociationExpandKinds
    {
        /// <summary>Anchor's control-plane reachable set (owns subtree + Controls grants).</summary>
        public const string Controls = "controls";

        /// <summary>No association filtering; only tag rules apply.</summary>
        public const string None = "none";
    }

    /// <summary>Built-in anchor kinds for <see cref="FilterProfileAssociationQuery.Anchor"/>.</summary>
    public static class FilterAnchorKinds
    {
        /// <summary>The sole possessed rep entity, supplied by the caller at evaluation time.</summary>
        public const string SolePossessedRep = "solePossessedRep";
    }

    /// <summary>
    /// Association provider contract (RFC-0065 DEC-8): expands the entity set reachable from an anchor.
    /// Implementations are injected by the control plane; the filter registry owns only the contract.
    /// Returns the number of entities written; a full buffer signals possible truncation and the
    /// caller retries with a larger buffer.
    /// </summary>
    public delegate int FilterAssociationExpander(Entity anchorRep, Span<Entity> buffer);

    /// <summary>Merged root of <c>Input/filter_profiles.json</c>.</summary>
    public sealed class FilterProfilesConfig
    {
        public List<FilterProfileDefinition> Profiles { get; set; }
    }

    /// <summary>One filter profile declaration (RFC-0065 §5.2). Strings live only in JSON.</summary>
    public sealed class FilterProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public FilterProfileAssociationQuery AssociationQuery { get; set; }
        public FilterProfileTagRule Exclude { get; set; }
        public FilterProfileTagRule Include { get; set; }
    }

    /// <summary>Association expansion declaration: which anchor, and which registered expand kind.</summary>
    public sealed class FilterProfileAssociationQuery
    {
        public string Anchor { get; set; } = string.Empty;
        public string Expand { get; set; } = string.Empty;
    }

    /// <summary>Any-of tag rule; tag names are resolved to ids at install time.</summary>
    public sealed class FilterProfileTagRule
    {
        public List<string> AnyTags { get; set; } = new();
    }
}
