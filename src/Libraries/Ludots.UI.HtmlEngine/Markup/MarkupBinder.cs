using System.Reflection;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.HtmlEngine.Markup;

public static class MarkupBinder
{
    public static void Bind(UiScene scene, object codeBehind)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(codeBehind);

        if (scene.Root == null)
        {
            throw new InvalidOperationException("Scene must be mounted before binding code-behind.");
        }

        BindNode(scene, scene.Root, codeBehind);
    }

    private static void BindNode(UiScene scene, UiNode node, object codeBehind)
    {
        string? methodName = node.Attributes["ui-click"] ?? node.Attributes["data-click"];
        if (!string.IsNullOrWhiteSpace(methodName))
        {
            MethodInfo method = ResolveMethod(codeBehind.GetType(), methodName);
            UiActionHandle handle = scene.Dispatcher.Register(ctx => InvokeMethod(codeBehind, method, ctx));
            node.AddActionHandle(handle);
        }

        string? canvasMethodName = node.Attributes["ui-canvas"] ?? node.Attributes["data-canvas"];
        if (!string.IsNullOrWhiteSpace(canvasMethodName))
        {
            MethodInfo canvasMethod = ResolveCanvasMethod(codeBehind.GetType(), canvasMethodName);
            node.SetCanvasContent(InvokeCanvasFactory(codeBehind, canvasMethod));
        }

        foreach (UiNode child in node.Children)
        {
            BindNode(scene, child, codeBehind);
        }
    }

    private static MethodInfo ResolveMethod(Type targetType, string methodName)
    {
        MethodInfo? method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            throw new InvalidOperationException($"Code-behind method '{methodName}' was not found on '{targetType.FullName}'.");
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length > 1)
        {
            throw new InvalidOperationException($"Code-behind method '{methodName}' must have zero parameters or a single UiActionContext parameter.");
        }

        if (parameters.Length == 1 && parameters[0].ParameterType != typeof(UiActionContext))
        {
            throw new InvalidOperationException($"Code-behind method '{methodName}' must accept UiActionContext when a parameter is present.");
        }

        return method;
    }

    private static MethodInfo ResolveCanvasMethod(Type targetType, string methodName)
    {
        MethodInfo? method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            throw new InvalidOperationException($"Canvas factory method '{methodName}' was not found on '{targetType.FullName}'.");
        }

        if (method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException($"Canvas factory method '{methodName}' must not declare parameters.");
        }

        if (method.ReturnType != typeof(UiCanvasContent))
        {
            throw new InvalidOperationException($"Canvas factory method '{methodName}' must return UiCanvasContent.");
        }

        return method;
    }

    private static void InvokeMethod(object target, MethodInfo method, UiActionContext context)
    {
        object? result = method.GetParameters().Length == 0
            ? method.Invoke(target, null)
            : method.Invoke(target, new object?[] { context });

        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
        }
    }

    private static UiCanvasContent InvokeCanvasFactory(object target, MethodInfo method)
    {
        object? result = method.Invoke(target, null);
        if (result is not UiCanvasContent canvasContent)
        {
            throw new InvalidOperationException($"Canvas factory method '{method.Name}' did not return a UiCanvasContent instance.");
        }

        return canvasContent;
    }
}
