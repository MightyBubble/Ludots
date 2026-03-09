namespace Ludots.UI.Runtime;

public sealed class UiTransform : IEquatable<UiTransform>
{
    private readonly UiTransformOperation[] _operations;

    public static UiTransform Identity { get; } = new(Array.Empty<UiTransformOperation>());

    public UiTransform(IEnumerable<UiTransformOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations.ToArray();
    }

    public IReadOnlyList<UiTransformOperation> Operations => _operations;

    public bool HasOperations => _operations.Length > 0;

    public UiTransform Append(UiTransformOperation operation)
    {
        UiTransformOperation[] next = new UiTransformOperation[_operations.Length + 1];
        Array.Copy(_operations, next, _operations.Length);
        next[^1] = operation;
        return new UiTransform(next);
    }

    public bool Equals(UiTransform? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other == null || _operations.Length != other._operations.Length)
        {
            return false;
        }

        for (int i = 0; i < _operations.Length; i++)
        {
            if (_operations[i] != other._operations[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is UiTransform other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        for (int i = 0; i < _operations.Length; i++)
        {
            hash.Add(_operations[i]);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return _operations.Length == 0
            ? "none"
            : string.Join(' ', _operations.Select(static operation => operation.ToString()));
    }
}

public readonly record struct UiTransformOperation(
    UiTransformOperationKind Kind,
    UiLength XLength,
    UiLength YLength,
    float ScaleX,
    float ScaleY,
    float AngleDegrees)
{
    public static UiTransformOperation Translate(UiLength x, UiLength y)
    {
        return new UiTransformOperation(UiTransformOperationKind.Translate, x, y, 1f, 1f, 0f);
    }

    public static UiTransformOperation Scale(float x, float y)
    {
        return new UiTransformOperation(UiTransformOperationKind.Scale, UiLength.Auto, UiLength.Auto, x, y, 0f);
    }

    public static UiTransformOperation Rotate(float angleDegrees)
    {
        return new UiTransformOperation(UiTransformOperationKind.Rotate, UiLength.Auto, UiLength.Auto, 1f, 1f, angleDegrees);
    }

    public override string ToString()
    {
        return Kind switch
        {
            UiTransformOperationKind.Translate => $"translate({XLength}, {YLength})",
            UiTransformOperationKind.Scale when Math.Abs(ScaleX - ScaleY) < 0.001f => $"scale({ScaleX:0.###})",
            UiTransformOperationKind.Scale => $"scale({ScaleX:0.###}, {ScaleY:0.###})",
            UiTransformOperationKind.Rotate => $"rotate({AngleDegrees:0.###}deg)",
            _ => string.Empty
        };
    }
}

public enum UiTransformOperationKind : byte
{
    Translate = 0,
    Scale = 1,
    Rotate = 2
}
