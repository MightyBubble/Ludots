using System;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class HtnPlanner256
    {
        private readonly int _maxDepth;
        private readonly Frame[] _stack;

        public HtnPlanner256(int maxDepth = 64)
        {
            _maxDepth = maxDepth < 8 ? 8 : maxDepth;
            _stack = new Frame[_maxDepth];
        }

        public bool TryPlan(
            in WorldStateBits256 worldState,
            HtnDomainCompiled256 domain,
            int rootTaskId,
            Span<int> outActionIds,
            out int outLength)
        {
            outLength = 0;
            if ((uint)rootTaskId >= (uint)domain.Tasks.Length) return false;

            int depth = 0;
            _stack[depth++] = Frame.CreateTask(rootTaskId, planLenSnapshot: 0);
            bool childFailed = false;

            while (depth > 0)
            {
                ref Frame frame = ref _stack[depth - 1];

                if (childFailed)
                {
                    childFailed = false;
                    frame.ResetMethod();
                    outLength = frame.PlanLenSnapshot;
                }

                if (!frame.HasMethod)
                {
                    if (!TrySelectMethod(in worldState, domain, frame.TaskId, frame.NextMethodOffset, out int methodId, out int nextOffset))
                    {
                        depth--;
                        childFailed = true;
                        continue;
                    }

                    frame.MethodId = methodId;
                    frame.SubtaskIndex = 0;
                    frame.NextMethodOffset = nextOffset;
                }

                ref readonly var method = ref domain.Methods[frame.MethodId];
                if (frame.SubtaskIndex >= method.SubtaskCount)
                {
                    depth--;
                    continue;
                }

                int subtaskIndex = method.SubtaskOffset + frame.SubtaskIndex;
                frame.SubtaskIndex++;
                if ((uint)subtaskIndex >= (uint)domain.Subtasks.Length)
                {
                    frame.ResetMethod();
                    outLength = frame.PlanLenSnapshot;
                    continue;
                }

                ref readonly var sub = ref domain.Subtasks[subtaskIndex];
                if (sub.Kind == HtnSubtaskKind.Action)
                {
                    if (outLength >= outActionIds.Length)
                    {
                        return false;
                    }
                    outActionIds[outLength++] = sub.Id;
                    continue;
                }

                if ((uint)sub.Id >= (uint)domain.Tasks.Length)
                {
                    frame.ResetMethod();
                    outLength = frame.PlanLenSnapshot;
                    continue;
                }

                if (depth >= _stack.Length)
                {
                    return false;
                }

                _stack[depth++] = Frame.CreateTask(sub.Id, outLength);
            }

            return !childFailed && outLength > 0;
        }

        private static bool TrySelectMethod(
            in WorldStateBits256 worldState,
            HtnDomainCompiled256 domain,
            int taskId,
            int methodOffsetWithinTask,
            out int methodId,
            out int nextMethodOffsetWithinTask)
        {
            methodId = -1;
            nextMethodOffsetWithinTask = methodOffsetWithinTask;

            ref readonly var task = ref domain.Tasks[taskId];
            int best = -1;
            int bestCost = int.MaxValue;
            int start = task.FirstMethod + methodOffsetWithinTask;
            int end = task.FirstMethod + task.MethodCount;

            for (int i = start; i < end; i++)
            {
                ref readonly var m = ref domain.Methods[i];
                if (!m.Condition.Match(in worldState)) continue;
                if (m.Cost < bestCost)
                {
                    bestCost = m.Cost;
                    best = i;
                }
            }

            if (best < 0) return false;

            methodId = best;
            nextMethodOffsetWithinTask = (best - task.FirstMethod) + 1;
            return true;
        }

        private struct Frame
        {
            public int TaskId;
            public int MethodId;
            public int SubtaskIndex;
            public int NextMethodOffset;
            public int PlanLenSnapshot;

            public bool HasMethod => MethodId >= 0;

            public static Frame CreateTask(int taskId, int planLenSnapshot)
            {
                return new Frame
                {
                    TaskId = taskId,
                    MethodId = -1,
                    SubtaskIndex = 0,
                    NextMethodOffset = 0,
                    PlanLenSnapshot = planLenSnapshot
                };
            }

            public void ResetMethod()
            {
                MethodId = -1;
                SubtaskIndex = 0;
            }
        }
    }
}

