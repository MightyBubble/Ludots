using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Commands
{
    public struct PresentationCommand
    {
        public int LogicTickStamp;
        public PresentationCommandKind Kind;
        public PresentationAnchorKind AnchorKind;

        public int PrefabId;
        public int PerformerDefinitionId;
        public int PerformerHandle;
        public int ScopeId;

        public Entity Source;
        public Entity Target;

        public Vector3 Position;

        public Vector4 Color;
        public float LifetimeSeconds;

        public string FieldName;
        public PresentationTypedValue FieldValue;

        public int LegacyParamKey;
        public float LegacyParamValue;
    }
}
