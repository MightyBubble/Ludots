using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Presentation;

namespace NarrativeFrontendMod.Runtime;

public sealed class NarrativeFrontendService
{
    private const int PageCapacity = 16;

    private readonly bool[] _active = new bool[PageCapacity];
    private readonly string[] _ownerIds = new string[PageCapacity];
    private readonly NarrativeFrontendPageState?[] _pages = new NarrativeFrontendPageState?[PageCapacity];

    private NarrativeFrontendRenderState _snapshot = NarrativeFrontendRenderState.Empty;
    private int _revision;

    public int Revision => _revision;

    public NarrativeFrontendRenderState Snapshot => _snapshot;

    /// <summary>
    /// Publish a Core story frame (dialogue/subtitle/choices as strings + imageId).
    /// Paths are resolved at this boundary via <paramref name="display"/>.
    /// </summary>
    public void PublishStoryFrame(
        string ownerId,
        StoryPresentationFrame frame,
        PresentationDisplayResolver? display,
        string frameImageSrc = "")
    {
        Publish(StoryPresentationFrontendAdapter.ToPage(ownerId, frame, display, frameImageSrc));
    }

    public void Publish(NarrativeFrontendPageState page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(page.OwnerId))
        {
            throw new ArgumentException("NarrativeFrontend page requires OwnerId.", nameof(page));
        }

        if (!page.Visible || page.Surfaces == null || page.Surfaces.Count == 0)
        {
            Clear(page.OwnerId);
            return;
        }

        int slot = FindSlot(page.OwnerId);
        if (slot < 0)
        {
            slot = FindFreeSlot();
        }

        if (slot < 0)
        {
            throw new InvalidOperationException(
                $"NarrativeFrontend page capacity ({PageCapacity}) exhausted while publishing '{page.OwnerId}'.");
        }

        NarrativeFrontendPageState? existing = _pages[slot];
        if (_active[slot] &&
            existing != null &&
            string.Equals(existing.Signature, page.Signature, StringComparison.Ordinal))
        {
            return;
        }

        _active[slot] = true;
        _ownerIds[slot] = page.OwnerId;
        _pages[slot] = page;
        RebuildSnapshot();
    }

    public void Clear(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return;
        }

        int slot = FindSlot(ownerId);
        if (slot < 0 || !_active[slot])
        {
            return;
        }

        _active[slot] = false;
        _ownerIds[slot] = string.Empty;
        _pages[slot] = null;
        RebuildSnapshot();
    }

    private int FindSlot(string ownerId)
    {
        for (int i = 0; i < PageCapacity; i++)
        {
            if (_active[i] && string.Equals(_ownerIds[i], ownerId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < PageCapacity; i++)
        {
            if (!_active[i])
            {
                return i;
            }
        }

        return -1;
    }

    private void RebuildSnapshot()
    {
        var surfaces = new List<NarrativeFrontendSurfaceModel>(64);
        string backdrop = string.Empty;

        for (int i = 0; i < PageCapacity; i++)
        {
            if (!_active[i] || _pages[i] == null)
            {
                continue;
            }

            NarrativeFrontendPageState page = _pages[i]!;
            if (string.IsNullOrWhiteSpace(backdrop) && !string.IsNullOrWhiteSpace(page.BackdropHex))
            {
                backdrop = page.BackdropHex;
            }

            foreach (NarrativeFrontendSurfaceModel surface in page.Surfaces ?? Array.Empty<NarrativeFrontendSurfaceModel>())
            {
                if (surface.Visible)
                {
                    surfaces.Add(surface);
                }
            }
        }

        surfaces.Sort(static (left, right) => left.ZIndex.CompareTo(right.ZIndex));
        _revision++;
        _snapshot = new NarrativeFrontendRenderState(
            _revision,
            surfaces.Count > 0,
            backdrop,
            surfaces.ToArray());
    }
}
