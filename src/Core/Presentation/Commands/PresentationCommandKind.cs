namespace Ludots.Core.Presentation.Commands
{
    public enum PresentationCommandKind : byte
    {
        None = 0,
        PlayOneShotPerformer = 1,

        // ── Persistent performer lifecycle commands ──
        /// <summary>Create a persistent performer instance (IdA=DefId, IdB=ScopeId, Source=Owner).</summary>
        CreatePerformer = 10,
        /// <summary>Destroy a single performer instance (IdA=Handle).</summary>
        DestroyPerformer = 11,
        /// <summary>Destroy all instances in a scope (IdA=ScopeId).</summary>
        DestroyPerformerScope = 12,
        /// <summary>Update a performer parameter override (IdA=Handle, IdB=ParamKey, Param1=Value).</summary>
        SetPerformerParam = 13,
    }
}
