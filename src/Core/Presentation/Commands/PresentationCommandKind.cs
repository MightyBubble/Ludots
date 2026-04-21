namespace Ludots.Core.Presentation.Commands
{
    public enum PresentationCommandKind : byte
    {
        None = 0,
        PlayOneShotPerformer = 1,

        // ── Persistent performer lifecycle commands ──
        /// <summary>Create a persistent performer instance.</summary>
        CreatePerformer = 10,
        /// <summary>Destroy a single performer instance.</summary>
        DestroyPerformer = 11,
        /// <summary>Destroy all instances in a scope.</summary>
        DestroyPerformerScope = 12,
        /// <summary>Update a legacy performer parameter override.</summary>
        SetPerformerParam = 13,
        /// <summary>Update a named typed performer field override.</summary>
        SetPerformerField = 14,
    }
}
