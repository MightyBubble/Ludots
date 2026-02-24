namespace Ludots.Core.Modding
{
    public interface IMod
    {
        void OnLoad(IModContext context);
        void OnUnload();
    }
}
