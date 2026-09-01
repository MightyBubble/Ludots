using System.Numerics;
using Ludots.Core.Input.Config;

namespace Ludots.Core.Input.Runtime
{
    public class InputActionInstance
    {
        public InputActionDef Definition { get; }
        
        public Vector3 Value { get; private set; }
        public bool Triggered { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public float Magnitude => Value.Length();

        private bool _wasTriggered;

        public InputActionInstance(InputActionDef def)
        {
            Definition = def;
        }

        public void Update(Vector3 rawValue)
        {
            Value = rawValue;
            Triggered = Magnitude > 0.001f;
            PressedThisFrame = Triggered && !_wasTriggered;
            ReleasedThisFrame = !Triggered && _wasTriggered;
            _wasTriggered = Triggered;
        }

        public void SuppressThisFrame()
        {
            Value = Vector3.Zero;
            Triggered = false;
            PressedThisFrame = false;
            ReleasedThisFrame = false;
        }
        
        public T ReadValue<T>() where T : struct
        {
            if (typeof(T) == typeof(bool)) return (T)(object)(Magnitude > 0.5f);
            if (typeof(T) == typeof(float)) return (T)(object)Value.X;
            if (typeof(T) == typeof(Vector2)) return (T)(object)new Vector2(Value.X, Value.Y);
            if (typeof(T) == typeof(Vector3)) return (T)(object)Value;
            return default;
        }
    }
}
