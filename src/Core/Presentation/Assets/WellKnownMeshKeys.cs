using System;

namespace Ludots.Core.Presentation.Assets
{
    public static class WellKnownMeshKeys
    {
        public const string Cube = "cube";
        public const string Sphere = "sphere";
        public const string CueMarker = "cue_marker";

        public static int RequireCueMarkerId(MeshAssetRegistry meshes)
        {
            ArgumentNullException.ThrowIfNull(meshes);
            int id = meshes.GetId(CueMarker);
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    $"Mesh asset '{CueMarker}' is required for transient cue markers. Author it in Presentation/mesh_assets.json.");
            }

            return id;
        }
    }
}
