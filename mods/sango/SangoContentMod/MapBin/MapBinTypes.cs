namespace Sango.Content.MapBin;

/// <summary>
/// Persisted RGB color for the Unity <c>Color</c> slots of DefaultMap.bin. The stream only ever
/// carries three float channels (light/fog colors); alpha is never serialized.
/// </summary>
public readonly record struct SangoRgbF(float R, float G, float B);

/// <summary>
/// Persisted RGB color for the Unity <c>Color32</c> slots of DefaultMap.bin (map labels). The
/// stream carries three bytes; alpha is never serialized (Unity reloads it as 255).
/// </summary>
public readonly record struct SangoRgb32(byte R, byte G, byte B);

/// <summary>Persisted equivalent of Unity <c>Rect</c> (skybox sky areas): four floats.</summary>
public readonly record struct SangoRectF(float X, float Y, float Width, float Height);
