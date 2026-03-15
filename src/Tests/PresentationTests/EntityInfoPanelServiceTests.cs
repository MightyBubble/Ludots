using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using EntityInfoPanelsMod;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class EntityInfoPanelServiceTests
{
    private const string SelectedEntityKey = "Tests.EntityInfo.Selected";

    [Test]
    public void Refresh_TracksMultipleInstances_AndBumpsUiRevisionForLayoutOnlyChanges()
    {
        using var world = World.Create();

        int healthId = AttributeRegistry.Register("Tests.EntityInfo.Health");
        int burningTagId = TagRegistry.Register("Tests.EntityInfo.Burning");

        var attributes = new AttributeBuffer();
        attributes.SetBase(healthId, 100f);
        attributes.SetCurrent(healthId, 75f);

        var tagCounts = new TagCountContainer();
        Assert.That(tagCounts.AddCount(burningTagId, 2), Is.True);

        var staticTags = new GameplayTagContainer();
        staticTags.AddTag(burningTagId);

        var effectiveTags = new GameplayTagEffectiveCache();
        effectiveTags.Set(burningTagId, true);

        Entity entity = world.Create(
            new Name { Value = "Arcweaver" },
            attributes,
            tagCounts,
            staticTags,
            effectiveTags);

        var service = new EntityInfoPanelService();
        var globals = new Dictionary<string, object>
        {
            [SelectedEntityKey] = entity
        };

        EntityInfoPanelHandle componentHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.ComponentInspector,
            EntityInfoPanelSurface.Ui,
            EntityInfoPanelTarget.Fixed(entity),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 16f, 20f, 420f, 320f),
            EntityInfoGasDetailFlags.None,
            true));

        EntityInfoPanelHandle gasHandle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.GasInspector,
            EntityInfoPanelSurface.Ui | EntityInfoPanelSurface.Overlay,
            EntityInfoPanelTarget.Global(SelectedEntityKey),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomRight, 18f, 22f, 440f, 300f),
            EntityInfoGasDetailFlags.ShowModifierState,
            true));

        service.Refresh(world, globals);

        Assert.That(componentHandle.IsValid, Is.True);
        Assert.That(gasHandle.IsValid, Is.True);
        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(2));
        Assert.That(service.UiRevision, Is.EqualTo(1));
        Assert.That(service.GetSubtitle(componentHandle.Slot), Does.Contain("Arcweaver"));

        bool sawNameSection = false;
        for (int i = 0; i < service.GetComponentSectionCount(componentHandle.Slot); i++)
        {
            if (service.GetComponentSectionName(componentHandle.Slot, i) == nameof(Name))
            {
                sawNameSection = true;
                break;
            }
        }

        Assert.That(sawNameSection, Is.True, "Component inspector should expose component sections for the target entity.");

        int revisionBeforeLayoutUpdate = service.UiRevision;
        Assert.That(
            service.UpdateLayout(
                componentHandle,
                new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopRight, 32f, 28f, 512f, 360f)),
            Is.True);

        service.Refresh(world, globals);

        Assert.That(service.UiRevision, Is.EqualTo(revisionBeforeLayoutUpdate + 1));
        Assert.That(service.GetLayout(componentHandle.Slot).Width, Is.EqualTo(512f));

        Assert.That(service.SetVisible(componentHandle, false), Is.True);
        service.Refresh(world, globals);

        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(1));
        Assert.That(service.Close(gasHandle), Is.True);
        service.Refresh(world, globals);
        Assert.That(service.GetVisibleUiCount(), Is.EqualTo(0));
    }

    [Test]
    public void RenderOverlay_EmitsRetainedMetadata_AndGasDetailsRespectFlags()
    {
        using var world = World.Create();

        int healthId = AttributeRegistry.Register("Tests.EntityInfo.GasHealth");
        int hasteTagId = TagRegistry.Register("Tests.EntityInfo.Haste");
        int templateId = EffectTemplateIdRegistry.Register("Tests.EntityInfo.HasteAura");

        Entity source = world.Create(new Name { Value = "Commander" });

        var attributes = new AttributeBuffer();
        attributes.SetBase(healthId, 100f);
        attributes.SetCurrent(healthId, 135f);

        var tagCounts = new TagCountContainer();
        Assert.That(tagCounts.AddCount(hasteTagId, 1), Is.True);

        var staticTags = new GameplayTagContainer();
        staticTags.AddTag(hasteTagId);

        var effectiveTags = new GameplayTagEffectiveCache();
        effectiveTags.Set(hasteTagId, true);

        Entity target = world.Create(
            new Name { Value = "Vanguard" },
            attributes,
            tagCounts,
            staticTags,
            effectiveTags,
            new ActiveEffectContainer());

        var modifiers = new EffectModifiers();
        Assert.That(modifiers.Add(healthId, ModifierOp.Add, 35f), Is.True);

        Entity effect = world.Create(
            modifiers,
            new GameplayEffect
            {
                RemainingTicks = 24,
                State = EffectState.Committed
            },
            new EffectTemplateRef { TemplateId = templateId },
            new EffectContext
            {
                Source = source,
                Target = target
            },
            new EffectStack
            {
                Count = 2,
                Limit = 4
            });

        var activeEffects = new ActiveEffectContainer();
        Assert.That(activeEffects.Add(effect), Is.True);
        world.Set(target, activeEffects);

        var service = new EntityInfoPanelService();
        var globals = new Dictionary<string, object>();
        EntityInfoPanelHandle handle = service.Open(new EntityInfoPanelRequest(
            EntityInfoPanelKind.GasInspector,
            EntityInfoPanelSurface.Overlay,
            EntityInfoPanelTarget.Fixed(target),
            new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopLeft, 24f, 18f, 360f, 220f),
            EntityInfoGasDetailFlags.ShowAttributeAggregateSources | EntityInfoGasDetailFlags.ShowModifierState,
            true));

        service.Refresh(world, globals);

        var overlay = new ScreenOverlayBuffer();
        service.RenderOverlay(overlay, new Vector2(1920f, 1080f));

        ReadOnlySpan<ScreenOverlayItem> items = overlay.GetSpan();
        Assert.That(items.Length, Is.GreaterThan(3));
        Assert.That(items[0].Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
        Assert.That(items[0].StableId, Is.GreaterThan(0));
        Assert.That(items[0].DirtySerial, Is.GreaterThan(0));

        string[] linesWithDetails = GetOverlayStrings(overlay, items);
        Assert.That(linesWithDetails, Has.Some.Contains("Tests.EntityInfo.Haste"));
        Assert.That(linesWithDetails, Has.Some.Contains("<- Tests.EntityInfo.HasteAura"));
        Assert.That(linesWithDetails, Has.Some.Contains("state=Committed"));

        Assert.That(service.UpdateGasDetailFlags(handle, EntityInfoGasDetailFlags.None), Is.True);
        service.Refresh(world, globals);

        overlay.Clear();
        service.RenderOverlay(overlay, new Vector2(1920f, 1080f));

        string[] compactLines = GetOverlayStrings(overlay, overlay.GetSpan());
        Assert.That(compactLines.Any(line => line.Contains("<-", System.StringComparison.Ordinal)), Is.False);
        Assert.That(compactLines.Any(line => line.Contains("state=Committed", System.StringComparison.Ordinal)), Is.False);
    }

    private static string[] GetOverlayStrings(ScreenOverlayBuffer overlay, ReadOnlySpan<ScreenOverlayItem> items)
    {
        var lines = new List<string>(items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly ScreenOverlayItem item = ref items[i];
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines.ToArray();
    }
}
