using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    /// <summary>
    /// Root payload of <c>Relationships/control_profiles.json</c> (RFC-0065 §5.4 / CTRL-4b).
    /// The schema carries zero business vocabulary: tag and edge names are opaque strings
    /// resolved to ids at load time.
    /// </summary>
    public sealed class AssociationControlProfileCatalogConfig
    {
        public List<AssociationControlProfileConfig> Profiles { get; set; } = new();
    }

    /// <summary>One generic predicate → edge grant/revoke rule.</summary>
    public sealed class AssociationControlProfileConfig
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Predicate that grants the edge while it holds and the edge does not exist.</summary>
        public AssociationControlConditionConfig? When { get; set; }

        /// <summary>The edge to grant between the two declared roles.</summary>
        public AssociationControlGrantConfig? Grant { get; set; }

        /// <summary>Predicate that revokes an edge previously granted by this profile.</summary>
        public AssociationControlConditionConfig? RevokeWhen { get; set; }
    }

    /// <summary><c>from</c>/<c>to</c> are role names; every predicate role reference must be one of them.</summary>
    public sealed class AssociationControlGrantConfig
    {
        public string EdgeType { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    /// <summary>
    /// Predicate node: exactly one of <see cref="All"/>, <see cref="Any"/>, <see cref="Not"/>,
    /// <see cref="Relationship"/> (+<see cref="Between"/>) or <see cref="Tag"/> (+<see cref="On"/>).
    /// </summary>
    public sealed class AssociationControlConditionConfig
    {
        public List<AssociationControlConditionConfig>? All { get; set; }
        public List<AssociationControlConditionConfig>? Any { get; set; }
        public AssociationControlConditionConfig? Not { get; set; }
        public string? Relationship { get; set; }
        public List<string>? Between { get; set; }
        public string? Tag { get; set; }
        public string? On { get; set; }
    }
}
