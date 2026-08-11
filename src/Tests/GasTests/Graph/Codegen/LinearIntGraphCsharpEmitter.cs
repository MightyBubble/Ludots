using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Tests.Gas.Graph.Codegen
{
    /// <summary>
    /// R0+Track C whitelist emitter: linear + branch integer IR → C#.
    /// Emits both a state-contract entry (<see cref="GeneratedMethodName"/>) and a tight
    /// locals entry (<see cref="GeneratedTightMethodName"/>).
    /// Jump / JumpIfFalse become labels + goto to preserve IR PC relative Imm semantics.
    /// Unsupported ops fail closed (no silent omit).
    /// </summary>
    public static class LinearIntGraphCsharpEmitter
    {
        public const string GeneratedNamespace = "Ludots.Graph.Generated";
        public const string GeneratedTypeName = "GraphEntry";
        public const string GeneratedMethodName = "Execute";
        public const string GeneratedTightMethodName = "ExecuteLinearInt";

        public static string Emit(ReadOnlySpan<GraphInstruction> program, string assemblyMarker)
        {
            if (string.IsNullOrWhiteSpace(assemblyMarker))
            {
                throw new ArgumentException("assemblyMarker is required.", nameof(assemblyMarker));
            }

            var usedInt = new SortedSet<int>();
            var usedBool = new SortedSet<int>();
            int resultRegister = 0;
            bool sawIntWrite = false;

            for (int i = 0; i < program.Length; i++)
            {
                ref readonly GraphInstruction ins = ref program[i];
                GraphNodeOp op = (GraphNodeOp)ins.Op;
                switch (op)
                {
                    case GraphNodeOp.None:
                        break;
                    case GraphNodeOp.ConstInt:
                        usedInt.Add(ins.Dst);
                        resultRegister = ins.Dst;
                        sawIntWrite = true;
                        break;
                    case GraphNodeOp.AddInt:
                        usedInt.Add(ins.A);
                        usedInt.Add(ins.B);
                        usedInt.Add(ins.Dst);
                        resultRegister = ins.Dst;
                        sawIntWrite = true;
                        break;
                    case GraphNodeOp.CompareLtInt:
                    case GraphNodeOp.CompareEqInt:
                        usedInt.Add(ins.A);
                        usedInt.Add(ins.B);
                        usedBool.Add(ins.Dst);
                        break;
                    case GraphNodeOp.Jump:
                        ValidateJumpTarget(i, ins.Imm, program.Length);
                        break;
                    case GraphNodeOp.JumpIfFalse:
                        usedBool.Add(ins.A);
                        ValidateJumpTarget(i, ins.Imm, program.Length);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"R0/Track C codegen rejects unsupported op '{op}' at instruction index {i}. " +
                            "Only None/ConstInt/AddInt/CompareLtInt/CompareEqInt/Jump/JumpIfFalse are whitelisted; " +
                            "widen via explicit emitter work, not silent skip.");
                }
            }

            var stateBody = new StringBuilder(program.Length * 64);
            var tightBody = new StringBuilder(program.Length * 56);

            for (int i = 0; i < program.Length; i++)
            {
                string label = LabelName(i);
                stateBody.Append("        ").Append(label).AppendLine(":");
                tightBody.Append("        ").Append(label).AppendLine(":");

                ref readonly GraphInstruction ins = ref program[i];
                GraphNodeOp op = (GraphNodeOp)ins.Op;
                switch (op)
                {
                    case GraphNodeOp.None:
                        stateBody.AppendLine("            ;");
                        tightBody.AppendLine("            ;");
                        break;
                    case GraphNodeOp.ConstInt:
                        stateBody.Append("            state.I[")
                            .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                            .Append("] = ")
                            .Append(ins.Imm.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(";");
                        tightBody.Append("            r")
                            .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                            .Append(" = ")
                            .Append(ins.Imm.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(";");
                        break;
                    case GraphNodeOp.AddInt:
                        stateBody.Append("            state.I[")
                            .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                            .Append("] = state.I[")
                            .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                            .Append("] + state.I[")
                            .Append(ins.B.ToString(CultureInfo.InvariantCulture))
                            .AppendLine("];");
                        tightBody.Append("            r")
                            .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                            .Append(" = r")
                            .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                            .Append(" + r")
                            .Append(ins.B.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(";");
                        break;
                    case GraphNodeOp.CompareLtInt:
                        AppendCompare(stateBody, tightBody, ins, "<");
                        break;
                    case GraphNodeOp.CompareEqInt:
                        AppendCompare(stateBody, tightBody, ins, "==");
                        break;
                    case GraphNodeOp.Jump:
                        AppendGoto(stateBody, ResolveJumpTarget(i, ins.Imm, program.Length));
                        AppendGoto(tightBody, ResolveJumpTarget(i, ins.Imm, program.Length));
                        break;
                    case GraphNodeOp.JumpIfFalse:
                        int falseTarget = ResolveJumpTarget(i, ins.Imm, program.Length);
                        stateBody.Append("            if (state.B[")
                            .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                            .Append("] == 0) goto ")
                            .Append(LabelName(falseTarget))
                            .AppendLine(";");
                        tightBody.Append("            if (b")
                            .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                            .Append(" == 0) goto ")
                            .Append(LabelName(falseTarget))
                            .AppendLine(";");
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"R0/Track C codegen rejects unsupported op '{op}' at instruction index {i}.");
                }
            }

            stateBody.Append("        ").Append(LabelName(program.Length)).AppendLine(":");
            stateBody.AppendLine("            ;");
            tightBody.Append("        ").Append(LabelName(program.Length)).AppendLine(":");
            if (!sawIntWrite)
            {
                tightBody.AppendLine("            return 0;");
            }
            else
            {
                tightBody.Append("            return r")
                    .Append(resultRegister.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(";");
            }

            var source = new StringBuilder(1024 + stateBody.Length + tightBody.Length);
            source.AppendLine("// <auto-generated />");
            source.AppendLine("#nullable enable");
            source.AppendLine("using Ludots.Core.NodeLibraries.GASGraph;");
            source.Append("namespace ").Append(GeneratedNamespace).AppendLine(";");
            source.AppendLine();
            source.Append("public static class ").Append(GeneratedTypeName).AppendLine();
            source.AppendLine("{");
            source.Append("    public const string AssemblyMarker = \"")
                .Append(EscapeForCSharpString(assemblyMarker))
                .AppendLine("\";");
            source.Append("    public static void ").Append(GeneratedMethodName)
                .AppendLine("(ref GraphExecutionState state)");
            source.AppendLine("    {");
            source.Append(stateBody);
            source.AppendLine("    }");
            source.AppendLine();
            source.Append("    public static int ").Append(GeneratedTightMethodName).AppendLine("()");
            source.AppendLine("    {");
            AppendTightLocals(source, usedInt, usedBool);
            source.Append(tightBody);
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static void AppendCompare(
            StringBuilder stateBody,
            StringBuilder tightBody,
            in GraphInstruction ins,
            string opToken)
        {
            stateBody.Append("            state.B[")
                .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                .Append("] = (byte)(state.I[")
                .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                .Append("] ")
                .Append(opToken)
                .Append(" state.I[")
                .Append(ins.B.ToString(CultureInfo.InvariantCulture))
                .AppendLine("] ? 1 : 0);");
            tightBody.Append("            b")
                .Append(ins.Dst.ToString(CultureInfo.InvariantCulture))
                .Append(" = (byte)(r")
                .Append(ins.A.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(opToken)
                .Append(" r")
                .Append(ins.B.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" ? 1 : 0);");
        }

        private static void AppendGoto(StringBuilder body, int targetIndex)
        {
            body.Append("            goto ")
                .Append(LabelName(targetIndex))
                .AppendLine(";");
        }

        private static void AppendTightLocals(
            StringBuilder source,
            SortedSet<int> usedInt,
            SortedSet<int> usedBool)
        {
            foreach (int r in usedInt)
            {
                source.Append("        int r")
                    .Append(r.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" = 0;");
            }

            foreach (int b in usedBool)
            {
                source.Append("        byte b")
                    .Append(b.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(" = 0;");
            }
        }

        /// <summary>
        /// VM advances PC before the handler, then Jump adds Imm. Target = index + 1 + Imm.
        /// Targets past the program fall through to the end label (VM loop exit).
        /// </summary>
        private static int ResolveJumpTarget(int instructionIndex, int imm, int programLength)
        {
            long target = (long)instructionIndex + 1L + imm;
            if (target < 0)
            {
                throw new InvalidOperationException(
                    $"Jump/JumpIfFalse at index {instructionIndex} resolves to negative PC {target} (Imm={imm}).");
            }

            if (target > programLength)
            {
                return programLength;
            }

            return (int)target;
        }

        private static void ValidateJumpTarget(int instructionIndex, int imm, int programLength)
        {
            _ = ResolveJumpTarget(instructionIndex, imm, programLength);
        }

        private static string LabelName(int index) =>
            "L" + index.ToString(CultureInfo.InvariantCulture);

        private static string EscapeForCSharpString(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}
