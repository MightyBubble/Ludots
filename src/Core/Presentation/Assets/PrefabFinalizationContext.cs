using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabFinalizationContext
    {
        public static PrefabFinalizationContext Empty { get; } = new(null);

        public PrefabFinalizationContext(IVisualHeightmap? visualHeightmap)
        {
            VisualHeightmap = visualHeightmap;
        }

        public IVisualHeightmap? VisualHeightmap { get; }
    }
}
