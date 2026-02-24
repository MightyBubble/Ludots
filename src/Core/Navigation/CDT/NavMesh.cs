using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.CDT
{
    public struct NavPoly
    {
        public int PolyId;
        // Indices into the vertex array of the mesh
        public int V1, V2, V3; 
        public Vector3 Centroid;
        public int Neighbor1, Neighbor2, Neighbor3; // -1 if none
    }

    public class NavMesh
    {
        public List<Vector3> Vertices = new List<Vector3>();
        public List<NavPoly> Polys = new List<NavPoly>();

        public void Clear()
        {
            Vertices.Clear();
            Polys.Clear();
        }
    }

    public interface INavMeshPositionProvider
    {
        Vector3 GetVertexPosition(int col, int row, float heightScale);
        bool TryGetCliffSplit(int cA, int rA, int cB, int rB, float heightScale, out Vector3 highExt, out Vector3 lowExt);
    }

    public interface INavMeshBuilder
    {
        NavMesh Build(VertexMap map, INavMeshPositionProvider positions);
    }
}
