using System.Collections.Generic;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// The four preference scopes of the ClientCastPreference chain (RFC-0065 CTX-8, §5.6).
    /// The chain is a fixed structural spec — resolution order is always
    /// perSlot &gt; perFormSet &gt; perTemplate &gt; global, with mod locks overriding every player layer.
    /// </summary>
    public enum CastPreferenceScope : byte
    {
        /// <summary>Applies to every slot not covered by a deeper scope.</summary>
        Global = 0,

        /// <summary>Applies to every slot of one entity template.</summary>
        PerTemplate = 1,

        /// <summary>Applies to every slot while one ability form set is active.</summary>
        PerFormSet = 2,

        /// <summary>Applies to one (template, slot index) pair.</summary>
        PerSlot = 3,
    }

    /// <summary>
    /// Scope names used by <c>Input/cast_commit_locks.json</c> lock declarations (RFC-0065 §5.6).
    /// </summary>
    public static class CastPreferenceScopeNames
    {
        public const string Global = "global";
        public const string Template = "template";
        public const string FormSet = "formSet";
        public const string Slot = "slot";
    }

    /// <summary>
    /// Resolves (and registers, idempotently) a scope key string to its int id. Bound by wiring to
    /// the authoritative id space: template keys resolve through <c>EntityTemplateKeyRegistry</c>,
    /// form set keys through <c>AbilityFormSetIdRegistry</c>.
    /// </summary>
    public delegate int PreferenceScopeKeyResolver(string key);

    /// <summary>Reverse lookup of a scope key id to its string name (persistence writes names, never ids).</summary>
    public delegate string PreferenceScopeKeyName(int id);

    /// <summary>Merged root of <c>Input/cast_commit_locks.json</c> (mod-declared lock set).</summary>
    public sealed class CastCommitLocksConfig
    {
        public List<CastCommitLockDefinition> Locks { get; set; }
    }

    /// <summary>
    /// One mod lock: pins the cast commit profile at a scope so player preferences cannot override
    /// it (RFC-0065 §5.6 <c>lockedCastCommitId</c> semantics). <see cref="Key"/> format per scope:
    /// <c>global</c> — empty; <c>template</c> — template key; <c>formSet</c> — form set name;
    /// <c>slot</c> — <c>&lt;templateKey&gt;/&lt;slotIndex&gt;</c>.
    /// </summary>
    public sealed class CastCommitLockDefinition
    {
        public string Scope { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string CastCommitId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Persisted player preference file (RFC-0065 §5.6 shape plus the active control scheme id from
    /// DEC-15). Keys are string names — ids are opaque runtime handles and never persist.
    /// </summary>
    public sealed class ClientCastPreferenceFile
    {
        public CastPreferenceEntry Global { get; set; }
        public Dictionary<string, CastPreferenceEntry> PerTemplate { get; set; }
        public Dictionary<string, CastPreferenceEntry> PerFormSet { get; set; }
        public Dictionary<string, CastPreferenceEntry> PerSlot { get; set; }
        public string ActiveSchemeId { get; set; }
    }

    /// <summary>One preference value; an empty/absent cast commit id declares no override at that key.</summary>
    public sealed class CastPreferenceEntry
    {
        public string CastCommitId { get; set; }
    }
}
