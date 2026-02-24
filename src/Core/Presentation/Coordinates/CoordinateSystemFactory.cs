using System;

namespace Ludots.Core.Presentation.Coordinates
{
    public enum PlatformType
    {
        Unity,
        Unreal,
        Godot,
        Web,
        Raylib
    }

    public static class CoordinateSystemFactory
    {
        public static ICoordinateMapper CreateMapper(PlatformType type)
        {
            return type switch
            {
                PlatformType.Unity => new UnityCoordinateMapper(),
                PlatformType.Unreal => new UnrealCoordinateMapper(),
                PlatformType.Godot => new GodotCoordinateMapper(),
                PlatformType.Web => new WebCoordinateMapper(),
                PlatformType.Raylib => new RaylibCoordinateMapper(),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }
    }
}
