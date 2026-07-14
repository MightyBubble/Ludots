using System;
using System.Numerics;
using Ludots.Core.Gameplay.GAS.Bindings;

namespace Ludots.Core.Gameplay.Camera
{
    public static class CameraBehaviorAttributes
    {
        public const string MoveX = "Camera.Behavior.MoveX";
        public const string MoveY = "Camera.Behavior.MoveY";
        public const string PointerX = "Camera.Behavior.PointerX";
        public const string PointerY = "Camera.Behavior.PointerY";
        public const string PointerActive = "Camera.Behavior.PointerActive";
        public const string PointerDeltaX = "Camera.Behavior.PointerDeltaX";
        public const string PointerDeltaY = "Camera.Behavior.PointerDeltaY";
        public const string LookX = "Camera.Behavior.LookX";
        public const string LookY = "Camera.Behavior.LookY";
        public const string Zoom = "Camera.Behavior.Zoom";
        public const string RotateHold = "Camera.Behavior.RotateHold";
        public const string RotateLeft = "Camera.Behavior.RotateLeft";
        public const string RotateRight = "Camera.Behavior.RotateRight";
        public const string GrabDragHold = "Camera.Behavior.GrabDragHold";
        public const string FollowHold = "Camera.Behavior.FollowHold";
    }

    public static class CameraBehaviorInputChannels
    {
        public const byte MoveX = 0;
        public const byte MoveY = 1;
        public const byte PointerX = 2;
        public const byte PointerY = 3;
        public const byte PointerDeltaX = 4;
        public const byte PointerDeltaY = 5;
        public const byte LookX = 6;
        public const byte LookY = 7;
        public const byte Zoom = 8;
        public const byte RotateHold = 9;
        public const byte RotateLeft = 10;
        public const byte RotateRight = 11;
        public const byte GrabDragHold = 12;
        public const byte FollowHold = 13;
        public const byte PointerActive = 14;

        public static void Validate(byte channel, string bindingId, string relativePath)
        {
            if (channel <= PointerActive)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Attribute binding '{bindingId}' in {relativePath}: camera behavior sink supports channels 0..{PointerActive}; found {channel}.");
        }
    }

    public sealed class CameraBehaviorInputState
    {
        public Vector2 Move { get; private set; }
        public Vector2 PointerPosition { get; private set; }
        public bool PointerActive { get; private set; }
        public Vector2 PointerDelta { get; private set; }
        public Vector2 Look { get; private set; }
        public float Zoom { get; private set; }
        public bool RotateHold { get; private set; }
        public bool RotateLeft { get; private set; }
        public bool RotateRight { get; private set; }
        public bool GrabDragHold { get; private set; }
        public bool FollowHold { get; private set; }
        public long Revision { get; private set; }

        public void Clear()
        {
            Move = Vector2.Zero;
            PointerPosition = Vector2.Zero;
            PointerActive = false;
            PointerDelta = Vector2.Zero;
            Look = Vector2.Zero;
            Zoom = 0f;
            RotateHold = false;
            RotateLeft = false;
            RotateRight = false;
            GrabDragHold = false;
            FollowHold = false;
            Revision++;
        }

        public void Apply(byte channel, float value, AttributeBindingMode mode)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException("Camera behavior input value must be finite.");
            }

            switch (channel)
            {
                case CameraBehaviorInputChannels.MoveX:
                    Move = new Vector2(ApplyScalar(Move.X, value, mode), Move.Y);
                    break;
                case CameraBehaviorInputChannels.MoveY:
                    Move = new Vector2(Move.X, ApplyScalar(Move.Y, value, mode));
                    break;
                case CameraBehaviorInputChannels.PointerX:
                    PointerPosition = new Vector2(ApplyScalar(PointerPosition.X, value, mode), PointerPosition.Y);
                    break;
                case CameraBehaviorInputChannels.PointerY:
                    PointerPosition = new Vector2(PointerPosition.X, ApplyScalar(PointerPosition.Y, value, mode));
                    break;
                case CameraBehaviorInputChannels.PointerActive:
                    PointerActive = ApplyBool(PointerActive, value, mode);
                    break;
                case CameraBehaviorInputChannels.PointerDeltaX:
                    PointerDelta = new Vector2(ApplyScalar(PointerDelta.X, value, mode), PointerDelta.Y);
                    break;
                case CameraBehaviorInputChannels.PointerDeltaY:
                    PointerDelta = new Vector2(PointerDelta.X, ApplyScalar(PointerDelta.Y, value, mode));
                    break;
                case CameraBehaviorInputChannels.LookX:
                    Look = new Vector2(ApplyScalar(Look.X, value, mode), Look.Y);
                    break;
                case CameraBehaviorInputChannels.LookY:
                    Look = new Vector2(Look.X, ApplyScalar(Look.Y, value, mode));
                    break;
                case CameraBehaviorInputChannels.Zoom:
                    Zoom = ApplyScalar(Zoom, value, mode);
                    break;
                case CameraBehaviorInputChannels.RotateHold:
                    RotateHold = ApplyBool(RotateHold, value, mode);
                    break;
                case CameraBehaviorInputChannels.RotateLeft:
                    RotateLeft = ApplyBool(RotateLeft, value, mode);
                    break;
                case CameraBehaviorInputChannels.RotateRight:
                    RotateRight = ApplyBool(RotateRight, value, mode);
                    break;
                case CameraBehaviorInputChannels.GrabDragHold:
                    GrabDragHold = ApplyBool(GrabDragHold, value, mode);
                    break;
                case CameraBehaviorInputChannels.FollowHold:
                    FollowHold = ApplyBool(FollowHold, value, mode);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported camera behavior input channel {channel}.");
            }
        }

        private static float ApplyScalar(float current, float value, AttributeBindingMode mode)
        {
            return mode == AttributeBindingMode.Override ? value : current + value;
        }

        private static bool ApplyBool(bool current, float value, AttributeBindingMode mode)
        {
            bool next = MathF.Abs(value) > 0.0001f;
            return mode == AttributeBindingMode.Override ? next : current || next;
        }
    }
}
