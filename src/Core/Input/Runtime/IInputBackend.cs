using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    public interface IInputBackend
    {
        float GetAxis(string devicePath);
        bool GetButton(string devicePath);
        Vector2 GetMousePosition();
        float GetMouseWheel();
        
        // IME Support
        void EnableIME(bool enable);
        void SetIMECandidatePosition(int x, int y);
        string GetCharBuffer(); // For text input
    }
}
