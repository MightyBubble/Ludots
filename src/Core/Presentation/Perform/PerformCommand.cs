using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Perform
{
    /// <summary>
    /// One-shot perform orchestration command.
    /// First-wave implementation bridges directly to the existing performer command contract.
    /// </summary>
    public struct PerformCommand
    {
        public PresentationCommandKind CommandKind;
        public int PerformerDefinitionId;
        public int PerformerHandle;
        public int ScopeId;
        public PerformerCommandScopeSource ScopeSource;
        public PresentationAnchorKind AnchorKind;
        public Entity Source;
        public Entity Target;
        public Vector3 Position;
        public int ParamKey;
        public float ParamValue;
        public int ParamGraphProgramId;

        public static PerformCommand FromLegacy(in PerformerCommand command)
        {
            return new PerformCommand
            {
                CommandKind = command.CommandKind,
                PerformerDefinitionId = command.PerformerDefinitionId,
                PerformerHandle = 0,
                ScopeId = command.ScopeId,
                ScopeSource = command.ScopeSource,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = Entity.Null,
                Target = Entity.Null,
                Position = Vector3.Zero,
                ParamKey = command.ParamKey,
                ParamValue = command.ParamValue,
                ParamGraphProgramId = command.ParamGraphProgramId,
            };
        }

        public readonly PerformerCommand ToLegacy()
        {
            return new PerformerCommand
            {
                CommandKind = CommandKind,
                PerformerDefinitionId = PerformerDefinitionId,
                ScopeId = ScopeId,
                ScopeSource = ScopeSource,
                ParamKey = ParamKey,
                ParamValue = ParamValue,
                ParamGraphProgramId = ParamGraphProgramId,
            };
        }
    }
}
