using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

internal static class GraphOpsNodeActorBinding
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

        if (ctx.Caster == Entity.Null)
        {
            throw new InvalidOperationException($"Gallery '{ctx.Vignette.Op}' requires a caster actor on the map.");
        }
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

    public static void WriteHealth(World world, Entity entity, float health, float healthMax)
    {
        int healthId = HealthAttributeId();
        if (!world.Has<AttributeBuffer>(entity))
        {
            world.Add(entity, new AttributeBuffer());
        }

        float ceiling = healthMax > 0f ? healthMax : health;
        if (ceiling <= 0f)
        {
            ceiling = 1f;
        }

        ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(entity);
        attrs.SetBase(healthId, ceiling);
        attrs.SetCurrent(healthId, health);
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
        if (ctx.Stage == null || ctx.StageProxies.Length > 0)
        {
            return;
        }

        GraphOpsNodeActor[] actors = ctx.Vignette.Actors;
        ctx.StageProxies = new Entity[actors.Length];
        bool viewerBound = false;
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActor actor = actors[i];
            bool bindViewer = !viewerBound && string.Equals(actor.Role, "caster", StringComparison.Ordinal);
            ctx.StageProxies[i] = ctx.Stage.BindMapEntity(
                ctx.SimActors[i],
                actor.Template,
                actor.Name,
                actor.X,
                actor.Y,
                ctx.ActorHealth[i],
                actor.HealthMax > 0f ? actor.HealthMax : actor.Health,
                bindViewer);
            viewerBound |= bindViewer;
        }
    }

    public static void SyncHud(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Stage == null || ctx.StageProxies.Length == 0)
        {
            return;
        }

        for (int i = 0; i < ctx.StageProxies.Length; i++)
        {
            GraphOpsNodeActor actor = ctx.Vignette.Actors[i];
            ctx.Stage.SetPosition(ctx.StageProxies[i], actor.X, actor.Y);
            ctx.Stage.SetHealth(ctx.StageProxies[i], ctx.ActorHealth[i], actor.HealthMax > 0f ? actor.HealthMax : actor.Health);
        }
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
