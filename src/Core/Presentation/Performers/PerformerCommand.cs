using Ludots.Core.Presentation.Commands;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// The action part of a <see cref="PerformerRule"/>. When the rule fires,
    /// the PerformerRuleSystem translates this into a <see cref="PresentationCommand"/>
    /// and writes it to the <see cref="PresentationCommandBuffer"/>.
    /// </summary>
    public struct PerformerCommand
    {
        /// <summary>
        /// The PresentationCommandKind to produce.
        /// Maps directly to CreatePerformer / DestroyPerformer / DestroyPerformerScope / SetPerformerParam.
        /// </summary>
        public PresentationCommandKind CommandKind;

        /// <summary>
        /// The PerformerDefinition ID to instantiate (used with CreatePerformer).
        /// </summary>
        public int PerformerDefinitionId;

        /// <summary>
        /// The Scope ID for grouping (used with CreatePerformer / DestroyPerformerScope).
        /// Instances sharing a ScopeId can be destroyed together with a single command.
        /// </summary>
        public int ScopeId;

        /// <summary>
        /// The parameter key for SetPerformerParam.
        /// </summary>
        public int ParamKey;

        /// <summary>
        /// Static parameter value for SetPerformerParam.
        /// </summary>
        public float ParamValue;

        /// <summary>
        /// When > 0, execute this Graph program to compute the parameter value
        /// dynamically instead of using <see cref="ParamValue"/>.
        /// </summary>
        public int ParamGraphProgramId;
    }
}
