using UnityEngine;

namespace Sango
{
    public interface IMap
    {
        bool QueryHeight(Vector3 pos, out float height);
        float cameraDistanceFactor { get; }
    }
}
