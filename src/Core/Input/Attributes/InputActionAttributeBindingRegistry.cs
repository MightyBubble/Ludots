using System;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Input.Attributes
{
    public enum InputActionAttributeValueKind : byte
    {
        Axis1D = 0,
        Axis2D = 1,
        Button = 2,
        Constant = 3
    }

    public enum InputActionAttributeTargetKind : byte
    {
        LocalPlayerEntity = 0,
        CameraBehaviorInput = 1
    }

    public readonly struct InputActionAttributeBindingEntry
    {
        public InputActionAttributeBindingEntry(
            string actionId,
            int attributeId,
            InputActionAttributeValueKind valueKind,
            byte sourceChannel,
            InputActionAttributeTargetKind target,
            float scale,
            bool zeroWhenUiCaptured,
            bool suppressOnUiWheelCaptured,
            bool preserveValueUntilSnapshot)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("Action id is required.", nameof(actionId));
            }

            ActionId = actionId;
            AttributeId = attributeId;
            ValueKind = valueKind;
            SourceChannel = sourceChannel;
            Target = target;
            Scale = scale;
            ZeroWhenUiCaptured = zeroWhenUiCaptured;
            SuppressOnUiWheelCaptured = suppressOnUiWheelCaptured;
            PreserveValueUntilSnapshot = preserveValueUntilSnapshot;
        }

        public string ActionId { get; }
        public int AttributeId { get; }
        public InputActionAttributeValueKind ValueKind { get; }
        public byte SourceChannel { get; }
        public InputActionAttributeTargetKind Target { get; }
        public float Scale { get; }
        public bool ZeroWhenUiCaptured { get; }
        public bool SuppressOnUiWheelCaptured { get; }
        public bool PreserveValueUntilSnapshot { get; }
    }

    public sealed class InputActionAttributeBindingRegistry
    {
        private InputActionAttributeBindingEntry[] _entries = Array.Empty<InputActionAttributeBindingEntry>();

        public InputActionAttributeBindingEntry[] Entries => _entries;

        public void Clear()
        {
            _entries = Array.Empty<InputActionAttributeBindingEntry>();
        }

        public void Set(InputActionAttributeBindingEntry[] entries)
        {
            _entries = entries ?? Array.Empty<InputActionAttributeBindingEntry>();
        }

        public bool ContainsAttribute(string attribute)
        {
            int attributeId = AttributeRegistry.Register(attribute);
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].AttributeId == attributeId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
