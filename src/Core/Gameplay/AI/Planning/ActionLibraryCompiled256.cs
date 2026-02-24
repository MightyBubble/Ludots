using System;
using Ludots.Core.Gameplay.AI.WorldState;

namespace Ludots.Core.Gameplay.AI.Planning
{
    public sealed class ActionLibraryCompiled256
    {
        public readonly WorldStateBits256[] PreMask;
        public readonly WorldStateBits256[] PreValues;
        public readonly WorldStateBits256[] PostMask;
        public readonly WorldStateBits256[] PostValues;
        public readonly int[] Cost;
        public readonly ActionExecutorKind[] ExecutorKind;
        public readonly ActionOrderSpec[] OrderSpec;
        public readonly ActionBinding[] Bindings;
        public readonly ActionBindingRange[] BindingRanges;
        public readonly ActionCandidateIndex256 CandidateIndex;

        private ActionLibraryCompiled256(
            WorldStateBits256[] preMask,
            WorldStateBits256[] preValues,
            WorldStateBits256[] postMask,
            WorldStateBits256[] postValues,
            int[] cost,
            ActionExecutorKind[] executorKind,
            ActionOrderSpec[] orderSpec,
            ActionBinding[] bindings,
            ActionBindingRange[] bindingRanges,
            ActionCandidateIndex256 candidateIndex)
        {
            PreMask = preMask;
            PreValues = preValues;
            PostMask = postMask;
            PostValues = postValues;
            Cost = cost;
            ExecutorKind = executorKind;
            OrderSpec = orderSpec;
            Bindings = bindings;
            BindingRanges = bindingRanges;
            CandidateIndex = candidateIndex;
        }

        public int Count => PreMask.Length;

        public ReadOnlySpan<ActionBinding> GetBindings(int actionId)
        {
            if ((uint)actionId >= (uint)BindingRanges.Length) return ReadOnlySpan<ActionBinding>.Empty;
            var range = BindingRanges[actionId];
            return Bindings.AsSpan(range.Offset, range.Count);
        }

        public static ActionLibraryCompiled256 Compile(ActionOpDefinition256[] actions)
        {
            actions ??= Array.Empty<ActionOpDefinition256>();
            int count = actions.Length;

            var preMask = new WorldStateBits256[count];
            var preValues = new WorldStateBits256[count];
            var postMask = new WorldStateBits256[count];
            var postValues = new WorldStateBits256[count];
            var cost = new int[count];
            var executorKind = new ActionExecutorKind[count];
            var orderSpec = new ActionOrderSpec[count];

            int bindingTotal = 0;
            for (int i = 0; i < count; i++) bindingTotal += actions[i].Bindings.Length;

            var bindingRanges = new ActionBindingRange[count];
            var bindings = new ActionBinding[bindingTotal];
            int cursor = 0;

            for (int i = 0; i < count; i++)
            {
                ref readonly var a = ref actions[i];
                preMask[i] = a.PreMask;
                preValues[i] = a.PreValues;
                postMask[i] = a.PostMask;
                postValues[i] = a.PostValues;
                cost[i] = a.Cost;
                executorKind[i] = a.ExecutorKind;
                orderSpec[i] = a.OrderSpec;

                int n = a.Bindings.Length;
                bindingRanges[i] = new ActionBindingRange(cursor, n);
                if (n > 0)
                {
                    Array.Copy(a.Bindings, 0, bindings, cursor, n);
                    cursor += n;
                }
            }

            var candidateIndex = ActionCandidateIndex256.Build(in preMask, in preValues);

            return new ActionLibraryCompiled256(
                preMask,
                preValues,
                postMask,
                postValues,
                cost,
                executorKind,
                orderSpec,
                bindings,
                bindingRanges,
                candidateIndex);
        }

        public WorldStateBits256 ApplyPost(int actionId, in WorldStateBits256 state)
        {
            if ((uint)actionId >= (uint)Count) return state;
            var mask = PostMask[actionId];
            var values = PostValues[actionId];
            return new WorldStateBits256
            {
                U0 = (state.U0 & ~mask.U0) | (values.U0 & mask.U0),
                U1 = (state.U1 & ~mask.U1) | (values.U1 & mask.U1),
                U2 = (state.U2 & ~mask.U2) | (values.U2 & mask.U2),
                U3 = (state.U3 & ~mask.U3) | (values.U3 & mask.U3)
            };
        }

        public bool IsApplicable(int actionId, in WorldStateBits256 state)
        {
            if ((uint)actionId >= (uint)Count) return false;
            return state.Match(in PreMask[actionId], in PreValues[actionId]);
        }
    }
}

