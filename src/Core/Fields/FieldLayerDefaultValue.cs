using System.Numerics;

namespace Ludots.Core.Fields
{
    /// <summary>
    /// Validated field default value. Only the member matching the layer kind carries data:
    /// <see cref="Scalar"/> for scalar32, <see cref="Vector2"/> for vector2, <see cref="Vector3"/> for vector3.
    /// discreteId layers carry no payload: their default is region id 0, the reserved "no region" id.
    /// </summary>
    public readonly struct FieldLayerDefaultValue
    {
        public FieldLayerDefaultValue(float scalar, Vector2 vector2, Vector3 vector3)
        {
            Scalar = scalar;
            Vector2 = vector2;
            Vector3 = vector3;
        }

        public static FieldLayerDefaultValue None => default;

        public static FieldLayerDefaultValue Scalar32(float value) => new(value, default, default);

        public static FieldLayerDefaultValue Vector2Value(Vector2 value) => new(0f, value, default);

        public static FieldLayerDefaultValue Vector3Value(Vector3 value) => new(0f, default, value);

        public readonly float Scalar;
        public readonly Vector2 Vector2;
        public readonly Vector3 Vector3;
    }
}
