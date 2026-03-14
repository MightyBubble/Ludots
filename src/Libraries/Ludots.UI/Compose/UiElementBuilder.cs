using System;
using System.Collections.Generic;
using System.Globalization;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using SkiaSharp;

namespace Ludots.UI.Compose;

public sealed class UiElementBuilder
{
	private readonly List<UiElementBuilder> _children = new List<UiElementBuilder>();

	private readonly HashSet<string> _classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, string> _attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private Action<UiActionContext>? _onClick;

	private UiCanvasContent? _canvasContent;

	private UiStyle _style;

	private string? _elementId;

	private string? _textContent;

	public UiNodeKind Kind { get; }

	public string? TagName { get; }

	public UiElementBuilder(UiNodeKind kind, string? tagName = null)
	{
		Kind = kind;
		TagName = tagName;
		_style = CreateDefaultStyle(kind);
	}

	public UiElementBuilder Id(string id)
	{
		_elementId = id;
		return this;
	}

	public UiElementBuilder Class(string className)
	{
		_classNames.Add(className);
		return this;
	}

	public UiElementBuilder Classes(params string[] classNames)
	{
		foreach (string item in classNames)
		{
			_classNames.Add(item);
		}
		return this;
	}

	public UiElementBuilder Attribute(string name, string value)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Attribute name is required.", "name");
		}
		_attributes[name] = value;
		return this;
	}

	public UiElementBuilder Name(string value)
	{
		return Attribute("name", value);
	}

	public UiElementBuilder Type(string value)
	{
		return Attribute("type", value);
	}

	public UiElementBuilder Src(string value)
	{
		return Attribute("src", value);
	}

	public UiElementBuilder Value(string value)
	{
		return Attribute("value", value);
	}

	public UiElementBuilder Placeholder(string value)
	{
		return Attribute("placeholder", value);
	}

	public UiElementBuilder Required(bool value = true)
	{
		if (value)
		{
			_attributes["required"] = "true";
		}
		else
		{
			_attributes.Remove("required");
		}
		return this;
	}

	public UiElementBuilder MinLength(int value)
	{
		if (value <= 0)
		{
			_attributes.Remove("minlength");
		}
		else
		{
			_attributes["minlength"] = value.ToString(CultureInfo.InvariantCulture);
		}
		return this;
	}

	public UiElementBuilder MaxLength(int value)
	{
		if (value <= 0)
		{
			_attributes.Remove("maxlength");
		}
		else
		{
			_attributes["maxlength"] = value.ToString(CultureInfo.InvariantCulture);
		}
		return this;
	}

	public UiElementBuilder Pattern(string value)
	{
		return Attribute("pattern", value);
	}

	public UiElementBuilder Min(float value)
	{
		_attributes["min"] = value.ToString(CultureInfo.InvariantCulture);
		return this;
	}

	public UiElementBuilder Max(float value)
	{
		_attributes["max"] = value.ToString(CultureInfo.InvariantCulture);
		return this;
	}

	public UiElementBuilder Step(float value)
	{
		if (value <= 0f)
		{
			_attributes.Remove("step");
		}
		else
		{
			_attributes["step"] = value.ToString(CultureInfo.InvariantCulture);
		}
		return this;
	}

	public UiElementBuilder ColSpan(int value)
	{
		if (value <= 1)
		{
			_attributes.Remove("colspan");
		}
		else
		{
			_attributes["colspan"] = value.ToString(CultureInfo.InvariantCulture);
		}
		return this;
	}

	public UiElementBuilder RowSpan(int value)
	{
		if (value <= 1)
		{
			_attributes.Remove("rowspan");
		}
		else
		{
			_attributes["rowspan"] = value.ToString(CultureInfo.InvariantCulture);
		}
		return this;
	}

	public UiElementBuilder AriaInvalid(bool value = true)
	{
		_attributes["aria-invalid"] = (value ? "true" : "false");
		return this;
	}

	public UiElementBuilder Checked(bool value = true)
	{
		if (value)
		{
			_attributes["checked"] = "true";
		}
		else
		{
			_attributes.Remove("checked");
		}
		return this;
	}

	public UiElementBuilder Disabled(bool value = true)
	{
		if (value)
		{
			_attributes["disabled"] = "true";
		}
		else
		{
			_attributes.Remove("disabled");
		}
		return this;
	}

	public UiElementBuilder Text(string text)
	{
		_textContent = text;
		return this;
	}

	public UiElementBuilder Child(UiElementBuilder child)
	{
		_children.Add(child);
		return this;
	}

	public UiElementBuilder Children(params UiElementBuilder[] children)
	{
		_children.AddRange(children);
		return this;
	}

	public UiElementBuilder Width(float pixels)
	{
		_style = _style with
		{
			Width = UiLength.Px(pixels)
		};
		return this;
	}

	public UiElementBuilder WidthPercent(float percent)
	{
		_style = _style with
		{
			Width = UiLength.Percent(percent)
		};
		return this;
	}

	public UiElementBuilder Height(float pixels)
	{
		_style = _style with
		{
			Height = UiLength.Px(pixels)
		};
		return this;
	}

	public UiElementBuilder HeightPercent(float percent)
	{
		_style = _style with
		{
			Height = UiLength.Percent(percent)
		};
		return this;
	}

	public UiElementBuilder Gap(float pixels)
	{
		_style = _style with
		{
			Gap = pixels,
			RowGap = pixels,
			ColumnGap = pixels
		};
		return this;
	}

	public UiElementBuilder Wrap(UiFlexWrap value = UiFlexWrap.Wrap)
	{
		_style = _style with
		{
			FlexWrap = value
		};
		return this;
	}

	public UiElementBuilder AlignContent(UiAlignContent value)
	{
		_style = _style with
		{
			AlignContent = value
		};
		return this;
	}

	public UiElementBuilder Padding(float all)
	{
		_style = _style with
		{
			Padding = UiThickness.All(all)
		};
		return this;
	}

	public UiElementBuilder Padding(float horizontal, float vertical)
	{
		_style = _style with
		{
			Padding = UiThickness.Symmetric(horizontal, vertical)
		};
		return this;
	}

	public UiElementBuilder Margin(float all)
	{
		_style = _style with
		{
			Margin = UiThickness.All(all)
		};
		return this;
	}

	public UiElementBuilder Margin(float horizontal, float vertical)
	{
		_style = _style with
		{
			Margin = UiThickness.Symmetric(horizontal, vertical)
		};
		return this;
	}

	public UiElementBuilder Background(UiColor color)
	{
		_style = _style with
		{
			BackgroundColor = color
		};
		return this;
	}

	public UiElementBuilder Background(string color)
	{
		if (!UiColor.TryParse(color, out var color2))
		{
			throw new InvalidOperationException("Unsupported color literal '" + color + "'. Use hex or UiColor.");
		}
		return Background(color2);
	}

	public UiElementBuilder Color(UiColor color)
	{
		_style = _style with
		{
			Color = color
		};
		return this;
	}

	public UiElementBuilder Color(string color)
	{
		if (!UiColor.TryParse(color, out var color2))
		{
			throw new InvalidOperationException("Unsupported color literal '" + color + "'. Use hex or UiColor.");
		}
		return Color(color2);
	}

	public UiElementBuilder FontFamily(string familyName)
	{
		_style = _style with
		{
			FontFamily = familyName
		};
		return this;
	}

	public UiElementBuilder FontSize(float pixels)
	{
		_style = _style with
		{
			FontSize = pixels
		};
		return this;
	}

	public UiElementBuilder Bold()
	{
		_style = _style with
		{
			Bold = true
		};
		return this;
	}

	public UiElementBuilder Radius(float pixels)
	{
		_style = _style with
		{
			BorderRadius = pixels
		};
		return this;
	}

	public UiElementBuilder Outline(float width, UiColor color)
	{
		_style = _style with
		{
			OutlineWidth = width,
			OutlineColor = color
		};
		return this;
	}

	public UiElementBuilder BoxShadow(float offsetX, float offsetY, float blurRadius, UiColor color, float spreadRadius = 0f)
	{
		_style = _style with
		{
			BoxShadow = new UiShadow(offsetX, offsetY, blurRadius, spreadRadius, color)
		};
		return this;
	}

	public UiElementBuilder TextShadow(float offsetX, float offsetY, float blurRadius, UiColor color, float spreadRadius = 0f)
	{
		_style = _style with
		{
			TextShadow = new UiShadow(offsetX, offsetY, blurRadius, spreadRadius, color)
		};
		return this;
	}

	public UiElementBuilder Blur(float radius)
	{
		_style = _style with
		{
			FilterBlurRadius = Math.Max(0f, radius)
		};
		return this;
	}

	public UiElementBuilder BackdropBlur(float radius)
	{
		_style = _style with
		{
			BackdropBlurRadius = Math.Max(0f, radius)
		};
		return this;
	}

	public UiElementBuilder WhiteSpace(UiWhiteSpace value)
	{
		_style = _style with
		{
			WhiteSpace = value
		};
		return this;
	}

	public UiElementBuilder BackgroundGradient(float angleDegrees, params UiColor[] colors)
	{
		if (colors == null || colors.Length < 2)
		{
			throw new InvalidOperationException("BackgroundGradient requires at least two colors.");
		}
		UiGradientStop[] array = new UiGradientStop[colors.Length];
		for (int i = 0; i < colors.Length; i++)
		{
			float position = ((colors.Length == 1) ? 0f : ((float)i / (float)(colors.Length - 1)));
			array[i] = new UiGradientStop(position, colors[i]);
		}
		_style = _style with
		{
			BackgroundGradient = new UiLinearGradient(angleDegrees, array)
		};
		return this;
	}

	public UiElementBuilder Border(float width, UiColor color)
	{
		_style = _style with
		{
			BorderWidth = width,
			BorderColor = color
		};
		return this;
	}

	public UiElementBuilder FlexGrow(float value)
	{
		_style = _style with
		{
			FlexGrow = value
		};
		return this;
	}

	public UiElementBuilder FlexShrink(float value)
	{
		_style = _style with
		{
			FlexShrink = value
		};
		return this;
	}

	public UiElementBuilder FlexBasis(float pixels)
	{
		_style = _style with
		{
			FlexBasis = UiLength.Px(pixels)
		};
		return this;
	}

	public UiElementBuilder FlexBasisPercent(float percent)
	{
		_style = _style with
		{
			FlexBasis = UiLength.Percent(percent)
		};
		return this;
	}

	public UiElementBuilder Row()
	{
		_style = _style with
		{
			Display = UiDisplay.Flex,
			FlexDirection = UiFlexDirection.Row
		};
		return this;
	}

	public UiElementBuilder Column()
	{
		_style = _style with
		{
			Display = UiDisplay.Flex,
			FlexDirection = UiFlexDirection.Column
		};
		return this;
	}

	public UiElementBuilder Justify(UiJustifyContent value)
	{
		_style = _style with
		{
			JustifyContent = value
		};
		return this;
	}

	public UiElementBuilder Align(UiAlignItems value)
	{
		_style = _style with
		{
			AlignItems = value
		};
		return this;
	}

	public UiElementBuilder Absolute(float left, float top)
	{
		_style = _style with
		{
			PositionType = UiPositionType.Absolute,
			Left = UiLength.Px(left),
			Top = UiLength.Px(top)
		};
		return this;
	}

	public UiElementBuilder ZIndex(int value)
	{
		_style = _style with
		{
			ZIndex = value
		};
		return this;
	}

	public UiElementBuilder Overflow(UiOverflow value)
	{
		UiStyle uiStyle = _style with
		{
			Overflow = value
		};
		UiStyle uiStyle2 = uiStyle;
		bool clipContent = ((value == UiOverflow.Hidden || value == UiOverflow.Clip) ? true : false);
		uiStyle2.ClipContent = clipContent;
		_style = uiStyle;
		return this;
	}

	public UiElementBuilder Direction(UiTextDirection value)
	{
		_style = _style with
		{
			Direction = value
		};
		return this;
	}

	public UiElementBuilder TextAlign(UiTextAlign value)
	{
		_style = _style with
		{
			TextAlign = value
		};
		return this;
	}

	public UiElementBuilder ObjectFit(UiObjectFit value)
	{
		_style = _style with
		{
			ObjectFit = value
		};
		return this;
	}

	public UiElementBuilder ImageSlice(float all)
	{
		_style = _style with
		{
			ImageSlice = UiThickness.All(all)
		};
		return this;
	}

	public UiElementBuilder ImageSlice(float leftRight, float topBottom)
	{
		_style = _style with
		{
			ImageSlice = UiThickness.Symmetric(leftRight, topBottom)
		};
		return this;
	}

	public UiElementBuilder ImageSlice(float left, float top, float right, float bottom)
	{
		_style = _style with
		{
			ImageSlice = new UiThickness(left, top, right, bottom)
		};
		return this;
	}

	public UiElementBuilder Translate(float x, float y = 0f)
	{
		_style = _style with
		{
			Transform = _style.Transform.Append(UiTransformOperation.Translate(UiLength.Px(x), UiLength.Px(y)))
		};
		return this;
	}

	public UiElementBuilder TranslatePercent(float xPercent, float yPercent = 0f)
	{
		_style = _style with
		{
			Transform = _style.Transform.Append(UiTransformOperation.Translate(UiLength.Percent(xPercent), UiLength.Percent(yPercent)))
		};
		return this;
	}

	public UiElementBuilder Scale(float uniform)
	{
		_style = _style with
		{
			Transform = _style.Transform.Append(UiTransformOperation.Scale(uniform, uniform))
		};
		return this;
	}

	public UiElementBuilder Scale(float x, float y)
	{
		_style = _style with
		{
			Transform = _style.Transform.Append(UiTransformOperation.Scale(x, y))
		};
		return this;
	}

	public UiElementBuilder Rotate(float degrees)
	{
		_style = _style with
		{
			Transform = _style.Transform.Append(UiTransformOperation.Rotate(degrees))
		};
		return this;
	}

	public UiElementBuilder Transition(params UiTransitionEntry[] entries)
	{
		_style = _style with
		{
			Transition = ((entries.Length == 0) ? null : new UiTransitionSpec(entries))
		};
		return this;
	}

	public UiElementBuilder OnClick(Action<UiActionContext> handler)
	{
		_onClick = handler;
		return this;
	}

	public UiElementBuilder CanvasContent(UiCanvasContent content)
	{
		_canvasContent = content ?? throw new ArgumentNullException("content");
		return this;
	}

	public UiElementBuilder CanvasContent(Action<SKCanvas, SKRect> draw)
	{
		_canvasContent = new UiCanvasContent(draw);
		return this;
	}

	public UiNode Build(UiDispatcher dispatcher, ref int nextId)
	{
		UiActionHandle[] actionHandles = ((_onClick == null) ? Array.Empty<UiActionHandle>() : new UiActionHandle[1] { dispatcher.Register(_onClick) });
		UiNode[] array = new UiNode[_children.Count];
		for (int i = 0; i < _children.Count; i++)
		{
			array[i] = _children[i].Build(dispatcher, ref nextId);
		}
		UiAttributeBag uiAttributeBag = new UiAttributeBag();
		foreach (KeyValuePair<string, string> attribute in _attributes)
		{
			uiAttributeBag[attribute.Key] = attribute.Value;
		}
		if (!string.IsNullOrWhiteSpace(_elementId))
		{
			uiAttributeBag["id"] = _elementId;
		}
		if (_classNames.Count > 0)
		{
			uiAttributeBag["class"] = string.Join(' ', _classNames);
		}
		return new UiNode(new UiNodeId(nextId++), Kind, _style with
		{
			Id = _elementId,
			ClassName = string.Join(' ', _classNames)
		}, _textContent, array, actionHandles, TagName, _elementId, _classNames, uiAttributeBag, null, _canvasContent);
	}

	private static UiStyle CreateDefaultStyle(UiNodeKind kind)
	{
		if (1 == 0)
		{
		}
		UiStyle result;
		switch (kind)
		{
		case UiNodeKind.Row:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center
			};
			break;
		case UiNodeKind.Column:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column
			};
			break;
		case UiNodeKind.Text:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Text,
				Color = UiColor.White
			};
			break;
		case UiNodeKind.Button:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center,
				JustifyContent = UiJustifyContent.Center,
				Padding = UiThickness.Symmetric(16f, 10f),
				BackgroundColor = new UiColor(58, 121, 220),
				BorderRadius = 10f,
				Color = UiColor.White
			};
			break;
		case UiNodeKind.Checkbox:
		case UiNodeKind.Radio:
		case UiNodeKind.Toggle:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Center,
				Padding = UiThickness.Symmetric(10f, 8f),
				BorderRadius = 8f
			};
			break;
		case UiNodeKind.Table:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableHeader:
		case UiNodeKind.TableBody:
		case UiNodeKind.TableFooter:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableRow:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Row,
				AlignItems = UiAlignItems.Stretch
			};
			break;
		case UiNodeKind.TableCell:
		case UiNodeKind.TableHeaderCell:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				AlignItems = UiAlignItems.Stretch,
				FlexGrow = 1f,
				FlexShrink = 1f,
				FlexBasis = UiLength.Px(0f)
			};
			break;
		case UiNodeKind.Card:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column,
				Padding = UiThickness.All(16f),
				BackgroundColor = new UiColor(25, 31, 48),
				BorderRadius = 12f
			};
			break;
		case UiNodeKind.Panel:
			result = UiStyle.Default with
			{
				Display = UiDisplay.Flex,
				FlexDirection = UiFlexDirection.Column
			};
			break;
		default:
			result = UiStyle.Default;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}
}
