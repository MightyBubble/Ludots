using System;
using System.Reflection;
using System.Threading.Tasks;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.HtmlEngine.Markup;

public static class MarkupBinder
{
	public static void Bind(UiScene scene, object codeBehind)
	{
		ArgumentNullException.ThrowIfNull(scene, "scene");
		ArgumentNullException.ThrowIfNull(codeBehind, "codeBehind");
		if (scene.Root == null)
		{
			throw new InvalidOperationException("Scene must be mounted before binding code-behind.");
		}
		BindNode(scene, scene.Root, codeBehind);
	}

	/// <summary>
	/// 在挂进任何场景之前绑定文档：<c>ui-click</c> 指向点击方法，<c>ui-canvas</c> 指向画布工厂，
	/// <c>ui-text</c> 指向返回 <see cref="UiTextBinding"/> 的字段、属性或无参方法。绑定落在
	/// <see cref="UiElement"/> 上，所以 <see cref="Compose.UiElementBuilder.FromElement"/> 每次重建都会带上，
	/// 适合交给 <see cref="Surface.UiSurfaceHost"/> 的租约发布。
	/// </summary>
	public static void Bind(UiDocument document, object codeBehind)
	{
		ArgumentNullException.ThrowIfNull(document, "document");
		ArgumentNullException.ThrowIfNull(codeBehind, "codeBehind");
		BindElement(document.Root, codeBehind);
	}

	private static void BindElement(UiElement element, object codeBehind)
	{
		string? click = element.Attributes["ui-click"] ?? element.Attributes["data-click"];
		if (!string.IsNullOrWhiteSpace(click))
		{
			MethodInfo method = ResolveMethod(codeBehind.GetType(), click);
			element.ClickHandler = ctx => InvokeMethod(codeBehind, method, ctx);
		}
		string? canvas = element.Attributes["ui-canvas"] ?? element.Attributes["data-canvas"];
		if (!string.IsNullOrWhiteSpace(canvas))
		{
			element.CanvasContent = InvokeCanvasFactory(codeBehind, ResolveCanvasMethod(codeBehind.GetType(), canvas));
		}
		string? text = element.Attributes["ui-text"] ?? element.Attributes["data-text"];
		if (!string.IsNullOrWhiteSpace(text))
		{
			element.TextBinding = ResolveTextBinding(codeBehind, text);
		}
		foreach (UiElement child in element.Children)
		{
			BindElement(child, codeBehind);
		}
	}

	private static UiTextBinding ResolveTextBinding(object codeBehind, string memberName)
	{
		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = codeBehind.GetType();
		object? value = type.GetField(memberName, Flags)?.GetValue(codeBehind)
			?? type.GetProperty(memberName, Flags)?.GetValue(codeBehind);
		if (value == null && type.GetMethod(memberName, Flags, Type.EmptyTypes) is MethodInfo method)
		{
			value = method.Invoke(codeBehind, null);
		}
		return value as UiTextBinding
			?? throw new InvalidOperationException($"Text binding '{memberName}' on '{type.FullName}' must be a non-null UiTextBinding field, property or parameterless method.");
	}

	private static void BindNode(UiScene scene, UiNode node, object codeBehind)
	{
		string text = node.Attributes["ui-click"] ?? node.Attributes["data-click"];
		if (!string.IsNullOrWhiteSpace(text))
		{
			MethodInfo method = ResolveMethod(codeBehind.GetType(), text);
			UiActionHandle handle = scene.Dispatcher.Register(delegate(UiActionContext ctx)
			{
				InvokeMethod(codeBehind, method, ctx);
			});
			node.AddActionHandle(handle);
		}
		string text2 = node.Attributes["ui-canvas"] ?? node.Attributes["data-canvas"];
		if (!string.IsNullOrWhiteSpace(text2))
		{
			MethodInfo method2 = ResolveCanvasMethod(codeBehind.GetType(), text2);
			node.SetCanvasContent(InvokeCanvasFactory(codeBehind, method2));
		}
		foreach (UiNode child in node.Children)
		{
			BindNode(scene, child, codeBehind);
		}
	}

	private static MethodInfo ResolveMethod(Type targetType, string methodName)
	{
		MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			throw new InvalidOperationException($"Code-behind method '{methodName}' was not found on '{targetType.FullName}'.");
		}
		ParameterInfo[] parameters = method.GetParameters();
		if (parameters.Length > 1)
		{
			throw new InvalidOperationException("Code-behind method '" + methodName + "' must have zero parameters or a single UiActionContext parameter.");
		}
		if (parameters.Length == 1 && parameters[0].ParameterType != typeof(UiActionContext))
		{
			throw new InvalidOperationException("Code-behind method '" + methodName + "' must accept UiActionContext when a parameter is present.");
		}
		return method;
	}

	private static MethodInfo ResolveCanvasMethod(Type targetType, string methodName)
	{
		MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			throw new InvalidOperationException($"Canvas factory method '{methodName}' was not found on '{targetType.FullName}'.");
		}
		if (method.GetParameters().Length != 0)
		{
			throw new InvalidOperationException("Canvas factory method '" + methodName + "' must not declare parameters.");
		}
		if (!typeof(IUiCanvasContent).IsAssignableFrom(method.ReturnType))
		{
			throw new InvalidOperationException("Canvas factory method '" + methodName + "' must return an IUiCanvasContent implementation.");
		}
		return method;
	}

	private static void InvokeMethod(object target, MethodInfo method, UiActionContext context)
	{
		object obj = ((method.GetParameters().Length == 0) ? method.Invoke(target, null) : method.Invoke(target, new object[1] { context }));
		if (obj is Task task)
		{
			task.GetAwaiter().GetResult();
		}
	}

	private static IUiCanvasContent InvokeCanvasFactory(object target, MethodInfo method)
	{
		object obj = method.Invoke(target, null);
		if (!(obj is IUiCanvasContent result))
		{
			throw new InvalidOperationException("Canvas factory method '" + method.Name + "' did not return an IUiCanvasContent instance.");
		}
		return result;
	}
}
