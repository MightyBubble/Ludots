using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Knowledge;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeActorBinding
{
    public static int HealthAttributeId()
    {
        int id = AttributeRegistry.GetId("Health");
        if (id < 0)
        {
            throw new InvalidOperationException(
                "Gallery Health attribute is not registered; MapLoader templates must author Health before HUD bind.");
        }

        return id;
    }

    public static void RequireMapActors(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.SimActors.Length != actors.Length)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' expected {actors.Length} map actors, got {ctx.SimActors.Length}.");
        }

        for (int i = 0; i < actors.Length; i++)
        {
            if (!ctx.SimWorld.IsAlive(ctx.SimActors[i]))
            {
                throw new InvalidOperationException(
                    $"Gallery '{ctx.Vignette.Op}' map actor '{actors[i].Id}' is not alive.");
            }
        }

        BindRolesFromMap(ctx);
        EnsureHudLitBuffer(ctx);
    }

    public static void BindRolesFromMap(GraphOpsNodeDriverContext ctx)
    {
        ctx.Caster = Entity.Null;
        ctx.Target = Entity.Null;
        ctx.TargetContext = Entity.Null;
        ctx.Viewer = Entity.Null;
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            string role = actors[i].Role;
            Entity entity = ctx.SimActors[i];
            if (string.Equals(role, "caster", StringComparison.Ordinal))
            {
                ctx.Caster = entity;
            }
            else if (string.Equals(role, "target", StringComparison.Ordinal) && ctx.Target == Entity.Null)
            {
                ctx.Target = entity;
            }
            else if (string.Equals(role, "context", StringComparison.Ordinal))
            {
                ctx.TargetContext = entity;
            }
            else if (string.Equals(role, "viewer", StringComparison.Ordinal))
            {
                ctx.Viewer = entity;
            }
        }

        if (ctx.Caster == Entity.Null)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires a caster actor on the map.");
        }

        int targetIndex = FindRole(ctx.Vignette, "target");
        if (targetIndex >= 0 &&
            (ctx.Target == Entity.Null || !ctx.SimWorld.IsAlive(ctx.Target) || !ctx.Target.Equals(ctx.SimActors[targetIndex])))
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' target role is not bound to live map actor '{actors[targetIndex].Id}'.");
        }
    }

    public static Entity RequireRole(GraphOpsNodeDriverContext ctx, string role)
    {
        int index = FindRole(ctx.Vignette, role);
        if (index < 0)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires a '{role}' actor on the map.");
        }

        Entity entity = ctx.SimActors[index];
        if (!ctx.SimWorld.IsAlive(entity))
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' map actor '{ctx.Vignette.Actors[index].Id}' is not alive.");
        }

        return entity;
    }

    public static int FindRole(GraphOpsNodeVignette vignette, string role)
    {
        GraphOpsNodeActor[] actors = vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Role, role, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static int IndexOf(GraphOpsNodeDriverContext ctx, Entity entity)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            if (ctx.SimActors[i].Equals(entity))
            {
                return i;
            }
        }

        return -1;
    }

    public static int IndexOfId(GraphOpsNodeVignette vignette, string id)
    {
        GraphOpsNodeActor[] actors = vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(actors[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Vignette '{vignette.Op}' has no actor '{id}'.");
    }

    public static TagOps RequireTagOps(GraphOpsNodeDriverContext ctx)
    {
        return ctx.TagOps
            ?? throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires TagOps.");
    }

    public static void WriteHealth(World world, Entity entity, float health, float healthMax, TagOps tagOps)
    {
        ArgumentNullException.ThrowIfNull(tagOps);
        int healthId = HealthAttributeId();
        if (!world.Has<AttributeBuffer>(entity))
        {
            world.Add(entity, new AttributeBuffer());
        }

        if (!world.Has<DirtyFlags>(entity))
        {
            world.Add(entity, new DirtyFlags());
        }

        float ceiling = healthMax > 0f ? healthMax : health;
        if (ceiling <= 0f)
        {
            throw new InvalidOperationException("Gallery actor health max must be positive.");
        }

        AttributeMutationOps.SetBase(world, entity, healthId, ceiling, tagOps);
        AttributeMutationOps.SetCurrent(world, entity, healthId, Math.Clamp(health, 0f, ceiling), tagOps);
    }

    public static float ReadHealth(World world, Entity entity)
    {
        int healthId = HealthAttributeId();
        if (!world.IsAlive(entity) || !world.Has<AttributeBuffer>(entity))
        {
            throw new InvalidOperationException("Gallery actor is missing AttributeBuffer.");
        }

        return world.Get<AttributeBuffer>(entity).GetCurrent(healthId);
    }

    public static void SyncActorHealthFromWorld(GraphOpsNodeDriverContext ctx)
    {
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            ctx.ActorHealth[i] = ReadHealth(ctx.SimWorld, ctx.SimActors[i]);
        }
    }

    public static void EnsureHudLitBuffer(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.ActorHudLit.Length == ctx.SimActors.Length)
        {
            return;
        }

        ctx.ActorHudLit = new bool[ctx.SimActors.Length];
        Array.Fill(ctx.ActorHudLit, true);
    }

    public static void LightCasterAndHits(GraphOpsNodeDriverContext ctx)
    {
        EnsureHudLitBuffer(ctx);
        Array.Fill(ctx.ActorHudLit, false);
        int caster = FindRole(ctx.Vignette, "caster");
        if (caster >= 0)
        {
            ctx.ActorHudLit[caster] = true;
        }

        for (int i = 0; i < ctx.HitTargetCount; i++)
        {
            int index = IndexOf(ctx, ctx.HitTargets[i]);
            if (index >= 0)
            {
                ctx.ActorHudLit[index] = true;
            }
        }
    }

    public static void LightCasterAndIndices(GraphOpsNodeDriverContext ctx, ReadOnlySpan<int> indices)
    {
        EnsureHudLitBuffer(ctx);
        Array.Fill(ctx.ActorHudLit, false);
        int caster = FindRole(ctx.Vignette, "caster");
        if (caster >= 0)
        {
            ctx.ActorHudLit[caster] = true;
        }

        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if (index >= 0 && index < ctx.ActorHudLit.Length)
            {
                ctx.ActorHudLit[index] = true;
            }
        }
    }

    public static void SetHudLit(GraphOpsNodeDriverContext ctx, int index, bool lit)
    {
        EnsureHudLitBuffer(ctx);
        if (index < 0 || index >= ctx.ActorHudLit.Length)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' HUD lit index {index} is outside {ctx.ActorHudLit.Length} actors.");
        }

        ctx.ActorHudLit[index] = lit;
    }

    public static bool IsHealthDisclosed(GraphOpsNodeDriverContext ctx, int actorIndex)
    {
        if (ctx.Knowledge == null)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires KnowledgeProjectionStore.");
        }

        Entity viewer = ResolveKnowledgeViewer(ctx);
        if (!ctx.Knowledge.TryGet(viewer, ctx.SimActors[actorIndex], currentTick: 0, out KnowledgeDisclosureRecord record))
        {
            return false;
        }

        return record.AttributeMask.ContainsId(HealthAttributeId());
    }

    public static void BindHud(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || HudAlreadyBound(ctx))
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        int viewerIndex = FindRole(ctx.Vignette, "viewer");
        if (viewerIndex < 0)
        {
            viewerIndex = FindRole(ctx.Vignette, "caster");
        }

        if (viewerIndex < 0)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' HUD needs a viewer or caster actor before anyone else's health can be shown.");
        }

        BindHudActor(ctx, viewerIndex, bindAsViewer: true);
        for (int i = 0; i < actors.Length; i++)
        {
            if (i == viewerIndex)
            {
                continue;
            }

            BindHudActor(ctx, i, bindAsViewer: false);
        }

        ctx.Stage.GateWorldHudByKnowledge();
        ApplyHealthBarKnowledge(ctx);
    }

    public static void SyncHud(GraphOpsNodeDriverContext ctx)
    {
        TagOps tagOps = RequireTagOps(ctx);
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            float healthMax = actor.HealthMax > 0f ? actor.HealthMax : actor.Health;
            WriteHealth(ctx.SimWorld, ctx.SimActors[i], ctx.ActorHealth[i], healthMax, tagOps);
        }

        ApplyHealthBarKnowledge(ctx);
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            Entity proxy = ctx.StageProxies[i];
            if (!ctx.SimWorld.IsAlive(proxy))
            {
                throw new InvalidOperationException(
                    $"Gallery '{ctx.Vignette.Op}' HUD proxy for '{ctx.Vignette.Actors[i].Name}' is not alive.");
            }

            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(proxy, actor.X, actor.Y);
            ctx.Stage.SetHealth(proxy, ctx.ActorHealth[i], actor.HealthMax > 0f ? actor.HealthMax : actor.Health);
            ctx.Stage.SetHealthBarVisible(proxy, ctx.ActorHudLit[i]);
        }
    }

    public static void ApplyHealthBarKnowledge(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Knowledge == null)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires KnowledgeProjectionStore.");
        }

        EnsureHudLitBuffer(ctx);
        Entity viewer = ResolveKnowledgeViewer(ctx);
        int healthId = HealthAttributeId();
        KnowledgeIdMask256 empty = KnowledgeIdMask256.Empty;
        for (int i = 0; i < ctx.SimActors.Length; i++)
        {
            KnowledgeIdMask256 attributeMask = ctx.ActorHudLit[i]
                ? empty.WithId(healthId)
                : empty;
            ctx.Knowledge.Upsert(
                viewer,
                ctx.SimActors[i],
                new KnowledgeDisclosureRecord(
                    KnowledgePresence.LiveVisible,
                    KnowledgePositionAccess.Live,
                    in attributeMask,
                    in empty,
                    in empty,
                    viewer,
                    observedTick: 0,
                    expiryTick: 0,
                    confidencePermille: 1000,
                    revision: 0));
        }
    }

    public static void RestoreVignetteHealth(GraphOpsNodeDriverContext ctx)
    {
        TagOps tagOps = RequireTagOps(ctx);
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        for (int i = 0; i < actors.Length; i++)
        {
            WriteHealth(
                ctx.SimWorld,
                ctx.SimActors[i],
                actors[i].Health,
                actors[i].HealthMax > 0f ? actors[i].HealthMax : actors[i].Health,
                tagOps);
            ctx.ActorHealth[i] = ReadHealth(ctx.SimWorld, ctx.SimActors[i]);
        }
    }

    private static Entity ResolveKnowledgeViewer(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Viewer != Entity.Null && ctx.SimWorld.IsAlive(ctx.Viewer))
        {
            return ctx.Viewer;
        }

        if (ctx.Caster != Entity.Null && ctx.SimWorld.IsAlive(ctx.Caster))
        {
            return ctx.Caster;
        }

        throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' has no live viewer or caster for Health disclosure.");
    }

    private static void BindHudActor(GraphOpsNodeDriverContext ctx, int index, bool bindAsViewer)
    {
        GraphOpsNodeActor actor = ctx.Vignette.Actors[index];
        ctx.StageProxies[index] = ctx.Stage!.BindMapEntity(
            ctx.SimActors[index],
            actor.Template,
            actor.Name,
            actor.X,
            actor.Y,
            ctx.ActorHealth[index],
            actor.HealthMax > 0f ? actor.HealthMax : actor.Health,
            bindAsViewer);
    }

    private static bool HudAlreadyBound(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        if (ctx.StageProxies.Length != actors.Length)
        {
            return false;
        }

        for (int i = 0; i < actors.Length; i++)
        {
            Entity proxy = ctx.StageProxies[i];
            if (!ctx.SimWorld.IsAlive(proxy) || !proxy.Equals(ctx.SimActors[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static string FormatDetail(string template, Dictionary<string, string> values)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Detail template still has unsubstituted placeholders: {text}");
        }

        return text;
    }
}
