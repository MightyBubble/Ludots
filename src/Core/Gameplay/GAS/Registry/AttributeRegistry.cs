using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry
{
    public static class AttributeRegistry
    {
        public const int InvalidId = -1;

        /// <summary>内嵌镜像宽度：AttributeBuffer/AttributeLastSnapshot/位掩码等组件内定长数组的槽位数。
        /// 槽位 [64, <see cref="MaxAttributeIds"/}) 只存在于 WorldAttributeStore（RFC-0067 P1），
        /// 注册上限与组件宽度自此解耦。</summary>
        public const int MaxAttributes = 64;

        /// <summary>可登记属性种类上限 = 容量计划绝对天花板（RFC-0067 §3.1）。</summary>
        public const int MaxAttributeIds = 1024;

        private static IdentityTable Table => ModRegistryAmbient.Current.Attributes;
        private static AttributeConstraints[] Constraints => ModRegistryAmbient.Current.AttributeConstraints;

        public static bool IsFrozen => Table.IsFrozen;

        public static bool IsValidId(int attributeId)
        {
            return attributeId != InvalidId && Table.ContainsId(attributeId);
        }

        public static int RequireId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Attribute name is empty.", nameof(name));
            }

            int id = GetId(name);
            if (id == InvalidId)
            {
                throw new ArgumentException($"Attribute '{name}' is not registered.", nameof(name));
            }

            return id;
        }

        public static void Clear() => ModRegistryAmbient.Current.ReplaceAttributes();

        public static void Freeze() => Table.Freeze();

        public static int Count => Table.Count;

        public static int Register(string name) => Table.Register(name);

        public static int GetId(string name) => Table.GetId(name);

        public static string GetName(int id) => Table.GetName(id);

        public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();

        public static bool TryGetConstraints(int attributeId, out AttributeConstraints constraints)
        {
            constraints = default;
            if ((uint)attributeId >= (uint)MaxAttributeIds) return false;
            constraints = Constraints[attributeId];
            return constraints.HasAny;
        }

        public static void SetConstraints(int attributeId, in AttributeConstraints constraints)
        {
            if (attributeId == InvalidId || (uint)attributeId >= (uint)MaxAttributeIds)
            {
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "Attribute id is invalid.");
            }

            Constraints[attributeId] = constraints;
        }

        public static void ReplaceConstraints(int attributeId, in AttributeConstraints constraints)
        {
            if (attributeId == InvalidId || (uint)attributeId >= (uint)MaxAttributeIds)
            {
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "Attribute id is invalid.");
            }

            if (!Table.ContainsId(attributeId))
            {
                throw new InvalidOperationException(
                    $"Attribute id {attributeId} is not registered; cannot ReplaceConstraints (new identities require EngineRestart).");
            }

            if (!Constraints[attributeId].HasAny)
            {
                throw new InvalidOperationException(
                    $"Attribute id {attributeId} has no authored constraints; hot-apply cannot introduce a new constraint schema.");
            }

            if (!constraints.HasAny)
            {
                throw new InvalidOperationException(
                    $"Attribute id {attributeId} replacement constraints are empty; deleting constraints requires MapReload/EngineRestart.");
            }

            Constraints[attributeId] = constraints;
        }

        public static void SetConstraints(string attributeName, in AttributeConstraints constraints)
        {
            if (string.IsNullOrEmpty(attributeName)) return;
            int id = GetId(attributeName);
            if (id == InvalidId)
            {
                id = Register(attributeName);
            }
            SetConstraints(id, in constraints);
        }

        public readonly struct AttributeConstraints
        {
            public readonly bool HasAny;
            public readonly bool ClampCurrentToBase;
            public readonly bool HasMin;
            public readonly float Min;
            public readonly bool HasMax;
            public readonly float Max;

            private AttributeConstraints(bool clampToBase, bool hasMin, float min, bool hasMax, float max)
            {
                ClampCurrentToBase = clampToBase;
                HasMin = hasMin;
                Min = min;
                HasMax = hasMax;
                Max = max;
                HasAny = clampToBase || hasMin || hasMax;
            }

            public static AttributeConstraints ClampToBase(float min = 0f)
            {
                return new AttributeConstraints(clampToBase: true, hasMin: true, min: min, hasMax: false, max: 0f);
            }

            public static AttributeConstraints Create(bool clampToBase, bool hasMin, float min, bool hasMax, float max)
            {
                return new AttributeConstraints(clampToBase: clampToBase, hasMin: hasMin, min: min, hasMax: hasMax, max: max);
            }
        }
    }
}
