using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Progression
{
    public enum ProgressionRequirementNodeKind : byte
    {
        None = 0,
        All = 1,
        Any = 2,
        Not = 3,
        ProgressionCompleted = 4,
        EntityCount = 5,
        TagAll = 6,
        GraphValidation = 7,
        ProgressionLevelAtLeast = 8,
    }

    public struct ProgressionDefinition
    {
        public int ProgressionId;
        public ScopeKey DeclaredScope;
    }

    public readonly struct ProgressionLevelChange
    {
        public readonly int Level;
        public readonly int Delta;

        public ProgressionLevelChange(int level, int delta)
        {
            Level = level;
            Delta = delta;
        }

        public static ProgressionLevelChange Complete => new(1, 0);

        public readonly int RequiredLevelOrCompleted => Level > 0 ? Level : 1;
    }

    public readonly struct ProgressionRequirementNode
    {
        public readonly ProgressionRequirementNodeKind Kind;
        public readonly ScopeKey Scope;
        public readonly RoleSlot EntitySource;
        public readonly int FirstChild;
        public readonly int ChildCount;
        public readonly int ProgressionId;
        public readonly int RequiredCount;
        public readonly int GraphProgramId;
        public readonly GameplayTagBitSet RequiredTags;

        public ProgressionRequirementNode(
            ProgressionRequirementNodeKind kind,
            ScopeKey scope,
            RoleSlot entitySource,
            int firstChild,
            int childCount,
            int progressionId,
            int requiredCount,
            int graphProgramId,
            in GameplayTagBitSet requiredTags)
        {
            Kind = kind;
            Scope = scope;
            EntitySource = entitySource;
            FirstChild = firstChild;
            ChildCount = childCount;
            ProgressionId = progressionId;
            RequiredCount = requiredCount;
            GraphProgramId = graphProgramId;
            RequiredTags = requiredTags;
        }
    }

    public sealed class ProgressionRequirementDefinition
    {
        public ProgressionRequirementDefinition(int requirementId, ProgressionRequirementNode[] nodes, int[] childIndices)
        {
            RequirementId = requirementId;
            Nodes = nodes ?? Array.Empty<ProgressionRequirementNode>();
            ChildIndices = childIndices ?? Array.Empty<int>();
        }

        public int RequirementId { get; }
        public ProgressionRequirementNode[] Nodes { get; }
        public int[] ChildIndices { get; }
    }
}
