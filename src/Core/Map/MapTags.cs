namespace Ludots.Core.Map
{
    public static class MapTags
    {
        public static readonly MapTag Benchmark = new MapTag("Benchmark");
        public static readonly MapTag Menu = new MapTag("Menu");
        public static readonly MapTag Gameplay = new MapTag("Gameplay");
        public static readonly MapTag Level = new MapTag("Level");
        public static readonly MapTag FeatureNavMeshOn = new MapTag("Feature.NavMesh:On");
        public static readonly MapTag RaylibDeepBackground = new MapTag("Raylib.Background:Deep");
        public static readonly MapTag RaylibHideDebugGuides = new MapTag("Raylib.DebugGuides:Off");
        public static readonly MapTag RaylibHideFieldOverlays = new MapTag("Raylib.FieldOverlays:Off");
    }
}
