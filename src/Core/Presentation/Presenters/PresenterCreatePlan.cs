using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// One explicit CreatePresenter command compiled from a declared child reference. Children
    /// authoring is syntactic sugar: the whole subtree is flattened into these nodes in
    /// declaration order with parent edges, and the runtime executes only this plan.
    /// </summary>
    public sealed class PresenterCreatePlanNode
    {
        public int DefinitionId;
        public int ParentNodeIndex = -1;
        public int ScopeTag;
        public ParamDefault[] ParamOverrides = Array.Empty<ParamDefault>();
        public PresenterInstanceTransformOverride TransformOverride;
        public PresenterChildInstanceOverride? InstanceOverride;
        public string Path = string.Empty;

        public bool HasOverridePayload =>
            InstanceOverride != null ||
            (ParamOverrides != null && ParamOverrides.Length != 0) ||
            TransformOverride.HasOverride;
    }

    /// <summary>
    /// Compiled CreatePresenter command plan for one root presenter definition. Nodes are ordered
    /// parent-before-child; a node whose <see cref="PresenterCreatePlanNode.ParentNodeIndex"/> is
    /// negative attaches to the hierarchy root entity created by the triggering command.
    /// </summary>
    public sealed class PresenterCreatePlan
    {
        public int RootDefinitionId;
        public PresenterCreatePlanNode[] Nodes = Array.Empty<PresenterCreatePlanNode>();
    }

    /// <summary>
    /// Searchable per-node record of one executed CreatePresenter plan node. The hierarchy root
    /// itself is recorded with <see cref="NodeIndex"/> equal to -1.
    /// </summary>
    public readonly struct PresenterCreateTraceEntry
    {
        public readonly int RootDefinitionId;
        public readonly int RootStableId;
        public readonly int NodeIndex;
        public readonly string Path;
        public readonly int DefinitionId;
        public readonly int ScopeId;
        public readonly int StableId;
        public readonly Entity Parent;
        public readonly Entity Created;

        public PresenterCreateTraceEntry(
            int rootDefinitionId,
            int rootStableId,
            int nodeIndex,
            string path,
            int definitionId,
            int scopeId,
            int stableId,
            Entity parent,
            Entity created)
        {
            RootDefinitionId = rootDefinitionId;
            RootStableId = rootStableId;
            NodeIndex = nodeIndex;
            Path = path;
            DefinitionId = definitionId;
            ScopeId = scopeId;
            StableId = stableId;
            Parent = parent;
            Created = created;
        }
    }

    public static class PresenterCreatePlanCompiler
    {
        public const string CircularChildReferenceError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanCircularChildReference";
        public const string UnknownChildDefinitionError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanUnknownChildDefinition";
        public const string ChildCapacityError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanChildCapacity";
        public const string ParamOverrideTypeError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanParamOverrideType";
        public const string InstanceChildrenPayloadMissingError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanInstanceChildrenPayloadMissing";
        public const string PlanParentMissingError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanParentMissing";
        public const string PlanNodeFailedError =
            "PRESENTATION.PRESENTER.ERR.CreatePlanNodeFailed";

        public static PresenterCreatePlan Compile(
            PresenterDefinitionRegistry definitions,
            PresenterDefinition rootDefinition)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            if (rootDefinition == null)
            {
                throw new ArgumentNullException(nameof(rootDefinition));
            }

            var nodes = new List<PresenterCreatePlanNode>();
            var activeSources = new HashSet<object>();
            ExpandChildren(
                definitions,
                rootDefinition,
                rootDefinition.Children,
                sourceKey: rootDefinition.Id,
                parentNodeIndex: -1,
                parentPath: "root",
                segmentName: "children",
                nodes,
                activeSources);
            return new PresenterCreatePlan
            {
                RootDefinitionId = rootDefinition.Id,
                Nodes = nodes.ToArray(),
            };
        }

        private static void ExpandChildren(
            PresenterDefinitionRegistry definitions,
            PresenterDefinition rootDefinition,
            ChildPresenterRef[]? children,
            object sourceKey,
            int parentNodeIndex,
            string parentPath,
            string segmentName,
            List<PresenterCreatePlanNode> nodes,
            HashSet<object> activeSources)
        {
            if (children == null || children.Length == 0)
            {
                return;
            }

            if (children.Length > PresenterChildren.MAX_CHILDREN)
            {
                throw new InvalidOperationException(
                    $"{ChildCapacityError}: root='{rootDefinition.Key}', childSource='{parentPath}/{segmentName}' declares {children.Length} direct children; capacity={PresenterChildren.MAX_CHILDREN}.");
            }

            if (!activeSources.Add(sourceKey))
            {
                throw new InvalidOperationException(
                    $"{CircularChildReferenceError}: root='{rootDefinition.Key}', childSource='{parentPath}/{segmentName}' is already active on the expansion path.");
            }

            try
            {
                for (int i = 0; i < children.Length; i++)
                {
                    ref readonly ChildPresenterRef child = ref children[i];
                    string nodePath = $"{parentPath}/{segmentName}[{i}]";
                    if (child.DefinitionId <= 0 ||
                        !definitions.TryGet(child.DefinitionId, out PresenterDefinition childDefinition))
                    {
                        throw new InvalidOperationException(
                            $"{UnknownChildDefinitionError}: root='{rootDefinition.Key}', childPath='{nodePath}', definitionId={child.DefinitionId} is not registered.");
                    }

                    ValidateParamOverrides(rootDefinition.Key, nodePath, child.ParamOverrides);

                    PresenterChildInstanceOverride? instanceOverride = child.InstanceOverride;
                    if (instanceOverride != null &&
                        instanceOverride.ChildrenMode == PresenterChildrenMode.Instance &&
                        instanceOverride.InstanceChildren == null)
                    {
                        throw new InvalidOperationException(
                            $"{InstanceChildrenPayloadMissingError}: root='{rootDefinition.Key}', childPath='{nodePath}' declares childrenMode 'Instance' without an instance children payload.");
                    }

                    var node = new PresenterCreatePlanNode
                    {
                        DefinitionId = child.DefinitionId,
                        ParentNodeIndex = parentNodeIndex,
                        ScopeTag = child.ScopeTag,
                        ParamOverrides = child.ParamOverrides ?? Array.Empty<ParamDefault>(),
                        TransformOverride = child.TransformOverride,
                        InstanceOverride = instanceOverride,
                        Path = nodePath,
                    };
                    nodes.Add(node);
                    int nodeIndex = nodes.Count - 1;

                    if (instanceOverride != null &&
                        instanceOverride.ChildrenMode == PresenterChildrenMode.Instance)
                    {
                        ExpandChildren(
                            definitions,
                            rootDefinition,
                            instanceOverride.InstanceChildren,
                            sourceKey: instanceOverride,
                            parentNodeIndex: nodeIndex,
                            parentPath: nodePath,
                            segmentName: "instanceChildren",
                            nodes,
                            activeSources);
                    }
                    else
                    {
                        ExpandChildren(
                            definitions,
                            rootDefinition,
                            childDefinition.Children,
                            sourceKey: childDefinition.Id,
                            parentNodeIndex: nodeIndex,
                            parentPath: nodePath,
                            segmentName: "children",
                            nodes,
                            activeSources);
                    }
                }
            }
            finally
            {
                activeSources.Remove(sourceKey);
            }
        }

        private static void ValidateParamOverrides(
            string rootKey,
            string nodePath,
            ParamDefault[]? overrides)
        {
            if (overrides == null)
            {
                return;
            }

            for (int i = 0; i < overrides.Length; i++)
            {
                ref readonly ParamDefault entry = ref overrides[i];
                if (entry.ParamKey < 0 || !Enum.IsDefined(typeof(ParamLane), entry.Lane))
                {
                    throw new InvalidOperationException(
                        $"{ParamOverrideTypeError}: root='{rootKey}', childPath='{nodePath}', overrides[{i}] paramKey={entry.ParamKey}, lane={entry.Lane} is not a valid typed param override.");
                }
            }
        }
    }
}
