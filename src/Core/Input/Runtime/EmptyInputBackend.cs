using System.Numerics;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Zero-reading <see cref="IInputBackend"/> for engines without input hardware (headless
    /// runs, servers, test hosts). Every device read reports neutral state so per-seat input
    /// handlers stay constructible and pumpable while real input arrives through handler-level
    /// injection or a host-bound backend.
    /// </summary>
    public sealed class EmptyInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => new(-1f, -1f);
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
