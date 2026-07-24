using Ludots.Core.Modding;

namespace DepApiMod
{
    public interface IDepApiMarker
    {
        string Ping();
    }

    public sealed class DepApiMarker : IDepApiMarker
    {
        public string Ping() => "pong";
    }

    public class DepApiModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("DepApiMod Loaded!");
        }

        public void OnUnload()
        {
        }
    }
}
