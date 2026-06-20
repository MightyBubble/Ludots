using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Technology
{
    public enum TechnologyScopeKind : byte
    {
        Self = 0,
        Explicit = 1,
        Named = 2,
    }

    public enum TechnologyRequirementNodeKind : byte
    {
        None = 0,
        All = 1,
        Any = 2,
        Not = 3,
        TechCompleted = 4,
        EntityCount = 5,
        TagAll = 6,
        GraphValidation = 7,
        TechLevelAtLeast = 8,
    }

    public enum TechnologyRequirementEntitySource : byte
    {
        ScopeMembers = 0,
        ScopeHost = 1,
        Actor = 2,
        Subject = 3,
    }

    public readonly struct TechnologyScopeSpec
    {
        public readonly TechnologyScopeKind Kind;
        public readonly int ScopeKeyId;

        public TechnologyScopeSpec(TechnologyScopeKind kind, int scopeKeyId = 0)
        {
            Kind = kind;
            ScopeKeyId = scopeKeyId;
        }

        public static TechnologyScopeSpec Self => new(TechnologyScopeKind.Self);
    }

    public struct TechnologyDefinition
    {
        public int TechnologyId;
        public TechnologyScopeSpec DefaultScope;
    }

    public readonly struct TechnologyLevelChange
    {
        public readonly int Level;
        public readonly int Delta;

        public TechnologyLevelChange(int level, int delta)
        {
            Level = level;
            Delta = delta;
        }

        public static TechnologyLevelChange Complete => new(1, 0);

        public readonly int RequiredLevelOrCompleted => Level > 0 ? Level : 1;
    }

    public readonly struct TechnologyRequirementEvaluationContext
    {
        public readonly Entity Actor;
        public readonly Entity Subject;
        public readonly Entity ExplicitScopeHost;

        public TechnologyRequirementEvaluationContext(Entity actor, Entity subject, Entity explicitScopeHost = default)
        {
            Actor = actor;
            Subject = subject;
            ExplicitScopeHost = explicitScopeHost;
        }
    }

    public readonly struct TechnologyRequirementNode
    {
        public readonly TechnologyRequirementNodeKind Kind;
        public readonly TechnologyScopeSpec Scope;
        public readonly TechnologyRequirementEntitySource EntitySource;
        public readonly int FirstChild;
        public readonly int ChildCount;
        public readonly int TechnologyId;
        public readonly int RequiredCount;
        public readonly int GraphProgramId;
        public readonly GameplayTagContainer RequiredTags;

        public TechnologyRequirementNode(
            TechnologyRequirementNodeKind kind,
            TechnologyScopeSpec scope,
            TechnologyRequirementEntitySource entitySource,
            int firstChild,
            int childCount,
            int technologyId,
            int requiredCount,
            int graphProgramId,
            in GameplayTagContainer requiredTags)
        {
            Kind = kind;
            Scope = scope;
            EntitySource = entitySource;
            FirstChild = firstChild;
            ChildCount = childCount;
            TechnologyId = technologyId;
            RequiredCount = requiredCount;
            GraphProgramId = graphProgramId;
            RequiredTags = requiredTags;
        }
    }

    public sealed class TechnologyRequirementDefinition
    {
        public TechnologyRequirementDefinition(int requirementId, TechnologyRequirementNode[] nodes, int[] childIndices)
        {
            RequirementId = requirementId;
            Nodes = nodes ?? Array.Empty<TechnologyRequirementNode>();
            ChildIndices = childIndices ?? Array.Empty<int>();
        }

        public int RequirementId { get; }
        public TechnologyRequirementNode[] Nodes { get; }
        public int[] ChildIndices { get; }
    }
}
