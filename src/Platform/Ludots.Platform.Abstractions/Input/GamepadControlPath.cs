using System;

namespace Ludots.Platform.Abstractions
{
    public enum GamepadControlKind : byte
    {
        Button = 1,
        Stick = 2,
        StickAxis = 3,

        // Analog axis and the digital trigger button. A binding of the bare name reads the axis.
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
    /// Engine-neutral <c>&lt;Gamepad&gt;</c> / <c>&lt;GamepadN&gt;</c> path.
    /// <c>&lt;Gamepad&gt;</c> and <c>&lt;Gamepad1&gt;</c> are slot 0; <c>&lt;Gamepad2&gt;</c>..<c>&lt;Gamepad4&gt;</c>
    /// are slots 1..3 (the host polls four pads). An out-of-range tag or an unknown control fails.
    /// Separators <c>/</c>, <c>_</c>, and space are ignored so <c>leftStick/x</c> and <c>left_stick_x</c> match.
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
        /// Parses a device path (<c>&lt;Gamepad2&gt;/buttonSouth</c>) or a bare control on slot 0 (<c>buttonSouth</c>).
        /// </summary>
        public static GamepadControlPath Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Gamepad path cannot be null or whitespace.", nameof(path));
            }

            if (path[0] == '<')
            {
                if (!TryParse(path, out GamepadControlPath parsed))
                {
                    throw Failure(path, ParseFailure.UnknownControl);
                }

                return parsed;
            }

            ParseFailure failure = Match(path.AsSpan(), pad: 0, out GamepadControlPath control);
            if (failure != ParseFailure.None)
            {
                throw Failure(path, failure);
            }

            return control;
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
            if (IsBlank(control))
            {
                return ParseFailure.MissingControl;
            }

            ControlName[] controls = Controls;
            for (int i = 0; i < controls.Length; i++)
            {
                if (!EqualsLoose(control, controls[i].Name))
                {
                    continue;
                }

                ControlName found = controls[i];
                parsed = new GamepadControlPath(pad, found.Kind, found.Button, found.Axis, found.AxisY);
                return ParseFailure.None;
            }

            return ParseFailure.UnknownControl;
        }

        // Canonical names are uppercase and separator-free. Input may still contain '/', '_' or spaces.
        private static bool EqualsLoose(ReadOnlySpan<char> text, string canonical)
        {
            int i = 0;
            int j = 0;
            while (i < text.Length && j < canonical.Length)
            {
                char c = text[i];
                if (c is '/' or '_' or ' ')
                {
                    i++;
                    continue;
                }

                char upper = c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
                if (upper != canonical[j])
                {
                    return false;
                }

                i++;
                j++;
            }

            while (i < text.Length)
            {
                if (text[i] is not ('/' or '_' or ' '))
                {
                    return false;
                }

                i++;
            }

            return j == canonical.Length;
        }

        private static bool IsBlank(ReadOnlySpan<char> control)
        {
            for (int i = 0; i < control.Length; i++)
            {
                if (control[i] is not ('/' or '_' or ' '))
                {
                    return false;
                }
            }

            return true;
        }

        private static InvalidOperationException Failure(string raw, ParseFailure failure) => failure switch
        {
            ParseFailure.InvalidIndex => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_INDEX_INVALID: '{raw}' is outside <Gamepad> and <Gamepad1>..<Gamepad{SlotCount}>."),
            ParseFailure.MissingControl => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_PATH_INVALID: '{raw}' has no control."),
            _ => new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN: '{raw}' is not a gamepad control."),
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
            new("LEFTSTICK", GamepadControlKind.Stick, default, GamepadAxisControl.LeftX, GamepadAxisControl.LeftY),
            new("RIGHTSTICK", GamepadControlKind.Stick, default, GamepadAxisControl.RightX, GamepadAxisControl.RightY),
            new("LEFTSTICKX", GamepadControlKind.StickAxis, default, GamepadAxisControl.LeftX, default),
            new("LEFTSTICKY", GamepadControlKind.StickAxis, default, GamepadAxisControl.LeftY, default),
            new("RIGHTSTICKX", GamepadControlKind.StickAxis, default, GamepadAxisControl.RightX, default),
            new("RIGHTSTICKY", GamepadControlKind.StickAxis, default, GamepadAxisControl.RightY, default),
            new("LEFTTRIGGER", GamepadControlKind.Trigger, GamepadButtonControl.LeftTrigger, GamepadAxisControl.LeftTrigger, default),
            new("LT", GamepadControlKind.Trigger, GamepadButtonControl.LeftTrigger, GamepadAxisControl.LeftTrigger, default),
            new("RIGHTTRIGGER", GamepadControlKind.Trigger, GamepadButtonControl.RightTrigger, GamepadAxisControl.RightTrigger, default),
            new("RT", GamepadControlKind.Trigger, GamepadButtonControl.RightTrigger, GamepadAxisControl.RightTrigger, default),
            new("BUTTONSOUTH", GamepadControlKind.Button, GamepadButtonControl.RightFaceDown, default, default),
            new("BUTTONBOTTOM", GamepadControlKind.Button, GamepadButtonControl.RightFaceDown, default, default),
            new("A", GamepadControlKind.Button, GamepadButtonControl.RightFaceDown, default, default),
            new("BUTTONEAST", GamepadControlKind.Button, GamepadButtonControl.RightFaceRight, default, default),
            new("BUTTONRIGHT", GamepadControlKind.Button, GamepadButtonControl.RightFaceRight, default, default),
            new("B", GamepadControlKind.Button, GamepadButtonControl.RightFaceRight, default, default),
            new("CANCEL", GamepadControlKind.Button, GamepadButtonControl.RightFaceRight, default, default),
            new("BUTTONWEST", GamepadControlKind.Button, GamepadButtonControl.RightFaceLeft, default, default),
            new("BUTTONLEFT", GamepadControlKind.Button, GamepadButtonControl.RightFaceLeft, default, default),
            new("X", GamepadControlKind.Button, GamepadButtonControl.RightFaceLeft, default, default),
            new("BUTTONNORTH", GamepadControlKind.Button, GamepadButtonControl.RightFaceUp, default, default),
            new("BUTTONTOP", GamepadControlKind.Button, GamepadButtonControl.RightFaceUp, default, default),
            new("Y", GamepadControlKind.Button, GamepadButtonControl.RightFaceUp, default, default),
            new("DPADUP", GamepadControlKind.Button, GamepadButtonControl.LeftFaceUp, default, default),
            new("LEFTFACEUP", GamepadControlKind.Button, GamepadButtonControl.LeftFaceUp, default, default),
            new("DPADRIGHT", GamepadControlKind.Button, GamepadButtonControl.LeftFaceRight, default, default),
            new("LEFTFACERIGHT", GamepadControlKind.Button, GamepadButtonControl.LeftFaceRight, default, default),
            new("DPADDOWN", GamepadControlKind.Button, GamepadButtonControl.LeftFaceDown, default, default),
            new("LEFTFACEDOWN", GamepadControlKind.Button, GamepadButtonControl.LeftFaceDown, default, default),
            new("DPADLEFT", GamepadControlKind.Button, GamepadButtonControl.LeftFaceLeft, default, default),
            new("LEFTFACELEFT", GamepadControlKind.Button, GamepadButtonControl.LeftFaceLeft, default, default),
            new("LEFTSHOULDER", GamepadControlKind.Button, GamepadButtonControl.LeftShoulder, default, default),
            new("LB", GamepadControlKind.Button, GamepadButtonControl.LeftShoulder, default, default),
            new("LEFTBUMPER", GamepadControlKind.Button, GamepadButtonControl.LeftShoulder, default, default),
            new("RIGHTSHOULDER", GamepadControlKind.Button, GamepadButtonControl.RightShoulder, default, default),
            new("RB", GamepadControlKind.Button, GamepadButtonControl.RightShoulder, default, default),
            new("RIGHTBUMPER", GamepadControlKind.Button, GamepadButtonControl.RightShoulder, default, default),
            new("SELECT", GamepadControlKind.Button, GamepadButtonControl.MiddleLeft, default, default),
            new("BACK", GamepadControlKind.Button, GamepadButtonControl.MiddleLeft, default, default),
            new("MIDDLELEFT", GamepadControlKind.Button, GamepadButtonControl.MiddleLeft, default, default),
            new("START", GamepadControlKind.Button, GamepadButtonControl.Middle, default, default),
            new("MENU", GamepadControlKind.Button, GamepadButtonControl.Middle, default, default),
            new("MIDDLE", GamepadControlKind.Button, GamepadButtonControl.Middle, default, default),
            new("MIDDLESHOULDER", GamepadControlKind.Button, GamepadButtonControl.MiddleRight, default, default),
            new("GUIDE", GamepadControlKind.Button, GamepadButtonControl.MiddleRight, default, default),
            new("MIDDLERIGHT", GamepadControlKind.Button, GamepadButtonControl.MiddleRight, default, default),
            new("LEFTSTICKPRESS", GamepadControlKind.Button, GamepadButtonControl.LeftThumb, default, default),
            new("L3", GamepadControlKind.Button, GamepadButtonControl.LeftThumb, default, default),
            new("LEFTTHUMB", GamepadControlKind.Button, GamepadButtonControl.LeftThumb, default, default),
            new("RIGHTSTICKPRESS", GamepadControlKind.Button, GamepadButtonControl.RightThumb, default, default),
            new("R3", GamepadControlKind.Button, GamepadButtonControl.RightThumb, default, default),
            new("RIGHTTHUMB", GamepadControlKind.Button, GamepadButtonControl.RightThumb, default, default),
        };

        private enum ParseFailure : byte
        {
            None = 0,
            InvalidIndex = 1,
            MissingControl = 2,
            UnknownControl = 3,
        }
    }
}
