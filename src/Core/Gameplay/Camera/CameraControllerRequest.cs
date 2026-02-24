namespace Ludots.Core.Gameplay.Camera
{
    public static class CameraControllerIds
    {
        public const string Orbit3C = "Orbit3C";
    }

    public sealed class CameraControllerRequest
    {
        public string Id { get; set; } = "";
        public object? Config { get; set; }
    }

    public sealed class Orbit3CCameraConfig
    {
        public bool EnablePan { get; set; } = true;

        public string MoveActionId { get; set; } = "Move";
        public string ZoomActionId { get; set; } = "Zoom";
        public string PointerPosActionId { get; set; } = "PointerPos";
        public string RotateHoldActionId { get; set; } = "OrbitRotateHold";
        public string RotateLeftActionId { get; set; } = "RotateLeft";
        public string RotateRightActionId { get; set; } = "RotateRight";

        public float RotateDegPerPixel { get; set; } = 0.28f;
        public float ZoomCmPerWheel { get; set; } = 2000f;
        public float PanCmPerSecond { get; set; } = 6000f;
        public float RotateDegPerSecond { get; set; } = 90f;

        public float MinPitchDeg { get; set; } = 10f;
        public float MaxPitchDeg { get; set; } = 85f;

        public float MinDistanceCm { get; set; } = 500f;
        public float MaxDistanceCm { get; set; } = 200000f;
    }
}
