using System;
using System.Numerics;

namespace Ludots.Core.Input.Runtime.Composites
{
    public class Vector2Composite : InputComposite
    {
        // Expected parts: 0=Up(Y+), 1=Down(Y-), 2=Left(X-), 3=Right(X+)
        public override Vector3 Evaluate(Func<int, Vector3> getPartValue)
        {
            float up = getPartValue(0).X;
            float down = getPartValue(1).X;
            float left = getPartValue(2).X;
            float right = getPartValue(3).X;

            return new Vector3(right - left, up - down, 0);
        }
    }
}
