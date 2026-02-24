using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;

namespace Ludots.Core.Input.Runtime.Processors
{
    public class NormalizeProcessor : InputProcessor
    {
        public override Vector3 Process(Vector3 value, IReadOnlyList<InputParameterDef> parameters)
        {
            if (value.LengthSquared() > 1.0f)
            {
                return Vector3.Normalize(value);
            }
            return value;
        }
    }

    public class ScaleProcessor : InputProcessor
    {
        public override Vector3 Process(Vector3 value, IReadOnlyList<InputParameterDef> parameters)
        {
            float factor = 1.0f;
            foreach (var p in parameters)
            {
                if (p.Name == "Factor") factor = p.Value;
            }
            return value * factor;
        }
    }
    
    public class DeadzoneProcessor : InputProcessor
    {
        public override Vector3 Process(Vector3 value, IReadOnlyList<InputParameterDef> parameters)
        {
            float min = 0.1f;
            foreach (var p in parameters)
            {
                if (p.Name == "Min") min = p.Value;
            }
            
            if (value.Length() < min) return Vector3.Zero;
            return value;
        }
    }
}
