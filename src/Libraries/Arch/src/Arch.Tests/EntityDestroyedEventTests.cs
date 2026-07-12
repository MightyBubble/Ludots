using System.Reflection;
using System.Threading.Tasks;
using Arch.Core;

namespace Arch.Tests;

[TestFixture]
public sealed class EntityDestroyedEventTests
{
    [Test]
    public void DestroyWithoutSubscribers_DoesNotEnterHandlerStorageLock()
    {
        using var world = World.Create();
        Entity entity = world.Create();
        FieldInfo handlersField = typeof(World).GetField(
            "_entityDestroyedHandlers",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        object handlerStorage = handlersField.GetValue(world)!;

        Monitor.Enter(handlerStorage);
        try
        {
            Task destroy = Task.Run(() => world.Destroy(entity));
            Assert.That(destroy.Wait(TimeSpan.FromSeconds(1)), Is.True,
                "Destroy without subscribers must not wait on handler storage.");
        }
        finally
        {
            Monitor.Exit(handlerStorage);
        }
    }

    [Test]
    public void DestroyWithSubscribers_PreservesRegistrationOrderAcrossBatch()
    {
        using var world = World.Create();
        var observed = new List<(int Handler, int EntityId)>();
        world.SubscribeEntityDestroyed((in Entity entity) => observed.Add((1, entity.Id)));
        world.SubscribeEntityDestroyed((in Entity entity) => observed.Add((2, entity.Id)));
        Entity first = world.Create();
        Entity second = world.Create();

        world.Destroy(first);
        world.Destroy(second);

        Assert.That(observed, Is.EqualTo(new[]
        {
            (1, first.Id),
            (2, first.Id),
            (1, second.Id),
            (2, second.Id),
        }));
    }
}
