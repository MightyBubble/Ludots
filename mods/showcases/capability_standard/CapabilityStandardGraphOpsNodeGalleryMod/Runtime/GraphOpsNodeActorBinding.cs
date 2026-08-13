using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeActorBinding
{
    public static int HealthAttributeId()
    {
        int id = AttributeRegistry.GetId("Health");
        return id >= 0 ? id : AttributeRegistry.Register("Health");
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
