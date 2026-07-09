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
            TargetBehaviorSlot = -1;
        }

        public PerformerCommandKind CommandKind;
        public int CommandKindId;
        public PerformerCommandRouteStrategy RouteStrategy;
        public int PerformerDefinitionId;
        public Entity PerformerEntity;
        public Entity ParentEntity;
        public int ScopeTag;
        public PerformerCommandScopeSource ScopeSource;
        public PresentationAnchorKind AnchorKind;
        public Entity Source;
        public Entity Target;
        public Entity Viewer;
        public PerformerCommandEntitySource OwnerSource;
        public Vector3 Position;
        public bool UseEventPosition;
        public bool HasParamPayload;
        public int ParamKey;
        public ParamLane ParamLane;
        public float ParamValue;
        public int IntValue;
        public Vector4 VectorValue;
        public PerformerCommandValueSource ValueSource;
        public PerformerCommandValueSource VectorXSource;
        public PerformerCommandValueSource VectorYSource;
        public PerformerCommandValueSource VectorZSource;
        public PerformerCommandValueSource VectorWSource;
        public int ParamGraphProgramId;
        public int TargetBehaviorSlot;
    }
}
