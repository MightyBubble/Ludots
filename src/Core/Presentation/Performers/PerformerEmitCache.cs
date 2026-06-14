using System.Numerics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public struct PerformerEmitCache
    {
        public int CachedVersion;
        public Vector3 LastEmitPosition;
        public byte LastOwnerCullVisible;
        public byte LastDefinitionVisible;
        public byte StableVisualPresent;
        public byte RetainedRequestPresent;
        public byte StaticDirty;
        public byte RetainedDirty;
        public LODLevel LastLod;
    }
}
