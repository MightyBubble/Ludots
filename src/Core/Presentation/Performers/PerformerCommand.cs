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

        public PresentationCommandKind LegacyCommandKind
        {
            readonly get => CommandKind;
            set => CommandKind = value;
        }

        /// <summary>
        /// The PerformerDefinition ID to instantiate (used with CreatePerformer).
        /// </summary>
        public int PerformerDefinitionId;

        /// <summary>
        /// The Scope ID for grouping (used with CreatePerformer / DestroyPerformerScope).
        /// Instances sharing a ScopeId can be destroyed together with a single command.
        /// </summary>
        public int ScopeId;

        public int ScopeTag
        {
            readonly get => ScopeId;
            set => ScopeId = value;
        }

        public int ParentHandle;

        public PerformerCommandScopeSource ScopeSource;

        public int TargetBehaviorSlot;

        /// <summary>
        /// The parameter key for SetPerformerParam.
        /// </summary>
        public int ParamKey;

        /// <summary>
        /// Static parameter value for SetPerformerParam.
        /// </summary>
        public float ParamValue;

        public ParamLane ParamLane;

        public int IntValue;

        public System.Numerics.Vector4 VectorValue;

        /// <summary>
        /// When > 0, execute this Graph program to compute the parameter value
        /// dynamically instead of using <see cref="ParamValue"/>.
        /// </summary>
        public int ParamGraphProgramId;
    }

    public enum PerformerCommandScopeSource : byte
    {
        Fixed = 0,
        EventPayloadA = 1,
        EventPayloadB = 2,
        SourceStableId = 3,
    }
}
