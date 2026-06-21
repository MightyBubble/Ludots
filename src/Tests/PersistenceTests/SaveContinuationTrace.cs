using System.Security.Cryptography;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Ludots.Tests.Persistence;

internal static class SaveContinuationTrace
{
    public static string[] RunFixedSteps(GameEngine engine, int count, float deltaTime)
    {
        var trace = new string[count];
        var pacemaker = (TurnBasedPacemaker)engine.Pacemaker;
        IClock clock = engine.GetService(CoreServiceKeys.Clock);
        for (int i = 0; i < count; i++)
        {
            pacemaker.Step();
            engine.Tick(deltaTime);
            trace[i] = $"tick={engine.GameSession.CurrentTick};fixedFrame={clock.Now(ClockDomainId.FixedFrame)};worldHash={ComputeWorldStateHash(engine.World)}";
        }

        return trace;
    }

    private static string ComputeWorldStateHash(World world)
    {
        MessagePackSerializerOptions options = CreateComponentSerializerOptions();
        var rows = new List<string>();
        world.Query(in QueryDescription.Null, entity =>
        {
            Signature signature = world.GetSignature(entity);
            var componentRows = new List<string>(signature.Components.Length);
            foreach (ComponentType componentType in signature.Components)
            {
                Type type = componentType.Type;
                object? component = world.Get(entity, componentType);
                if (component == null)
                {
                    componentRows.Add($"{type.FullName ?? type.Name}=<null>");
                    continue;
                }

                componentRows.Add($"{type.FullName ?? type.Name}={Convert.ToHexString(SerializeComponent(type, component, options))}");
            }

            componentRows.Sort(StringComparer.Ordinal);
            rows.Add($"{entity.Id}:{entity.Version}|{string.Join("|", componentRows)}");
        });

        rows.Sort(StringComparer.Ordinal);
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\n", rows));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static MessagePackSerializerOptions CreateComponentSerializerOptions()
    {
        return MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                LudotsCorePersistenceFormatters.CreateFormatters(),
                new IFormatterResolver[]
                {
                    BuiltinResolver.Instance,
                    ContractlessStandardResolverAllowPrivate.Instance
                }));
    }

    private static byte[] SerializeComponent(Type type, object component, MessagePackSerializerOptions options)
    {
        return MessagePackSerializer.Serialize(type, component, options);
    }
}
