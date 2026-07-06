using System;
using System.Numerics;
using Ludots.Core.Input.Runtime;

namespace Ludots.Core.Input.Automation
{
    public sealed class InputAutomationBackend : IInputBackend, IFrameSynchronizedInputBackend
    {
        private readonly IInputBackend _inner;
        private readonly IFrameSynchronizedInputBackend? _synchronizedInner;
        private readonly InputAutomationPlayer _player;

        public InputAutomationBackend(IInputBackend inner, InputAutomationPlayer player)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _synchronizedInner = inner as IFrameSynchronizedInputBackend;
            _player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public IInputBackend Inner => _inner;

        public InputAutomationPlayer Player => _player;

        public void AdvanceFrameInput()
        {
            _synchronizedInner?.AdvanceFrameInput();
            if (!_player.UsesExternalFrameClock)
            {
                _player.AdvanceFrame();
            }
        }

        public float GetAxis(string devicePath)
        {
            if (IsMouseScrollPath(devicePath))
            {
                return GetMouseWheel();
            }

            return _inner.GetAxis(devicePath);
        }

        public bool GetButton(string devicePath)
        {
            return _inner.GetButton(devicePath) ||
                (_player.TryGetButton(devicePath, out bool isDown) && isDown);
        }

        public Vector2 GetMousePosition()
        {
            return _player.HasPointerPosition
                ? _player.PointerPosition
                : _inner.GetMousePosition();
        }

        public float GetMouseWheel()
        {
            return _inner.GetMouseWheel() + _player.MouseWheel;
        }

        public void EnableIME(bool enable) => _inner.EnableIME(enable);

        public void SetIMECandidatePosition(int x, int y) => _inner.SetIMECandidatePosition(x, y);

        public string GetCharBuffer()
        {
            string inner = _inner.GetCharBuffer();
            string automation = _player.ConsumeCharBuffer();
            if (string.IsNullOrEmpty(inner))
            {
                return automation;
            }

            return string.IsNullOrEmpty(automation) ? inner : inner + automation;
        }

        private static bool IsMouseScrollPath(string? devicePath)
        {
            return !string.IsNullOrWhiteSpace(devicePath) &&
                devicePath.Contains("<Mouse>/Scroll", StringComparison.OrdinalIgnoreCase);
        }
    }
}
