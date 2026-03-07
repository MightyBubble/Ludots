using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseMounting
{
    public static void MountScene(ScriptContext context, UiScene scene)
    {
        UIRoot root = ResolveRoot(context);
        root.MountScene(scene);
        root.IsDirty = true;
    }

    public static void MountReactivePage<TState>(ScriptContext context, ReactivePage<TState> page)
    {
        UIRoot root = ResolveRoot(context);
        root.MountScene(page.Scene);
        root.IsDirty = true;
    }

    private static UIRoot ResolveRoot(ScriptContext context)
    {
        return context.Get(CoreServiceKeys.UIRoot) as UIRoot
            ?? throw new InvalidOperationException("UIRoot service is missing from ScriptContext.");
    }
}
