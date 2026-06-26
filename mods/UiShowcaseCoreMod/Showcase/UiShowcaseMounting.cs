using System;
using System.Collections.Generic;
using Ludots.Core.Scripting;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseMounting
{
    public static void PublishSurface(
        ScriptContext context,
        string ownerId,
        Func<UiElementBuilder> buildRoot,
        UiThemePack? theme = null,
        IEnumerable<UiStyleSheet>? styleSheets = null,
        UiSurfaceSegment segment = UiSurfaceSegment.Main,
        int priority = 0,
        bool exclusive = true)
    {
        IUiSurfaceHost host = ResolveHost(context);
        UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest(ownerId, segment, priority, exclusive));
        host.Publish(lease, UiSurfaceContribution.FromBuilder(buildRoot, theme, styleSheets));
    }

    public static void PublishReactivePage<TState>(
        ScriptContext context,
        string ownerId,
        ReactivePage<TState> page,
        UiSurfaceSegment segment = UiSurfaceSegment.Main,
        int priority = 0,
        bool exclusive = true)
    {
        IUiSurfaceHost host = ResolveHost(context);
        UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest(ownerId, segment, priority, exclusive));
        host.Publish(lease, UiSurfaceContribution.FromReactivePage(page));
    }

    public static void PublishContribution(
        ScriptContext context,
        string ownerId,
        UiSurfaceContribution contribution,
        UiSurfaceSegment segment = UiSurfaceSegment.Main,
        int priority = 0,
        bool exclusive = true)
    {
        IUiSurfaceHost host = ResolveHost(context);
        UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest(ownerId, segment, priority, exclusive));
        host.Publish(lease, contribution);
    }

    private static IUiSurfaceHost ResolveHost(ScriptContext context)
    {
        return context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
    }
}
