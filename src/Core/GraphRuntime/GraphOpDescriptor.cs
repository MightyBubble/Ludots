using System;

namespace Ludots.Core.GraphRuntime
{
    public readonly struct GraphOpDescriptor
    {
        public readonly string Name;
        public readonly ushort OpCode;
        public readonly byte OutputKind;
        public readonly byte? FixedRegister;
        public readonly byte InputCount;
        public readonly byte Input0Kind;
        public readonly byte Input1Kind;
        public readonly byte Input2Kind;

        public GraphOpDescriptor(
            string name,
            ushort opCode,
            byte outputKind = 0,
            byte? fixedRegister = null,
            byte inputCount = 0,
            byte input0Kind = 0,
            byte input1Kind = 0,
            byte input2Kind = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Graph op name must be non-empty.", nameof(name));
            }

            if (inputCount > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(inputCount), inputCount, "Graph ops support at most three register inputs.");
            }

            Name = name;
            OpCode = opCode;
            OutputKind = outputKind;
            FixedRegister = fixedRegister;
            InputCount = inputCount;
            Input0Kind = input0Kind;
            Input1Kind = input1Kind;
            Input2Kind = input2Kind;
        }

        public bool HasDefaultCompilerShape =>
            OutputKind == 0 &&
            FixedRegister == null &&
            InputCount == 0 &&
            Input0Kind == 0 &&
            Input1Kind == 0 &&
            Input2Kind == 0;

        public GraphOpDescriptor WithName(string name)
        {
            return new GraphOpDescriptor(
                name,
                OpCode,
                OutputKind,
                FixedRegister,
                InputCount,
                Input0Kind,
                Input1Kind,
                Input2Kind);
        }

        public bool HasSameCompilerShape(in GraphOpDescriptor other)
        {
            return OpCode == other.OpCode &&
                   OutputKind == other.OutputKind &&
                   FixedRegister == other.FixedRegister &&
                   InputCount == other.InputCount &&
                   Input0Kind == other.Input0Kind &&
                   Input1Kind == other.Input1Kind &&
                   Input2Kind == other.Input2Kind;
        }

        public byte GetInputKind(int index)
        {
            return index switch
            {
                0 => Input0Kind,
                1 => Input1Kind,
                2 => Input2Kind,
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Graph op input index is out of range."),
            };
        }
    }
}
