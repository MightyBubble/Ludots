using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Rendering
{
    public enum PresentationTypedValueKind : byte
    {
        None = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
        Vector4 = 4,
        Color = 5,
        Entity = 6,
        AssetRef = 7,
        StructuredObject = 8,
    }

    public readonly struct PresentationTypedValue
    {
        public PresentationTypedValue(PresentationTypedValueKind kind, int intValue, float floatValue, Vector4 vectorValue, Entity entityValue, string assetKey, string structuredJson = "")
        {
            Kind = kind;
            IntValue = intValue;
            FloatValue = floatValue;
            VectorValue = vectorValue;
            EntityValue = entityValue;
            AssetKey = assetKey ?? string.Empty;
            StructuredJson = structuredJson ?? string.Empty;
        }

        public PresentationTypedValueKind Kind { get; }

        public int IntValue { get; }

        public float FloatValue { get; }

        public Vector4 VectorValue { get; }

        public Entity EntityValue { get; }

        public string AssetKey { get; }

        public string StructuredJson { get; }

        public static PresentationTypedValue FromBool(bool value)
            => new(PresentationTypedValueKind.Bool, value ? 1 : 0, value ? 1f : 0f, default, Entity.Null, string.Empty);

        public static PresentationTypedValue FromInt(int value)
            => new(PresentationTypedValueKind.Int, value, value, default, Entity.Null, string.Empty);

        public static PresentationTypedValue FromFloat(float value)
            => new(PresentationTypedValueKind.Float, 0, value, default, Entity.Null, string.Empty);

        public static PresentationTypedValue FromVector4(Vector4 value)
            => new(PresentationTypedValueKind.Vector4, 0, 0f, value, Entity.Null, string.Empty);

        public static PresentationTypedValue FromColor(Vector4 value)
            => new(PresentationTypedValueKind.Color, 0, 0f, value, Entity.Null, string.Empty);

        public static PresentationTypedValue FromEntity(Entity value)
            => new(PresentationTypedValueKind.Entity, 0, 0f, default, value, string.Empty);

        public static PresentationTypedValue FromAssetRef(string assetKey)
            => new(PresentationTypedValueKind.AssetRef, 0, 0f, default, Entity.Null, assetKey);

        public static PresentationTypedValue FromStructuredObject(string structuredJson)
            => new(PresentationTypedValueKind.StructuredObject, 0, 0f, default, Entity.Null, string.Empty, structuredJson);
    }
}
