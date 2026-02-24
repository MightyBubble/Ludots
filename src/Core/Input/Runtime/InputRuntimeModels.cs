using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;

namespace Ludots.Core.Input.Runtime
{
    public abstract class InputProcessor
    {
        public abstract Vector3 Process(Vector3 value, IReadOnlyList<InputParameterDef> parameters);
    }

    public abstract class InputComposite
    {
        public abstract Vector3 Evaluate(Func<int, Vector3> getPartValue);
    }

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

        public void ClearSuppressed()
        {
            Value = Vector3.Zero;
            Triggered = false;
            PressedThisFrame = false;
            ReleasedThisFrame = false;
            _wasTriggered = false;
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
