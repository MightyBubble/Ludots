using Ludots.Core.Engine;

namespace RtsDemoMod.Runtime
{
    public static class RtsDemoRuntimeKeys
    {
        public const string SuppressNativeCommandPanels = "RtsDemoMod.SuppressNativeCommandPanels";

        public static bool AreNativeCommandPanelsSuppressed(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(SuppressNativeCommandPanels, out object? value) &&
                   value is bool suppressed &&
                   suppressed;
        }
    }
}
