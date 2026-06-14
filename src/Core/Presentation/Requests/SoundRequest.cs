using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Requests
{
    public struct SoundRequest
    {
        public SoundRequestKind Kind;
        public int StableId;
        public int SoundAssetId;
        public bool Loop;
        public float Volume;
        public Vector3 WorldPosition;
        public Entity Owner;
    }
}
