namespace Ludots.Core.Presentation.Rendering
{
    public static class TransientMarkerIdentity
    {
        private const int NamespacePrefix = 0x40000000;

        public static int ComposeStableId(int sequence)
        {
            int normalized = sequence & 0x3FFFFFFF;
            int stableId = NamespacePrefix | normalized;
            return stableId <= 0 ? 1 : stableId;
        }
    }
}
