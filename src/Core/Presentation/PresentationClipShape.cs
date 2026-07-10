namespace Ludots.Core.Presentation
{
    public enum PresentationClipShapeKind : byte
    {
        None = 0,
        Rect = 1,
        Circle = 2,
        Diamond = 3,
    }

    public readonly record struct PresentationClipShape(
        PresentationClipShapeKind Kind,
        float X,
        float Y,
        float Width,
        float Height)
    {
        public static PresentationClipShape None => default;

        public bool IsActive => Kind != PresentationClipShapeKind.None &&
            Width > 0f &&
            Height > 0f;

        public static PresentationClipShape FromRect(float x, float y, float width, float height)
        {
            return Create(PresentationClipShapeKind.Rect, x, y, width, height);
        }

        public static PresentationClipShape FromCircle(float x, float y, float width, float height)
        {
            return Create(PresentationClipShapeKind.Circle, x, y, width, height);
        }

        public static PresentationClipShape FromDiamond(float x, float y, float width, float height)
        {
            return Create(PresentationClipShapeKind.Diamond, x, y, width, height);
        }

        public static PresentationClipShape Create(
            PresentationClipShapeKind kind,
            float x,
            float y,
            float width,
            float height)
        {
            if (kind == PresentationClipShapeKind.None || width <= 0f || height <= 0f)
            {
                return None;
            }

            return new PresentationClipShape(kind, x, y, width, height);
        }
    }
}
