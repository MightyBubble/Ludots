using System.Numerics;

namespace Ludots.Core.Presentation.Performers
{
    public enum PerformerCommandKind : byte
    {
        None = 0,
        CreatePerformer = 1,
        DestroyPerformer = 2,
        DestroyPerformerScope = 3,
        SetParam = 4,
        SetParamDefaults = 5,
        ActivateBehavior = 6,
        DeactivateBehavior = 7,
    }

    public enum PerformerCommandScopeSource : byte
    {
        Fixed = 0,
        EventPayloadA = 1,
        EventPayloadB = 2,
        SourceStableId = 3,
        TargetStableId = 4,
    }

    /// <summary>
    /// The action part of a <see cref="PerformerRule"/>. When the rule fires,
    /// the PerformerRuleSystem translates this into an adapter-neutral presentation command.
    /// </summary>
    public struct PerformerCommand
    {
        public PerformerCommandKind CommandKind;

        /// <summary>
        /// The PerformerDefinition ID to instantiate (used with CreatePerformer).
        /// </summary>
        public int PerformerDefinitionId;

        public int ParentHandle;

        public int ScopeTag;

        public PerformerCommandScopeSource ScopeSource;

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

        public Vector4 VectorValue;

        /// <summary>
        /// When > 0, execute this Graph program to compute the parameter value
        /// dynamically instead of using <see cref="ParamValue"/>.
        /// </summary>
        public int ParamGraphProgramId;

        public int TargetBehaviorSlot;
    }
}
