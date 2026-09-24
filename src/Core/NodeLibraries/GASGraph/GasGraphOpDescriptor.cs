using System;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public static class GasGraphOpDescriptor
    {
        public static GraphOpDescriptor Create(
            string name,
            ushort opCode,
            GraphValueType outputType = GraphValueType.Void,
            byte? fixedRegister = null)
        {
            return Create(name, opCode, outputType, ReadOnlySpan<GraphValueType>.Empty, fixedRegister);
        }

        public static GraphOpDescriptor CreateUnary(
            string name,
            ushort opCode,
            GraphValueType outputType,
            GraphValueType input0,
            byte? fixedRegister = null)
        {
            return Create(name, opCode, outputType, stackalloc GraphValueType[] { input0 }, fixedRegister);
        }

        public static GraphOpDescriptor CreateBinary(
            string name,
            ushort opCode,
            GraphValueType outputType,
            GraphValueType input0,
            GraphValueType input1,
            byte? fixedRegister = null)
        {
            return Create(name, opCode, outputType, stackalloc GraphValueType[] { input0, input1 }, fixedRegister);
        }

        public static GraphOpDescriptor CreateTernary(
            string name,
            ushort opCode,
            GraphValueType outputType,
            GraphValueType input0,
            GraphValueType input1,
            GraphValueType input2,
            byte? fixedRegister = null)
        {
            return Create(name, opCode, outputType, stackalloc GraphValueType[] { input0, input1, input2 }, fixedRegister);
        }

        public static GraphOpDescriptor Create(
            string name,
            ushort opCode,
            GraphValueType outputType,
            ReadOnlySpan<GraphValueType> inputs,
            byte? fixedRegister = null)
        {
            if (inputs.Length > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), inputs.Length, "GAS graph ops support at most three register inputs.");
            }

            return new GraphOpDescriptor(
                name,
                opCode,
                (byte)outputType,
                fixedRegister,
                (byte)inputs.Length,
                inputs.Length > 0 ? (byte)inputs[0] : (byte)GraphValueType.Void,
                inputs.Length > 1 ? (byte)inputs[1] : (byte)GraphValueType.Void,
                inputs.Length > 2 ? (byte)inputs[2] : (byte)GraphValueType.Void);
        }
    }
}
