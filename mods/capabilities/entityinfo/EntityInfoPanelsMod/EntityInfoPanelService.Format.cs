using System;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private int WriteComponentLines(Entity entity, World world, ComponentType componentType, int slot, int lineCursor)
    {
        int start = lineCursor;
        Type type = componentType.Type;

        if (type == typeof(Name) && world.TryGet(entity, out Name name))
        {
            SetComponentLine(slot, lineCursor++, $"Value = {name.Value}");
            return lineCursor - start;
        }

        if (type == typeof(Team) && world.TryGet(entity, out Team team))
        {
            SetComponentLine(slot, lineCursor++, $"Id = {team.Id}");
            return lineCursor - start;
        }

        if (type == typeof(MapEntity) && world.TryGet(entity, out MapEntity mapEntity))
        {
            SetComponentLine(slot, lineCursor++, $"MapId = {mapEntity.MapId.Value}");
            return lineCursor - start;
        }

        if (type == typeof(WorldPositionCm) && world.TryGet(entity, out WorldPositionCm position))
        {
            var worldCm = position.ToWorldCmInt2();
            SetComponentLine(slot, lineCursor++, $"WorldCm = ({worldCm.X}, {worldCm.Y})");
            return lineCursor - start;
        }

        if (type == typeof(VisualTransform) && world.TryGet(entity, out VisualTransform transform))
        {
            SetComponentLine(slot, lineCursor++, $"Position = {FormatVector3(transform.Position)}");
            SetComponentLine(slot, lineCursor++, $"Rotation = {FormatQuaternion(transform.Rotation)}");
            SetComponentLine(slot, lineCursor++, $"Scale = {FormatVector3(transform.Scale)}");
            return lineCursor - start;
        }

        if (type == typeof(CullState) && world.TryGet(entity, out CullState cull))
        {
            SetComponentLine(slot, lineCursor++, $"Visible = {cull.IsVisible}");
            SetComponentLine(slot, lineCursor++, $"LOD = {cull.LOD}");
            SetComponentLine(slot, lineCursor++, $"DistanceSq = {FormatNumber(cull.DistanceToCameraSq)}");
            return lineCursor - start;
        }

        if (type == typeof(AttributeBuffer) && world.TryGet(entity, out AttributeBuffer attrs))
        {
            bool any = false;
            for (int attrId = 0; attrId < AttributeRegistry.MaxAttributes && lineCursor < MaxComponentLinesPerPanel; attrId++)
            {
                float baseValue = attrs.GetBase(attrId);
                float currentValue = attrs.GetCurrent(attrId);
                if (baseValue == 0f && currentValue == 0f)
                {
                    continue;
                }

                any = true;
                string attrName = AttributeRegistry.GetName(attrId);
                if (string.IsNullOrEmpty(attrName))
                {
                    attrName = $"attr:{attrId}";
                }

                SetComponentLine(slot, lineCursor++, $"{attrName} = {FormatNumber(currentValue)} (base={FormatNumber(baseValue)})");
            }

            if (!any)
            {
                SetComponentLine(slot, lineCursor++, "(empty)");
            }

            return lineCursor - start;
        }

        if (type == typeof(TagCountContainer) && world.TryGet(entity, out TagCountContainer tagCounts))
        {
            bool any = false;
            for (int tagId = 1; tagId < TagRegistry.MaxTags && lineCursor < MaxComponentLinesPerPanel; tagId++)
            {
                ushort count = tagCounts.GetCount(tagId);
                if (count == 0)
                {
                    continue;
                }

                any = true;
                string tagName = TagRegistry.GetName(tagId);
                if (string.IsNullOrEmpty(tagName))
                {
                    tagName = $"tag:{tagId}";
                }

                SetComponentLine(slot, lineCursor++, $"{tagName} = {count}");
            }

            if (!any)
            {
                SetComponentLine(slot, lineCursor++, "(empty)");
            }

            return lineCursor - start;
        }

        object? component = entity.Get(componentType);
        if (component == null)
        {
            SetComponentLine(slot, lineCursor++, "(null)");
            return lineCursor - start;
        }

        FieldInfo[] fields = GetFields(type);
        if (fields.Length == 0)
        {
            SetComponentLine(slot, lineCursor++, component.ToString() ?? type.Name);
            return lineCursor - start;
        }

        for (int i = 0; i < fields.Length && lineCursor < MaxComponentLinesPerPanel; i++)
        {
            SetComponentLine(slot, lineCursor++, $"{fields[i].Name} = {FormatObject(fields[i].GetValue(component))}");
        }

        return lineCursor - start;
    }

    private bool WriteAttributeSources(int slot, ref int lineCount, World world, in ActiveEffectContainer activeEffects, int attrId)
    {
        bool dirty = false;
        for (int effectIndex = 0; effectIndex < activeEffects.Count && lineCount < MaxGasLinesPerPanel; effectIndex++)
        {
            Entity effectEntity = activeEffects.GetEntity(effectIndex);
            if (!world.IsAlive(effectEntity) || !world.TryGet(effectEntity, out EffectModifiers modifiers))
            {
                continue;
            }

            for (int modifierIndex = 0; modifierIndex < modifiers.Count && lineCount < MaxGasLinesPerPanel; modifierIndex++)
            {
                ModifierData modifier = modifiers.Get(modifierIndex);
                if (modifier.AttributeId != attrId)
                {
                    continue;
                }

                dirty |= SetGasLine(slot, lineCount++, $"    <- {DescribeEffect(world, effectEntity)} | {modifier.Operation} {FormatNumber(modifier.Value)}");
            }
        }

        return dirty;
    }

    private static bool HasModifierForAttribute(World world, in ActiveEffectContainer activeEffects, int attrId)
    {
        for (int effectIndex = 0; effectIndex < activeEffects.Count; effectIndex++)
        {
            Entity effectEntity = activeEffects.GetEntity(effectIndex);
            if (!world.IsAlive(effectEntity) || !world.TryGet(effectEntity, out EffectModifiers modifiers))
            {
                continue;
            }

            for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
            {
                if (modifiers.Get(modifierIndex).AttributeId == attrId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private FieldInfo[] GetFields(Type type)
    {
        if (_fieldCache.TryGetValue(type, out FieldInfo[]? cached))
        {
            return cached;
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        _fieldCache[type] = fields;
        return fields;
    }

    private static string DescribeEffect(World world, Entity effectEntity)
    {
        string templateName = "effect";
        if (world.TryGet(effectEntity, out EffectTemplateRef templateRef))
        {
            string resolved = EffectTemplateIdRegistry.GetName(templateRef.TemplateId);
            if (!string.IsNullOrEmpty(resolved))
            {
                templateName = resolved;
            }
        }

        string source = string.Empty;
        if (world.TryGet(effectEntity, out EffectContext context) && world.IsAlive(context.Source))
        {
            source = ResolveEntityLabel(world, context.Source);
        }

        string state = world.TryGet(effectEntity, out GameplayEffect effect)
            ? $"state={effect.State}, remaining={effect.RemainingTicks}"
            : "state=unknown";
        string stack = world.TryGet(effectEntity, out EffectStack effectStack)
            ? $", stacks={effectStack.Count}/{effectStack.Limit}"
            : string.Empty;
        return string.IsNullOrEmpty(source)
            ? $"{templateName} | {state}{stack}"
            : $"{templateName} | src={source} | {state}{stack}";
    }

    private static string ResolveEntityLabel(World world, Entity entity)
    {
        if (world.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value))
        {
            return $"{name.Value} #{entity.Id}";
        }

        return $"Entity #{entity.Id}";
    }

    private static string ResolveMissingSubtitle(EntityInfoPanelTarget target)
    {
        return target.Kind switch
        {
            EntityInfoPanelTargetKind.FixedEntity => "Fixed target unavailable.",
            EntityInfoPanelTargetKind.GlobalEntityKey when !string.IsNullOrWhiteSpace(target.Key) => $"Waiting for `{target.Key}`.",
            _ => "Target unavailable.",
        };
    }

    private static string FormatObject(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text,
            Entity entity => entity == Entity.Null ? "Entity.Null" : $"Entity #{entity.Id}",
            MapId mapId => mapId.Value,
            Vector2 vector2 => $"({FormatNumber(vector2.X)}, {FormatNumber(vector2.Y)})",
            Vector3 vector3 => FormatVector3(vector3),
            Quaternion quaternion => FormatQuaternion(quaternion),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
        };
    }

    private static string FormatVector3(Vector3 value) =>
        $"({FormatNumber(value.X)}, {FormatNumber(value.Y)}, {FormatNumber(value.Z)})";

    private static string FormatQuaternion(Quaternion value) =>
        $"({FormatNumber(value.X)}, {FormatNumber(value.Y)}, {FormatNumber(value.Z)}, {FormatNumber(value.W)})";

    private static string FormatNumber(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
