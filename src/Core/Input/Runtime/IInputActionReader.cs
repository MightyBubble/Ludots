namespace Ludots.Core.Input.Runtime
{
    public interface IInputActionReader
    {
        T ReadAction<T>(string actionId) where T : struct;
    }
}
