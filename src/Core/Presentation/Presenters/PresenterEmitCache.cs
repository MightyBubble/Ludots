using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public struct PresenterEmitCache
    {
        public int CachedVersion;
        public int RetainedWorldHudStableId;
        public Vector3 LastEmitPosition;
        public Vector3 LastRetainedHudScale;
        public byte LastOwnerCullVisible;
        public byte StableVisualPresent;
        public byte RetainedRequestPresent;
        public byte StaticDirty;
        public byte RetainedDirty;
        public byte RetainedPositionOnlyDirty;
        public LODLevel LastLod;
    }
}
