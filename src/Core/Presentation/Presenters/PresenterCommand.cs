using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// The action part of a <see cref="PresenterRule"/> and the single runtime command DTO
    /// consumed by the presenter pipeline.
    /// </summary>
    public struct PresenterCommand
    {
        public PresenterCommand()
        {
            TargetBehaviorSlot = -1;
        }

        public PresenterCommandKind CommandKind;
        public int CommandKindId;
        public PerformerCommandRouteStrategy RouteStrategy;
        public int PresenterDefinitionId;
        public Entity PresenterEntity;
        public Entity ParentEntity;
        public int ScopeTag;
        public PresenterCommandScopeSource ScopeSource;
        public PresentationAnchorKind AnchorKind;
        public Entity Source;
        public Entity Target;
        public Entity Viewer;
        public PresenterCommandEntitySource OwnerSource;
        public Vector3 Position;
        public bool UseEventPosition;
        public bool HasParamPayload;
        public int ParamKey;
        public ParamLane ParamLane;
        public float ParamValue;
        public int IntValue;
        public Vector4 VectorValue;
        public PresenterCommandValueSource ValueSource;
        public PresenterCommandValueSource VectorXSource;
        public PresenterCommandValueSource VectorYSource;
        public PresenterCommandValueSource VectorZSource;
        public PresenterCommandValueSource VectorWSource;
        public int ParamGraphProgramId;
        public int TargetBehaviorSlot;
        public int TimerNameId;
        public float TimerDurationSeconds;
        public float TimerDurationRangeSeconds;
    }
}
