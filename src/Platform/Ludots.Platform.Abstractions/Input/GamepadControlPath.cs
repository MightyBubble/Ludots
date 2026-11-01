using System;

namespace Ludots.Platform.Abstractions
{
    public enum GamepadControlKind : byte
    {
        Button = 1,
        Stick = 2,
        StickAxis = 3,

        // Analog axis and the digital trigger button. The control name itself reads the axis.
        Trigger = 4,
    }

    public enum GamepadButtonControl : byte
    {
        LeftFaceUp = 0,
        LeftFaceRight = 1,
        LeftFaceDown = 2,
        LeftFaceLeft = 3,
        RightFaceUp = 4,
        RightFaceRight = 5,
        RightFaceDown = 6,
        RightFaceLeft = 7,
        LeftShoulder = 8,
        RightShoulder = 9,
        LeftTrigger = 10,
        RightTrigger = 11,
        MiddleLeft = 12,
        Middle = 13,
        MiddleRight = 14,
        LeftThumb = 15,
        RightThumb = 16,
    }

    public enum GamepadAxisControl : byte
    {
        LeftX = 0,
        LeftY = 1,
        RightX = 2,
        RightY = 3,
        LeftTrigger = 4,
        RightTrigger = 5,
    }

    /// <summary>
    /// Engine-neutral <c>&lt;Gamepad&gt;/&lt;control&gt;</c> or <c>&lt;GamepadN&gt;/&lt;control&gt;</c> path.
    /// <c>&lt;Gamepad&gt;</c> and <c>&lt;Gamepad1&gt;</c> are slot 0; <c>&lt;Gamepad2&gt;</c>..<c>&lt;Gamepad4&gt;</c>
    /// are slots 1..3 (the host polls four pads). The device tag and the control name are
    /// case-insensitive. <c>/</c> is part of the control name, so <c>leftStick/x</c> does not
    /// match <c>left_stick_x</c>. An out-of-range tag or an unknown control fails.
    /// </summary>
    public readonly struct GamepadControlPath
    {
        public const int SlotCount = 4;
        public const int ButtonControlCount = 17;
        public const int AxisControlCount = 6;

        public int Pad { get; }
        public GamepadControlKind Kind { get; }
        public GamepadButtonControl Button { get; }
        public GamepadAxisControl Axis { get; }
        public GamepadAxisControl AxisY { get; }

        private GamepadControlPath(
            int pad,
            GamepadControlKind kind,
            GamepadButtonControl button,
            GamepadAxisControl axis,
            GamepadAxisControl axisY)
        {
            Pad = pad;
            Kind = kind;
            Button = button;
            Axis = axis;
            AxisY = axisY;
        }

        public static bool IsGamepadPath(ReadOnlySpan<char> path) =>
            path.Length >= 8 && path[0] == '<' && (path[1] == 'G' || path[1] == 'g') &&
            path.StartsWith("<Gamepad", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses a device path. Returns false when <paramref name="path"/> is not a gamepad path.
        /// A gamepad path with a bad slot or an unknown control throws.
        /// </summary>
        public static bool TryParse(string path, out GamepadControlPath parsed)
        {
            parsed = default;
            if (string.IsNullOrEmpty(path) || !IsGamepadPath(path))
            {
                return false;
            }

            ParseFailure failure = ParseTagged(path, out parsed);
            if (failure == ParseFailure.None)
            {
                return true;
            }

            throw Failure(path, failure);
        }

        /// <summary>
        /// Parses <c>&lt;Gamepad&gt;/&lt;control&gt;</c> or <c>&lt;GamepadN&gt;/&lt;control&gt;</c>.
        /// </summary>
        public static GamepadControlPath Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Gamepad path cannot be null or whitespace.", nameof(path));
            }

            if (TryParse(path, out GamepadControlPath parsed))
            {
                return parsed;
            }

            throw Failure(path, ParseFailure.NotAGamepadPath);
        }

        private static ParseFailure ParseTagged(string path, out GamepadControlPath parsed)
        {
            parsed = default;
            int close = path.IndexOf('>');
            if (close < 0)
            {
                return ParseFailure.InvalidIndex;
            }

            ReadOnlySpan<char> body = path.AsSpan(1, close - 1);
            int pad;
            if (body.Length == 7)
            {
                if (!body.Equals("Gamepad", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseFailure.InvalidIndex;
                }

                pad = 0;
            }
            else if (body.Length == 8 && body.Slice(0, 7).Equals("Gamepad", StringComparison.OrdinalIgnoreCase))
            {
                int slot = body[7] - '0';
                if ((uint)(slot - 1) >= (uint)SlotCount)
                {
                    return ParseFailure.InvalidIndex;
                }

                pad = slot - 1;
            }
            else
            {
                return ParseFailure.InvalidIndex;
            }

            if ((uint)(close + 1) >= (uint)path.Length || path[close + 1] != '/')
            {
                return ParseFailure.MissingControl;
            }

            return Match(path.AsSpan(close + 2), pad, out parsed);
        }

        private static ParseFailure Match(ReadOnlySpan<char> control, int pad, out GamepadControlPath parsed)
        {
            parsed = default;
            if (control.IsEmpty)
            {
                return ParseFailure.MissingControl;
            }

            ControlName[] controls = Controls;
            for (int i = 0; i < controls.Length; i++)
            {
                if (!control.Equals(controls[i].Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ControlName found = controls[i];
                parsed = new GamepadControlPath(pad, found.Kind, found.Button, found.Axis, found.AxisY);
                return ParseFailure.None;
            }

            return ParseFailure.UnknownControl;
        }

        private static InvalidOperationException Failure(string raw, ParseFailure failure) => failure switch
        {
            ParseFailure.InvalidIndex => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_INDEX_INVALID: '{raw}' is outside <Gamepad> and <Gamepad1>..<Gamepad{SlotCount}>."),
            ParseFailure.MissingControl => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_PATH_INVALID: '{raw}' has no control."),
            ParseFailure.NotAGamepadPath => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_PATH_INVALID: '{raw}' is not a <Gamepad>/<control> or <GamepadN>/<control> path."),
            _ => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN: '{raw}' is not a gamepad control: {CanonicalControls}"),
        };

        private readonly struct ControlName
        {
            public ControlName(
                string name,
                GamepadControlKind kind,
                GamepadButtonControl button,
                GamepadAxisControl axis,
                GamepadAxisControl axisY)
            {
                Name = name;
                Kind = kind;
                Button = button;
                Axis = axis;
                AxisY = axisY;
            }

            public string Name { get; }
            public GamepadControlKind Kind { get; }
            public GamepadButtonControl Button { get; }
            public GamepadAxisControl Axis { get; }
            public GamepadAxisControl AxisY { get; }
        }

        private static readonly ControlName[] Controls =
        {
            new("buttonSouth", GamepadControlKind.Button, GamepadButtonControl.RightFaceDown, default, default),
            new("buttonEast", GamepadControlKind.Button, GamepadButtonControl.RightFaceRight, default, default),
            new("buttonWest", GamepadControlKind.Button, GamepadButtonControl.RightFaceLeft, default, default),
            new("buttonNorth", GamepadControlKind.Button, GamepadButtonControl.RightFaceUp, default, default),
            new("dpadUp", GamepadControlKind.Button, GamepadButtonControl.LeftFaceUp, default, default),
            new("dpadDown", GamepadControlKind.Button, GamepadButtonControl.LeftFaceDown, default, default),
            new("dpadLeft", GamepadControlKind.Button, GamepadButtonControl.LeftFaceLeft, default, default),
            new("dpadRight", GamepadControlKind.Button, GamepadButtonControl.LeftFaceRight, default, default),
            new("leftShoulder", GamepadControlKind.Button, GamepadButtonControl.LeftShoulder, default, default),
            new("rightShoulder", GamepadControlKind.Button, GamepadButtonControl.RightShoulder, default, default),
            new("leftTrigger", GamepadControlKind.Trigger, GamepadButtonControl.LeftTrigger, GamepadAxisControl.LeftTrigger, default),
            new("rightTrigger", GamepadControlKind.Trigger, GamepadButtonControl.RightTrigger, GamepadAxisControl.RightTrigger, default),
            new("leftStick", GamepadControlKind.Stick, default, GamepadAxisControl.LeftX, GamepadAxisControl.LeftY),
            new("rightStick", GamepadControlKind.Stick, default, GamepadAxisControl.RightX, GamepadAxisControl.RightY),
            new("leftStick/x", GamepadControlKind.StickAxis, default, GamepadAxisControl.LeftX, default),
            new("leftStick/y", GamepadControlKind.StickAxis, default, GamepadAxisControl.LeftY, default),
            new("rightStick/x", GamepadControlKind.StickAxis, default, GamepadAxisControl.RightX, default),
            new("rightStick/y", GamepadControlKind.StickAxis, default, GamepadAxisControl.RightY, default),
            new("leftStickPress", GamepadControlKind.Button, GamepadButtonControl.LeftThumb, default, default),
            new("rightStickPress", GamepadControlKind.Button, GamepadButtonControl.RightThumb, default, default),
            new("select", GamepadControlKind.Button, GamepadButtonControl.MiddleLeft, default, default),
            new("start", GamepadControlKind.Button, GamepadButtonControl.Middle, default, default),
            new("guide", GamepadControlKind.Button, GamepadButtonControl.MiddleRight, default, default),
        };

        private static readonly string CanonicalControls = JoinControls();

        private static string JoinControls()
        {
            var names = new string[Controls.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = Controls[i].Name;
            }

            return string.Join(", ", names);
        }

        private enum ParseFailure : byte
        {
            None = 0,
            InvalidIndex = 1,
            MissingControl = 2,
            UnknownControl = 3,
            NotAGamepadPath = 4,
        }
    }
}
