namespace Ludots.UI.Runtime;

public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    public static UiThickness Zero => new(0f, 0f, 0f, 0f);

    public static UiThickness All(float value)
    {
        return new UiThickness(value, value, value, value);
    }

    public static UiThickness Symmetric(float horizontal, float vertical)
    {
        return new UiThickness(horizontal, vertical, horizontal, vertical);
    }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;
}
