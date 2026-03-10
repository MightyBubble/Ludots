namespace Ludots.Core.Input.Runtime
{
    public interface IInputActionReader
    {
        T ReadAction<T>(string actionId) where T : struct;
        bool IsDown(string actionId);
        bool PressedThisFrame(string actionId);
        bool ReleasedThisFrame(string actionId);
    }
}
