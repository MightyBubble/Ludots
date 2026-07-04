using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.EntityView;
using Ludots.Core.Input.Selection;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas;

[TestFixture]
public sealed class EntityViewProfileTests
{
    [Test]
    public void EntityViewRuntimeConfig_RequiresDefaultProfile()
    {
        var config = new EntityViewRuntimeConfig
        {
            DefaultViewKey = SelectionViewKeys.Primary,
            Profiles =
            {
                new EntityViewProfileEntry
                {
                    ViewKey = SelectionViewKeys.Secondary,
                    CommandSourceCollectionKey = EntityCollectionKeys.CommandSource,
                    DisplayCollectionKey = EntityCollectionKeys.SelectionLivePrimary,
                }
            }
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => config.Validate())!;
        Assert.That(ex.Message, Does.Contain("defaultViewKey"));
    }

    [Test]
    public void EntityViewRuntime_PromoteCommandSource_IsReadableByHandle()
    {
        using World world = World.Create();
        var globals = new Dictionary<string, object>();
        Entity owner = world.Create();
        var collectionRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
        var collections = new EntityCollectionStore(collectionRegistry);
        globals[CoreServiceKeys.EntityCollectionStore.Name] = collections;

        var config = new EntityViewRuntimeConfig
        {
            DefaultViewKey = SelectionViewKeys.Primary,
            Profiles =
            {
                new EntityViewProfileEntry
                {
                    ViewKey = SelectionViewKeys.Primary,
                    CommandSourceCollectionKey = EntityCollectionKeys.CommandSource,
                    DisplayCollectionKey = EntityCollectionKeys.SelectionLivePrimary,
                }
            }
        };
        config.Validate();
        globals[CoreServiceKeys.EntityViewConfig.Name] = config;
        globals[CoreServiceKeys.EntityViewViewerEntity.Name] = owner;
        globals[CoreServiceKeys.EntityViewKey.Name] = SelectionViewKeys.Primary;

        Entity agent = world.Create();
        EntityViewProfileEntry profile = config.RequireProfile(SelectionViewKeys.Primary);
        EntityCollectionHandle handle = EntityViewRuntime.PromoteCommandSource(
            collections,
            owner,
            in profile,
            new[] { agent },
            "unit test");

        Assert.That(EntityViewRuntime.TryGetCommandSourceHandle(world, globals, config, out Entity resolvedOwner, out EntityCollectionHandle resolvedHandle), Is.True);
        Assert.That(resolvedOwner, Is.EqualTo(owner));
        Assert.That(resolvedHandle.Revision, Is.EqualTo(handle.Revision));
        Assert.That(EntityViewRuntime.GetCommandSourceCount(world, globals, config), Is.EqualTo(1));
    }
}
