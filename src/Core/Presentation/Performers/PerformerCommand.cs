using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// The action part of a <see cref="PerformerRule"/> and the single runtime command DTO
    /// consumed by the performer pipeline.
    /// </summary>
    public struct PerformerCommand
    {
        public PerformerCommand()
        {
            PerformerHandle = -1;
            ParentHandle = -1;
            TargetBehaviorSlot = -1;
        }

        public PerformerCommandKind CommandKind;
        public int PerformerDefinitionId;
        public int PerformerHandle;
        public int ParentHandle;
        public int ScopeTag;
        public PerformerCommandScopeSource ScopeSource;
        public PresentationAnchorKind AnchorKind;
        public Entity Source;
        public Entity Target;
        public Vector3 Position;
        public int ParamKey;
        public ParamLane ParamLane;
        public float ParamValue;
        public int IntValue;
        public Vector4 VectorValue;
        public int ParamGraphProgramId;
        public int TargetBehaviorSlot;
    }
}
