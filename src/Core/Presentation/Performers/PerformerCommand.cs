using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Rendering;

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
        /// The performer instance handle targeted by imperative update/destroy commands.
        /// </summary>
        public int PerformerHandle;

        /// <summary>
        /// The Scope ID for grouping (used with CreatePerformer / DestroyPerformerScope).
        /// Instances sharing a ScopeId can be destroyed together with a single command.
        /// </summary>
        public int ScopeId;

        /// <summary>
        /// Named semantic field for SetPerformerField.
        /// </summary>
        public string FieldName;

        /// Static typed field value for SetPerformerField.
        /// </summary>
        public PresentationTypedValue FieldValue;

        /// The legacy parameter key for SetPerformerParam.
        /// </summary>
        public int LegacyParamKey;

        /// <summary>
        /// Static parameter value for SetPerformerParam.
        /// </summary>
        public float LegacyParamValue;

        /// <summary>
        /// When > 0, execute this Graph program to compute the parameter value
        /// dynamically instead of using <see cref="LegacyParamValue"/>.
        /// </summary>
        public int LegacyParamGraphProgramId;
    }
}
