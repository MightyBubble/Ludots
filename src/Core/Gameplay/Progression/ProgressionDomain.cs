using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Progression
{
    public enum ProgressionScopeKind : byte
    {
        Self = 0,
        Explicit = 1,
        Named = 2,
    }

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

    public enum ProgressionRequirementEntitySource : byte
    {
        ScopeMembers = 0,
        ScopeHost = 1,
        Actor = 2,
        Subject = 3,
    }

    public readonly struct ProgressionScopeSpec
    {
        public readonly ProgressionScopeKind Kind;
        public readonly int ScopeKeyId;

        public ProgressionScopeSpec(ProgressionScopeKind kind, int scopeKeyId = 0)
        {
            Kind = kind;
            ScopeKeyId = scopeKeyId;
        }

        public static ProgressionScopeSpec Self => new(ProgressionScopeKind.Self);
    }

    public struct ProgressionDefinition
    {
        public int ProgressionId;
        public ProgressionScopeSpec DefaultScope;
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

    public readonly struct ProgressionRequirementEvaluationContext
    {
        public readonly Entity Actor;
        public readonly Entity Subject;
        public readonly Entity ExplicitScopeHost;

        public ProgressionRequirementEvaluationContext(Entity actor, Entity subject, Entity explicitScopeHost = default)
        {
            Actor = actor;
            Subject = subject;
            ExplicitScopeHost = explicitScopeHost;
        }
    }

    public readonly struct ProgressionRequirementNode
    {
        public readonly ProgressionRequirementNodeKind Kind;
        public readonly ProgressionScopeSpec Scope;
        public readonly ProgressionRequirementEntitySource EntitySource;
        public readonly int FirstChild;
        public readonly int ChildCount;
        public readonly int ProgressionId;
        public readonly int RequiredCount;
        public readonly int GraphProgramId;
        public readonly GameplayTagContainer RequiredTags;

        public ProgressionRequirementNode(
            ProgressionRequirementNodeKind kind,
            ProgressionScopeSpec scope,
            ProgressionRequirementEntitySource entitySource,
            int firstChild,
            int childCount,
            int progressionId,
            int requiredCount,
            int graphProgramId,
            in GameplayTagContainer requiredTags)
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
