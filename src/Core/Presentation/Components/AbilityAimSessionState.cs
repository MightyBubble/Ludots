using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Components
{
    public struct AbilityAimSessionState
    {
        public Entity Actor;
        public Entity Viewer;
        public Entity HoveredEntity;
        public int AbilityId;
        public int ImpactEffectTemplateId;
        public int ActionIdKeyId;
        public int SemanticEventKeyId;
        public byte CurrentInputSlot;
        public byte InputSlotCount;
        public bool IsAiming;
        public bool IsWithinCastRange;
        public bool IsValidPlacement;
        public Vector3 CursorWorldCm;
        public Vector3 OriginWorldCm;
        public float DirectionDeg;
        public uint Revision;
    }
}
