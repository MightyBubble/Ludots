using System;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class HtnDomainCompiled256
    {
        public readonly HtnCompoundTask[] Tasks;
        public readonly HtnMethod256[] Methods;
        public readonly HtnSubtask[] Subtasks;

        public HtnDomainCompiled256(HtnCompoundTask[] tasks, HtnMethod256[] methods, HtnSubtask[] subtasks)
        {
            Tasks = tasks ?? Array.Empty<HtnCompoundTask>();
            Methods = methods ?? Array.Empty<HtnMethod256>();
            Subtasks = subtasks ?? Array.Empty<HtnSubtask>();
        }
    }
}

