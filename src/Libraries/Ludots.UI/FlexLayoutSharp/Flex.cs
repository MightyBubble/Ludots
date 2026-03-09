using System;

namespace FlexLayoutSharp;

public class Flex
{
	internal class CachedMeasurement
	{
		internal float availableWidth;

		internal float availableHeight;

		internal MeasureMode widthMeasureMode = MeasureMode.Undefined;

		internal MeasureMode heightMeasureMode = MeasureMode.Undefined;

		internal float computedWidth = -1f;

		internal float computedHeight = -1f;

		internal void ResetToDefault()
		{
			availableHeight = 0f;
			availableWidth = 0f;
			widthMeasureMode = MeasureMode.Undefined;
			heightMeasureMode = MeasureMode.Undefined;
			computedWidth = -1f;
			computedHeight = -1f;
		}
	}

	internal class Layout
	{
		internal readonly float[] Position = new float[4];

		internal readonly float[] Dimensions = new float[2]
		{
			float.NaN,
			float.NaN
		};

		internal readonly float[] Margin = new float[6];

		internal readonly float[] Border = new float[6];

		internal readonly float[] Padding = new float[6];

		internal Direction Direction;

		internal int computedFlexBasisGeneration;

		internal float computedFlexBasis = float.NaN;

		internal bool HadOverflow = false;

		internal int generationCount;

		internal Direction lastParentDirection = Direction.NeverUsed_1;

		internal int nextCachedMeasurementsIndex = 0;

		internal readonly CachedMeasurement[] cachedMeasurements = new CachedMeasurement[16]
		{
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement(),
			new CachedMeasurement()
		};

		internal readonly float[] measuredDimensions = new float[2]
		{
			float.NaN,
			float.NaN
		};

		internal readonly CachedMeasurement cachedLayout = new CachedMeasurement();

		internal void ResetToDefault()
		{
			for (int i = 0; i < Position.Length; i++)
			{
				Position[i] = 0f;
			}
			for (int j = 0; j < Dimensions.Length; j++)
			{
				Dimensions[j] = float.NaN;
			}
			for (int k = 0; k < 6; k++)
			{
				Margin[k] = 0f;
				Border[k] = 0f;
				Padding[k] = 0f;
			}
			Direction = Direction.Inherit;
			computedFlexBasisGeneration = 0;
			computedFlexBasis = float.NaN;
			HadOverflow = false;
			generationCount = 0;
			lastParentDirection = Direction.NeverUsed_1;
			nextCachedMeasurementsIndex = 0;
			CachedMeasurement[] array = cachedMeasurements;
			foreach (CachedMeasurement cachedMeasurement in array)
			{
				cachedMeasurement.ResetToDefault();
			}
			for (int m = 0; m < measuredDimensions.Length; m++)
			{
				measuredDimensions[m] = float.NaN;
			}
			cachedLayout.ResetToDefault();
		}
	}

	internal static readonly Value ValueZero = new Value(0f, Unit.Point);

	internal static readonly Value ValueUndefined = new Value(float.NaN, Unit.Undefined);

	internal static readonly Value ValueAuto = new Value(float.NaN, Unit.Auto);

	internal static int currentGenerationCount = 0;

	internal static readonly Edge[] leading = new Edge[4]
	{
		Edge.Top,
		Edge.Bottom,
		Edge.Left,
		Edge.Right
	};

	internal static readonly Edge[] trailing = new Edge[4]
	{
		Edge.Bottom,
		Edge.Top,
		Edge.Right,
		Edge.Left
	};

	internal static readonly Edge[] pos = new Edge[4]
	{
		Edge.Top,
		Edge.Bottom,
		Edge.Left,
		Edge.Right
	};

	internal static readonly Dimension[] dim = new Dimension[4]
	{
		Dimension.Height,
		Dimension.Height,
		Dimension.Width,
		Dimension.Width
	};

	private const string spacerStr = "";

	internal static int gDepth = 0;

	private const bool gPrintTree = false;

	private const bool gPrintChanges = false;

	private const bool gPrintSkips = false;

	public static string AlignToString(Align value)
	{
		return value switch
		{
			Align.Auto => "auto", 
			Align.FlexStart => "flex-start", 
			Align.Center => "center", 
			Align.FlexEnd => "flex-end", 
			Align.Stretch => "stretch", 
			Align.Baseline => "baseline", 
			Align.SpaceBetween => "space-between", 
			Align.SpaceAround => "space-around", 
			_ => "unknown", 
		};
	}

	public static string DimensionToString(Dimension value)
	{
		return value switch
		{
			Dimension.Width => "width", 
			Dimension.Height => "height", 
			_ => "unknown", 
		};
	}

	public static string DirectionToString(Direction value)
	{
		return value switch
		{
			Direction.Inherit => "inherit", 
			Direction.LTR => "ltr", 
			Direction.RTL => "rtl", 
			_ => "unknown", 
		};
	}

	public static string DisplayToString(Display value)
	{
		return value switch
		{
			Display.Flex => "flex", 
			Display.None => "none", 
			_ => "unknown", 
		};
	}

	public static string EdgeToString(Edge value)
	{
		return value switch
		{
			Edge.Left => "left", 
			Edge.Top => "top", 
			Edge.Right => "right", 
			Edge.Bottom => "bottom", 
			Edge.Start => "start", 
			Edge.End => "end", 
			Edge.Horizontal => "horizontal", 
			Edge.Vertical => "vertical", 
			Edge.All => "all", 
			_ => "unknown", 
		};
	}

	public static string ExperimentalFeatureToString(ExperimentalFeature value)
	{
		if (value == ExperimentalFeature.WebFlexBasis)
		{
			return "web-flex-basis";
		}
		return "unknown";
	}

	public static string FlexDirectionToString(FlexDirection value)
	{
		return value switch
		{
			FlexDirection.Column => "column", 
			FlexDirection.ColumnReverse => "column-reverse", 
			FlexDirection.Row => "row", 
			FlexDirection.RowReverse => "row-reverse", 
			_ => "unknown", 
		};
	}

	public static string JustifyToString(Justify value)
	{
		return value switch
		{
			Justify.FlexStart => "flex-start", 
			Justify.Center => "center", 
			Justify.FlexEnd => "flex-end", 
			Justify.SpaceBetween => "space-between", 
			Justify.SpaceAround => "space-around", 
			_ => "unknown", 
		};
	}

	public static string LogLevelToString(LogLevel value)
	{
		return value switch
		{
			LogLevel.Error => "error", 
			LogLevel.Warn => "warn", 
			LogLevel.Info => "info", 
			LogLevel.Debug => "debug", 
			LogLevel.Verbose => "verbose", 
			LogLevel.Fatal => "fatal", 
			_ => "unknown", 
		};
	}

	public static string MeasureModeToString(MeasureMode value)
	{
		return value switch
		{
			MeasureMode.Undefined => "undefined", 
			MeasureMode.Exactly => "exactly", 
			MeasureMode.AtMost => "at-most", 
			_ => "unknown", 
		};
	}

	public static string NodeTypeToString(NodeType value)
	{
		return value switch
		{
			NodeType.Default => "default", 
			NodeType.Text => "text", 
			_ => "unknown", 
		};
	}

	public static string OverflowToString(Overflow value)
	{
		return value switch
		{
			Overflow.Visible => "visible", 
			Overflow.Hidden => "hidden", 
			Overflow.Scroll => "scroll", 
			_ => "unknown", 
		};
	}

	public static string PositionTypeToString(PositionType value)
	{
		return value switch
		{
			PositionType.Relative => "relative", 
			PositionType.Absolute => "absolute", 
			_ => "unknown", 
		};
	}

	public static string PrintOptionsToString(PrintOptions value)
	{
		return value switch
		{
			PrintOptions.Layout => "layout", 
			PrintOptions.Style => "style", 
			PrintOptions.Children => "children", 
			_ => "unknown", 
		};
	}

	public static string UnitToString(Unit value)
	{
		return value switch
		{
			Unit.Undefined => "undefined", 
			Unit.Point => "point", 
			Unit.Percent => "percent", 
			Unit.Auto => "auto", 
			_ => "unknown", 
		};
	}

	public static string WrapToString(Wrap value)
	{
		return value switch
		{
			Wrap.NoWrap => "no-wrap", 
			Wrap.Wrap => "wrap", 
			Wrap.WrapReverse => "wrap-reverse", 
			_ => "unknown", 
		};
	}

	public static bool StringToAlign(string value, out Align result)
	{
		switch (value)
		{
		case "auto":
			result = Align.Auto;
			return true;
		case "flex-start":
			result = Align.FlexStart;
			return true;
		case "center":
			result = Align.Center;
			return true;
		case "flex-end":
			result = Align.FlexEnd;
			return true;
		case "stretch":
			result = Align.Stretch;
			return true;
		case "baseline":
			result = Align.Baseline;
			return true;
		case "space-between":
			result = Align.SpaceBetween;
			return true;
		case "space-around":
			result = Align.SpaceAround;
			return true;
		default:
			result = Align.Auto;
			return true;
		}
	}

	public static bool StringToDimension(string value, out Dimension result)
	{
		if (!(value == "width"))
		{
			if (value == "height")
			{
				result = Dimension.Height;
				return true;
			}
			result = Dimension.Width;
			return true;
		}
		result = Dimension.Width;
		return true;
	}

	public static bool StringToDirection(string value, out Direction result)
	{
		switch (value)
		{
		case "inherit":
			result = Direction.Inherit;
			return true;
		case "ltr":
			result = Direction.LTR;
			return true;
		case "rtl":
			result = Direction.RTL;
			return true;
		default:
			result = Direction.Inherit;
			return true;
		}
	}

	public static bool StringToDisplay(string value, out Display result)
	{
		if (!(value == "flex"))
		{
			if (value == "none")
			{
				result = Display.None;
				return true;
			}
			result = Display.Flex;
			return true;
		}
		result = Display.Flex;
		return true;
	}

	public static bool StringToEdge(string value, out Edge result)
	{
		switch (value)
		{
		case "left":
			result = Edge.Left;
			return true;
		case "top":
			result = Edge.Top;
			return true;
		case "right":
			result = Edge.Right;
			return true;
		case "bottom":
			result = Edge.Bottom;
			return true;
		case "start":
			result = Edge.Start;
			return true;
		case "end":
			result = Edge.End;
			return true;
		case "horizontal":
			result = Edge.Horizontal;
			return true;
		case "vertical":
			result = Edge.Vertical;
			return true;
		case "all":
			result = Edge.All;
			return true;
		default:
			result = Edge.Left;
			return true;
		}
	}

	public static bool StringToExperimentalFeature(string value, out ExperimentalFeature result)
	{
		if (value == "web-flex-basis")
		{
			result = ExperimentalFeature.WebFlexBasis;
			return true;
		}
		result = ExperimentalFeature.WebFlexBasis;
		return true;
	}

	public static bool StringToFlexDirection(string value, out FlexDirection result)
	{
		switch (value)
		{
		case "column":
			result = FlexDirection.Column;
			return true;
		case "column-reverse":
			result = FlexDirection.ColumnReverse;
			return true;
		case "row":
			result = FlexDirection.Row;
			return true;
		case "row-reverse":
			result = FlexDirection.RowReverse;
			return true;
		default:
			result = FlexDirection.Column;
			return true;
		}
	}

	public static bool StringToJustify(string value, out Justify result)
	{
		switch (value)
		{
		case "flex-start":
			result = Justify.FlexStart;
			return true;
		case "center":
			result = Justify.Center;
			return true;
		case "flex-end":
			result = Justify.FlexEnd;
			return true;
		case "space-between":
			result = Justify.SpaceBetween;
			return true;
		case "space-around":
			result = Justify.SpaceAround;
			return true;
		default:
			result = Justify.FlexStart;
			return true;
		}
	}

	public static bool StringToLogLevel(string value, out LogLevel result)
	{
		switch (value)
		{
		case "error":
			result = LogLevel.Error;
			return true;
		case "warn":
			result = LogLevel.Warn;
			return true;
		case "info":
			result = LogLevel.Info;
			return true;
		case "debug":
			result = LogLevel.Debug;
			return true;
		case "verbose":
			result = LogLevel.Verbose;
			return true;
		case "fatal":
			result = LogLevel.Fatal;
			return true;
		default:
			result = LogLevel.Error;
			return true;
		}
	}

	public static bool StringToMeasureMode(string value, out MeasureMode result)
	{
		switch (value)
		{
		case "undefined":
			result = MeasureMode.Undefined;
			return true;
		case "exactly":
			result = MeasureMode.Exactly;
			return true;
		case "at-most":
			result = MeasureMode.AtMost;
			return true;
		default:
			result = MeasureMode.Undefined;
			return true;
		}
	}

	public static bool StringToNodeType(string value, out NodeType result)
	{
		if (!(value == "default"))
		{
			if (value == "text")
			{
				result = NodeType.Text;
				return true;
			}
			result = NodeType.Default;
			return true;
		}
		result = NodeType.Default;
		return true;
	}

	public static bool StringToOverflow(string value, out Overflow result)
	{
		switch (value)
		{
		case "visible":
			result = Overflow.Visible;
			return true;
		case "hidden":
			result = Overflow.Hidden;
			return true;
		case "scroll":
			result = Overflow.Scroll;
			return true;
		default:
			result = Overflow.Visible;
			return true;
		}
	}

	public static bool StringToPositionType(string value, out PositionType result)
	{
		if (!(value == "relative"))
		{
			if (value == "absolute")
			{
				result = PositionType.Absolute;
				return true;
			}
			result = PositionType.Relative;
			return true;
		}
		result = PositionType.Relative;
		return true;
	}

	public static bool StringToPrintOptions(string value, out PrintOptions result)
	{
		switch (value)
		{
		case "layout":
			result = PrintOptions.Layout;
			return true;
		case "style":
			result = PrintOptions.Style;
			return true;
		case "children":
			result = PrintOptions.Children;
			return true;
		default:
			result = PrintOptions.Layout;
			return true;
		}
	}

	public static bool StringToUnit(string value, out Unit result)
	{
		switch (value)
		{
		case "undefined":
			result = Unit.Undefined;
			return true;
		case "point":
			result = Unit.Point;
			return true;
		case "percent":
			result = Unit.Percent;
			return true;
		case "auto":
			result = Unit.Auto;
			return true;
		default:
			result = Unit.Undefined;
			return true;
		}
	}

	public static bool StringToWrap(string value, out Wrap result)
	{
		switch (value)
		{
		case "no-wrap":
			result = Wrap.NoWrap;
			return true;
		case "wrap":
			result = Wrap.Wrap;
			return true;
		case "wrap-reverse":
			result = Wrap.WrapReverse;
			return true;
		default:
			result = Wrap.NoWrap;
			return true;
		}
	}

	internal static bool feq(float a, float b)
	{
		if (float.IsNaN(a) && float.IsNaN(b))
		{
			return true;
		}
		return a == b;
	}

	internal static bool valueEq(Value v1, Value v2)
	{
		if (v1.unit != v2.unit)
		{
			return false;
		}
		return feq(v1.value, v2.value);
	}

	internal static Value computedEdgeValue(Value[] edges, Edge edge, Value defaultValue)
	{
		if (edges[(int)edge].unit != Unit.Undefined)
		{
			return edges[(int)edge];
		}
		if ((edge == Edge.Top || edge == Edge.Bottom) && edges[7].unit != Unit.Undefined)
		{
			return edges[7];
		}
		if ((edge == Edge.Left || edge == Edge.Right || edge == Edge.Start || edge == Edge.End) && edges[6].unit != Unit.Undefined)
		{
			return edges[6];
		}
		if (edges[8].unit != Unit.Undefined)
		{
			return edges[8];
		}
		if (edge == Edge.Start || edge == Edge.End)
		{
			return ValueUndefined;
		}
		return defaultValue;
	}

	internal static float resolveValue(Value value, float parentSize)
	{
		switch (value.unit)
		{
		case Unit.Undefined:
		case Unit.Auto:
			return float.NaN;
		case Unit.Point:
			return value.value;
		case Unit.Percent:
			return value.value * parentSize / 100f;
		default:
			return float.NaN;
		}
	}

	internal static float resolveValueMargin(Value value, float parentSize)
	{
		if (value.unit == Unit.Auto)
		{
			return 0f;
		}
		return resolveValue(value, parentSize);
	}

	internal static Node NewNodeWithConfig(Config config)
	{
		Node node = CreateDefaultNode();
		if (config.UseWebDefaults)
		{
			node.nodeStyle.FlexDirection = FlexDirection.Row;
			node.nodeStyle.AlignContent = Align.Stretch;
		}
		node.config = config;
		return node;
	}

	internal static Node NewNode()
	{
		return NewNodeWithConfig(CreateDefaultConfig());
	}

	internal static Config ConfigGetDefault()
	{
		return CreateDefaultConfig();
	}

	internal static Config NewConfig()
	{
		return CreateDefaultConfig();
	}

	internal static void ConfigCopy(Config dest, Config src)
	{
		Config.Copy(dest, src);
	}

	internal static void nodeMarkDirtyInternal(Node node)
	{
		if (!node.IsDirty)
		{
			node.IsDirty = true;
			node.nodeLayout.computedFlexBasis = float.NaN;
			if (node.Parent != null)
			{
				nodeMarkDirtyInternal(node.Parent);
			}
		}
	}

	internal static void SetMeasureFunc(Node node, MeasureFunc measureFunc)
	{
		if (measureFunc == null)
		{
			node.measureFunc = null;
			node.NodeType = NodeType.Default;
		}
		else
		{
			assertWithNode(node, node.Children.Count == 0, "Cannot set measure function: Nodes with measure functions cannot have children.");
			node.measureFunc = measureFunc;
			node.NodeType = NodeType.Text;
		}
	}

	internal static void InsertChild(Node node, Node child, int idx)
	{
		assertWithNode(node, child.Parent == null, "Child already has a parent, it must be removed first.");
		assertWithNode(node, node.measureFunc == null, "Cannot add child: Nodes with measure functions cannot have children.");
		node.Children.Insert(idx, child);
		child.Parent = node;
		nodeMarkDirtyInternal(node);
	}

	internal static void RemoveChild(Node node, Node child)
	{
		if (node.Children.Remove(child))
		{
			child.nodeLayout.ResetToDefault();
			child.Parent = null;
			nodeMarkDirtyInternal(node);
		}
	}

	internal static Node GetChild(Node node, int idx)
	{
		return (idx < node.Children.Count) ? node.Children[idx] : null;
	}

	internal static void MarkDirty(Node node)
	{
		assertWithNode(node, node.measureFunc != null, "Only leaf nodes with custom measure functions should manually mark themselves as dirty");
		nodeMarkDirtyInternal(node);
	}

	internal static bool styleEq(Style s1, Style s2)
	{
		if (s1.Direction != s2.Direction || s1.FlexDirection != s2.FlexDirection || s1.JustifyContent != s2.JustifyContent || s1.AlignContent != s2.AlignContent || s1.AlignItems != s2.AlignItems || s1.AlignSelf != s2.AlignSelf || s1.PositionType != s2.PositionType || s1.FlexWrap != s2.FlexWrap || s1.Overflow != s2.Overflow || s1.Display != s2.Display || !feq(s1.Flex, s2.Flex) || !feq(s1.FlexGrow, s2.FlexGrow) || !feq(s1.FlexShrink, s2.FlexShrink) || !valueEq(s1.FlexBasis, s2.FlexBasis))
		{
			return false;
		}
		for (int i = 0; i < 9; i++)
		{
			if (!valueEq(s1.Margin[i], s2.Margin[i]) || !valueEq(s1.Position[i], s2.Position[i]) || !valueEq(s1.Padding[i], s2.Padding[i]) || !valueEq(s1.Border[i], s2.Border[i]))
			{
				return false;
			}
		}
		for (int j = 0; j < 2; j++)
		{
			if (!valueEq(s1.Dimensions[j], s2.Dimensions[j]) || !valueEq(s1.MinDimensions[j], s2.MinDimensions[j]) || !valueEq(s1.MaxDimensions[j], s2.MaxDimensions[j]))
			{
				return false;
			}
		}
		return true;
	}

	internal static float resolveFlexGrow(Node node)
	{
		if (node.Parent == null)
		{
			return 0f;
		}
		if (!FloatIsUndefined(node.nodeStyle.FlexGrow))
		{
			return node.nodeStyle.FlexGrow;
		}
		if (!FloatIsUndefined(node.nodeStyle.Flex) && node.nodeStyle.Flex > 0f)
		{
			return node.nodeStyle.Flex;
		}
		return 0f;
	}

	internal static float nodeResolveFlexShrink(Node node)
	{
		if (node.Parent == null)
		{
			return 0f;
		}
		if (!FloatIsUndefined(node.nodeStyle.FlexShrink))
		{
			return node.nodeStyle.FlexShrink;
		}
		if (!node.config.UseWebDefaults && !FloatIsUndefined(node.nodeStyle.Flex) && node.nodeStyle.Flex < 0f)
		{
			return 0f - node.nodeStyle.Flex;
		}
		if (node.config.UseWebDefaults)
		{
			return 1f;
		}
		return 0f;
	}

	internal static Value nodeResolveFlexBasisPtr(Node node)
	{
		Style nodeStyle = node.nodeStyle;
		if (nodeStyle.FlexBasis.unit != Unit.Auto && nodeStyle.FlexBasis.unit != Unit.Undefined)
		{
			return nodeStyle.FlexBasis;
		}
		if (!FloatIsUndefined(nodeStyle.Flex) && nodeStyle.Flex > 0f)
		{
			if (node.config.UseWebDefaults)
			{
				return ValueAuto;
			}
			return ValueZero;
		}
		return ValueAuto;
	}

	internal static bool FloatIsUndefined(float value)
	{
		return float.IsNaN(value);
	}

	internal static bool ValueEqual(Value a, Value b)
	{
		if (a.unit != b.unit)
		{
			return false;
		}
		if (a.unit == Unit.Undefined)
		{
			return true;
		}
		return Math.Abs(a.value - b.value) < 0.0001f;
	}

	internal static void resolveDimensions(Node node)
	{
		for (int i = 0; i <= 1; i++)
		{
			if (node.nodeStyle.MaxDimensions[i].unit != Unit.Undefined && ValueEqual(node.nodeStyle.MaxDimensions[i], node.nodeStyle.MinDimensions[i]))
			{
				node.resolvedDimensions[i] = node.nodeStyle.MaxDimensions[i];
			}
			else
			{
				node.resolvedDimensions[i] = node.nodeStyle.Dimensions[i];
			}
		}
	}

	internal static bool flexDirectionIsRow(FlexDirection flexDirection)
	{
		return flexDirection == FlexDirection.Row || flexDirection == FlexDirection.RowReverse;
	}

	internal static bool flexDirectionIsColumn(FlexDirection flexDirection)
	{
		return flexDirection == FlexDirection.Column || flexDirection == FlexDirection.ColumnReverse;
	}

	internal static float nodeLeadingMargin(Node node, FlexDirection axis, float widthSize)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Margin[4].unit != Unit.Undefined)
		{
			return resolveValueMargin(node.nodeStyle.Margin[4], widthSize);
		}
		Value value = computedEdgeValue(node.nodeStyle.Margin, leading[(int)axis], ValueZero);
		return resolveValueMargin(value, widthSize);
	}

	internal static float nodeTrailingMargin(Node node, FlexDirection axis, float widthSize)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Margin[5].unit != Unit.Undefined)
		{
			return resolveValueMargin(node.nodeStyle.Margin[5], widthSize);
		}
		return resolveValueMargin(computedEdgeValue(node.nodeStyle.Margin, trailing[(int)axis], ValueZero), widthSize);
	}

	internal static float nodeLeadingPadding(Node node, FlexDirection axis, float widthSize)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Padding[4].unit != Unit.Undefined && resolveValue(node.nodeStyle.Padding[4], widthSize) >= 0f)
		{
			return resolveValue(node.nodeStyle.Padding[4], widthSize);
		}
		return InnerFunc.fmaxf(resolveValue(computedEdgeValue(node.nodeStyle.Padding, leading[(int)axis], ValueZero), widthSize), 0f);
	}

	internal static float nodeTrailingPadding(Node node, FlexDirection axis, float widthSize)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Padding[5].unit != Unit.Undefined && resolveValue(node.nodeStyle.Padding[5], widthSize) >= 0f)
		{
			return resolveValue(node.nodeStyle.Padding[5], widthSize);
		}
		return InnerFunc.fmaxf(resolveValue(computedEdgeValue(node.nodeStyle.Padding, trailing[(int)axis], ValueZero), widthSize), 0f);
	}

	internal static float nodeLeadingBorder(Node node, FlexDirection axis)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Border[4].unit != Unit.Undefined && node.nodeStyle.Border[4].value >= 0f)
		{
			return node.nodeStyle.Border[4].value;
		}
		return InnerFunc.fmaxf(computedEdgeValue(node.nodeStyle.Border, leading[(int)axis], ValueZero).value, 0f);
	}

	internal static float nodeTrailingBorder(Node node, FlexDirection axis)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Border[5].unit != Unit.Undefined && node.nodeStyle.Border[5].value >= 0f)
		{
			return node.nodeStyle.Border[5].value;
		}
		return InnerFunc.fmaxf(computedEdgeValue(node.nodeStyle.Border, trailing[(int)axis], ValueZero).value, 0f);
	}

	internal static float nodeLeadingPaddingAndBorder(Node node, FlexDirection axis, float widthSize)
	{
		return nodeLeadingPadding(node, axis, widthSize) + nodeLeadingBorder(node, axis);
	}

	internal static float nodeTrailingPaddingAndBorder(Node node, FlexDirection axis, float widthSize)
	{
		return nodeTrailingPadding(node, axis, widthSize) + nodeTrailingBorder(node, axis);
	}

	internal static float nodeMarginForAxis(Node node, FlexDirection axis, float widthSize)
	{
		float num = nodeLeadingMargin(node, axis, widthSize);
		float num2 = nodeTrailingMargin(node, axis, widthSize);
		return num + num2;
	}

	internal static float nodePaddingAndBorderForAxis(Node node, FlexDirection axis, float widthSize)
	{
		return nodeLeadingPaddingAndBorder(node, axis, widthSize) + nodeTrailingPaddingAndBorder(node, axis, widthSize);
	}

	internal static Align nodeAlignItem(Node node, Node child)
	{
		Align align = child.nodeStyle.AlignSelf;
		if (child.nodeStyle.AlignSelf == Align.Auto)
		{
			align = node.nodeStyle.AlignItems;
		}
		if (align == Align.Baseline && flexDirectionIsColumn(node.nodeStyle.FlexDirection))
		{
			return Align.FlexStart;
		}
		return align;
	}

	internal static Direction nodeResolveDirection(Node node, Direction parentDirection)
	{
		if (node.nodeStyle.Direction == Direction.Inherit)
		{
			if (parentDirection > Direction.Inherit)
			{
				return parentDirection;
			}
			return Direction.LTR;
		}
		return node.nodeStyle.Direction;
	}

	internal static float Baseline(Node node)
	{
		if (node.baselineFunc != null)
		{
			float num = node.baselineFunc(node, node.nodeLayout.measuredDimensions[0], node.nodeLayout.measuredDimensions[1]);
			assertWithNode(node, !FloatIsUndefined(num), "Expect custom baseline function to not return NaN");
			return num;
		}
		Node node2 = null;
		foreach (Node child in node.Children)
		{
			if (child.lineIndex > 0)
			{
				break;
			}
			if (child.nodeStyle.PositionType != PositionType.Absolute)
			{
				if (nodeAlignItem(node, child) == Align.Baseline)
				{
					node2 = child;
					break;
				}
				if (node2 == null)
				{
					node2 = child;
				}
			}
		}
		if (node2 == null)
		{
			return node.nodeLayout.measuredDimensions[1];
		}
		float num2 = Baseline(node2);
		return num2 + node2.nodeLayout.Position[1];
	}

	internal static FlexDirection resolveFlexDirection(FlexDirection flexDirection, Direction direction)
	{
		if (direction == Direction.RTL)
		{
			switch (flexDirection)
			{
			case FlexDirection.Row:
				return FlexDirection.RowReverse;
			case FlexDirection.RowReverse:
				return FlexDirection.Row;
			}
		}
		return flexDirection;
	}

	internal static FlexDirection flexDirectionCross(FlexDirection flexDirection, Direction direction)
	{
		if (flexDirectionIsColumn(flexDirection))
		{
			return resolveFlexDirection(FlexDirection.Row, direction);
		}
		return FlexDirection.Column;
	}

	internal static bool nodeIsFlex(Node node)
	{
		return node.nodeStyle.PositionType == PositionType.Relative && (resolveFlexGrow(node) != 0f || nodeResolveFlexShrink(node) != 0f);
	}

	internal static bool isBaselineLayout(Node node)
	{
		if (flexDirectionIsColumn(node.nodeStyle.FlexDirection))
		{
			return false;
		}
		if (node.nodeStyle.AlignItems == Align.Baseline)
		{
			return true;
		}
		foreach (Node child in node.Children)
		{
			if (child.nodeStyle.PositionType == PositionType.Relative && child.nodeStyle.AlignSelf == Align.Baseline)
			{
				return true;
			}
		}
		return false;
	}

	internal static float nodeDimWithMargin(Node node, FlexDirection axis, float widthSize)
	{
		return node.nodeLayout.measuredDimensions[(int)dim[(int)axis]] + nodeLeadingMargin(node, axis, widthSize) + nodeTrailingMargin(node, axis, widthSize);
	}

	internal static bool nodeIsStyleDimDefined(Node node, FlexDirection axis, float parentSize)
	{
		Value value = node.resolvedDimensions[(int)dim[(int)axis]];
		bool flag = value.unit == Unit.Auto || value.unit == Unit.Undefined || (value.unit == Unit.Point && value.value < 0f) || (value.unit == Unit.Percent && (value.value < 0f || FloatIsUndefined(parentSize)));
		return !flag;
	}

	internal static bool nodeIsLayoutDimDefined(Node node, FlexDirection axis)
	{
		float num = node.nodeLayout.measuredDimensions[(int)dim[(int)axis]];
		return !FloatIsUndefined(num) && num >= 0f;
	}

	internal static bool nodeIsLeadingPosDefined(Node node, FlexDirection axis)
	{
		return (flexDirectionIsRow(axis) && computedEdgeValue(node.nodeStyle.Position, Edge.Start, ValueUndefined).unit != Unit.Undefined) || computedEdgeValue(node.nodeStyle.Position, leading[(int)axis], ValueUndefined).unit != Unit.Undefined;
	}

	internal static bool nodeIsTrailingPosDefined(Node node, FlexDirection axis)
	{
		return (flexDirectionIsRow(axis) && computedEdgeValue(node.nodeStyle.Position, Edge.End, ValueUndefined).unit != Unit.Undefined) || computedEdgeValue(node.nodeStyle.Position, trailing[(int)axis], ValueUndefined).unit != Unit.Undefined;
	}

	internal static float nodeLeadingPosition(Node node, FlexDirection axis, float axisSize)
	{
		if (flexDirectionIsRow(axis))
		{
			Value value = computedEdgeValue(node.nodeStyle.Position, Edge.Start, ValueUndefined);
			if (value.unit != Unit.Undefined)
			{
				return resolveValue(value, axisSize);
			}
		}
		Value value2 = computedEdgeValue(node.nodeStyle.Position, leading[(int)axis], ValueUndefined);
		if (value2.unit == Unit.Undefined)
		{
			return 0f;
		}
		return resolveValue(value2, axisSize);
	}

	internal static float nodeTrailingPosition(Node node, FlexDirection axis, float axisSize)
	{
		if (flexDirectionIsRow(axis))
		{
			Value value = computedEdgeValue(node.nodeStyle.Position, Edge.End, ValueUndefined);
			if (value.unit != Unit.Undefined)
			{
				return resolveValue(value, axisSize);
			}
		}
		Value value2 = computedEdgeValue(node.nodeStyle.Position, trailing[(int)axis], ValueUndefined);
		if (value2.unit == Unit.Undefined)
		{
			return 0f;
		}
		return resolveValue(value2, axisSize);
	}

	internal static float nodeBoundAxisWithinMinAndMax(Node node, FlexDirection axis, float value, float axisSize)
	{
		float num = float.NaN;
		float num2 = float.NaN;
		if (flexDirectionIsColumn(axis))
		{
			num = resolveValue(node.nodeStyle.MinDimensions[1], axisSize);
			num2 = resolveValue(node.nodeStyle.MaxDimensions[1], axisSize);
		}
		else if (flexDirectionIsRow(axis))
		{
			num = resolveValue(node.nodeStyle.MinDimensions[0], axisSize);
			num2 = resolveValue(node.nodeStyle.MaxDimensions[0], axisSize);
		}
		float num3 = value;
		if (!FloatIsUndefined(num2) && num2 >= 0f && num3 > num2)
		{
			num3 = num2;
		}
		if (!FloatIsUndefined(num) && num >= 0f && num3 < num)
		{
			num3 = num;
		}
		return num3;
	}

	internal static Value marginLeadingValue(Node node, FlexDirection axis)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Margin[4].unit != Unit.Undefined)
		{
			return node.nodeStyle.Margin[4];
		}
		return node.nodeStyle.Margin[(int)leading[(int)axis]];
	}

	internal static Value marginTrailingValue(Node node, FlexDirection axis)
	{
		if (flexDirectionIsRow(axis) && node.nodeStyle.Margin[5].unit != Unit.Undefined)
		{
			return node.nodeStyle.Margin[5];
		}
		return node.nodeStyle.Margin[(int)trailing[(int)axis]];
	}

	internal static float nodeBoundAxis(Node node, FlexDirection axis, float value, float axisSize, float widthSize)
	{
		return InnerFunc.fmaxf(nodeBoundAxisWithinMinAndMax(node, axis, value, axisSize), nodePaddingAndBorderForAxis(node, axis, widthSize));
	}

	internal static void nodeSetChildTrailingPosition(Node node, Node child, FlexDirection axis)
	{
		float num = child.nodeLayout.measuredDimensions[(int)dim[(int)axis]];
		child.nodeLayout.Position[(int)trailing[(int)axis]] = node.nodeLayout.measuredDimensions[(int)dim[(int)axis]] - num - child.nodeLayout.Position[(int)pos[(int)axis]];
	}

	internal static float nodeRelativePosition(Node node, FlexDirection axis, float axisSize)
	{
		if (nodeIsLeadingPosDefined(node, axis))
		{
			return nodeLeadingPosition(node, axis, axisSize);
		}
		return 0f - nodeTrailingPosition(node, axis, axisSize);
	}

	internal static void constrainMaxSizeForMode(Node node, FlexDirection axis, float parentAxisSize, float parentWidth, ref MeasureMode mode, ref float size)
	{
		float num = resolveValue(node.nodeStyle.MaxDimensions[(int)dim[(int)axis]], parentAxisSize) + nodeMarginForAxis(node, axis, parentWidth);
		switch (mode)
		{
		case MeasureMode.Exactly:
		case MeasureMode.AtMost:
			if (!FloatIsUndefined(num) && !(size < num))
			{
				size = num;
			}
			break;
		case MeasureMode.Undefined:
			if (!FloatIsUndefined(num))
			{
				mode = MeasureMode.AtMost;
				size = num;
			}
			break;
		}
	}

	internal static void nodeSetPosition(Node node, Direction direction, float mainSize, float crossSize, float parentWidth)
	{
		Direction direction2 = Direction.LTR;
		if (node.Parent != null)
		{
			direction2 = direction;
		}
		FlexDirection flexDirection = resolveFlexDirection(node.nodeStyle.FlexDirection, direction2);
		FlexDirection flexDirection2 = flexDirectionCross(flexDirection, direction2);
		float num = nodeRelativePosition(node, flexDirection, mainSize);
		float num2 = nodeRelativePosition(node, flexDirection2, crossSize);
		float[] position = node.nodeLayout.Position;
		position[(int)leading[(int)flexDirection]] = nodeLeadingMargin(node, flexDirection, parentWidth) + num;
		position[(int)trailing[(int)flexDirection]] = nodeTrailingMargin(node, flexDirection, parentWidth) + num;
		position[(int)leading[(int)flexDirection2]] = nodeLeadingMargin(node, flexDirection2, parentWidth) + num2;
		position[(int)trailing[(int)flexDirection2]] = nodeTrailingMargin(node, flexDirection2, parentWidth) + num2;
	}

	internal static void nodeComputeFlexBasisForChild(Node node, Node child, float width, MeasureMode widthMode, float height, float parentWidth, float parentHeight, MeasureMode heightMode, Direction direction, Config config)
	{
		FlexDirection flexDirection = resolveFlexDirection(node.nodeStyle.FlexDirection, direction);
		bool flag = flexDirectionIsRow(flexDirection);
		float value = height;
		float parentSize = parentHeight;
		if (flag)
		{
			value = width;
			parentSize = parentWidth;
		}
		float num = resolveValue(nodeResolveFlexBasisPtr(child), parentSize);
		bool flag2 = nodeIsStyleDimDefined(child, FlexDirection.Row, parentWidth);
		bool flag3 = nodeIsStyleDimDefined(child, FlexDirection.Column, parentHeight);
		if (!FloatIsUndefined(num) && !FloatIsUndefined(value))
		{
			if (FloatIsUndefined(child.nodeLayout.computedFlexBasis) || (child.config.IsExperimentalFeatureEnabled(ExperimentalFeature.WebFlexBasis) && child.nodeLayout.computedFlexBasisGeneration != currentGenerationCount))
			{
				child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf(num, nodePaddingAndBorderForAxis(child, flexDirection, parentWidth));
			}
		}
		else if (flag && flag2)
		{
			child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf(resolveValue(child.resolvedDimensions[0], parentWidth), nodePaddingAndBorderForAxis(child, FlexDirection.Row, parentWidth));
		}
		else if (!flag && flag3)
		{
			child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf(resolveValue(child.resolvedDimensions[1], parentHeight), nodePaddingAndBorderForAxis(child, FlexDirection.Column, parentWidth));
		}
		else
		{
			float size = float.NaN;
			float size2 = float.NaN;
			MeasureMode mode = MeasureMode.Undefined;
			MeasureMode mode2 = MeasureMode.Undefined;
			float num2 = nodeMarginForAxis(child, FlexDirection.Row, parentWidth);
			float num3 = nodeMarginForAxis(child, FlexDirection.Column, parentWidth);
			if (flag2)
			{
				size = resolveValue(child.resolvedDimensions[0], parentWidth) + num2;
				mode = MeasureMode.Exactly;
			}
			if (flag3)
			{
				size2 = resolveValue(child.resolvedDimensions[1], parentHeight) + num3;
				mode2 = MeasureMode.Exactly;
			}
			if (((!flag && node.nodeStyle.Overflow == Overflow.Scroll) || node.nodeStyle.Overflow != Overflow.Scroll) && FloatIsUndefined(size) && !FloatIsUndefined(width))
			{
				size = width;
				mode = MeasureMode.AtMost;
			}
			if (((flag && node.nodeStyle.Overflow == Overflow.Scroll) || node.nodeStyle.Overflow != Overflow.Scroll) && FloatIsUndefined(size2) && !FloatIsUndefined(height))
			{
				size2 = height;
				mode2 = MeasureMode.AtMost;
			}
			if (!flag && !FloatIsUndefined(width) && !flag2 && widthMode == MeasureMode.Exactly && nodeAlignItem(node, child) == Align.Stretch)
			{
				size = width;
				mode = MeasureMode.Exactly;
			}
			if (flag && !FloatIsUndefined(height) && !flag3 && heightMode == MeasureMode.Exactly && nodeAlignItem(node, child) == Align.Stretch)
			{
				size2 = height;
				mode2 = MeasureMode.Exactly;
			}
			if (!FloatIsUndefined(child.nodeStyle.AspectRatio))
			{
				if (!flag && mode == MeasureMode.Exactly)
				{
					child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf((size - num2) / child.nodeStyle.AspectRatio, nodePaddingAndBorderForAxis(child, FlexDirection.Column, parentWidth));
					return;
				}
				if (flag && mode2 == MeasureMode.Exactly)
				{
					child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf((size2 - num3) * child.nodeStyle.AspectRatio, nodePaddingAndBorderForAxis(child, FlexDirection.Row, parentWidth));
					return;
				}
			}
			constrainMaxSizeForMode(child, FlexDirection.Row, parentWidth, parentWidth, ref mode, ref size);
			constrainMaxSizeForMode(child, FlexDirection.Column, parentHeight, parentWidth, ref mode2, ref size2);
			layoutNodeInternal(child, size, size2, direction, mode, mode2, parentWidth, parentHeight, performLayout: false, "measure", config);
			child.nodeLayout.computedFlexBasis = InnerFunc.fmaxf(child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]], nodePaddingAndBorderForAxis(child, flexDirection, parentWidth));
		}
		child.nodeLayout.computedFlexBasisGeneration = currentGenerationCount;
	}

	internal static void nodeAbsoluteLayoutChild(Node node, Node child, float width, MeasureMode widthMode, float height, Direction direction, Config config)
	{
		FlexDirection flexDirection = resolveFlexDirection(node.nodeStyle.FlexDirection, direction);
		FlexDirection flexDirection2 = flexDirectionCross(flexDirection, direction);
		bool flag = flexDirectionIsRow(flexDirection);
		float num = float.NaN;
		float num2 = float.NaN;
		MeasureMode measureMode = MeasureMode.Undefined;
		MeasureMode measureMode2 = MeasureMode.Undefined;
		float num3 = nodeMarginForAxis(child, FlexDirection.Row, width);
		float num4 = nodeMarginForAxis(child, FlexDirection.Column, width);
		if (nodeIsStyleDimDefined(child, FlexDirection.Row, width))
		{
			num = resolveValue(child.resolvedDimensions[0], width) + num3;
		}
		else if (nodeIsLeadingPosDefined(child, FlexDirection.Row) && nodeIsTrailingPosDefined(child, FlexDirection.Row))
		{
			num = node.nodeLayout.measuredDimensions[0] - (nodeLeadingBorder(node, FlexDirection.Row) + nodeTrailingBorder(node, FlexDirection.Row)) - (nodeLeadingPosition(child, FlexDirection.Row, width) + nodeTrailingPosition(child, FlexDirection.Row, width));
			num = nodeBoundAxis(child, FlexDirection.Row, num, width, width);
		}
		if (nodeIsStyleDimDefined(child, FlexDirection.Column, height))
		{
			num2 = resolveValue(child.resolvedDimensions[1], height) + num4;
		}
		else if (nodeIsLeadingPosDefined(child, FlexDirection.Column) && nodeIsTrailingPosDefined(child, FlexDirection.Column))
		{
			num2 = node.nodeLayout.measuredDimensions[1] - (nodeLeadingBorder(node, FlexDirection.Column) + nodeTrailingBorder(node, FlexDirection.Column)) - (nodeLeadingPosition(child, FlexDirection.Column, height) + nodeTrailingPosition(child, FlexDirection.Column, height));
			num2 = nodeBoundAxis(child, FlexDirection.Column, num2, height, width);
		}
		if (FloatIsUndefined(num) != FloatIsUndefined(num2) && !FloatIsUndefined(child.nodeStyle.AspectRatio))
		{
			if (FloatIsUndefined(num))
			{
				num = num3 + InnerFunc.fmaxf((num2 - num4) * child.nodeStyle.AspectRatio, nodePaddingAndBorderForAxis(child, FlexDirection.Column, width));
			}
			else if (FloatIsUndefined(num2))
			{
				num2 = num4 + InnerFunc.fmaxf((num - num3) / child.nodeStyle.AspectRatio, nodePaddingAndBorderForAxis(child, FlexDirection.Row, width));
			}
		}
		if (FloatIsUndefined(num) || FloatIsUndefined(num2))
		{
			measureMode = MeasureMode.Exactly;
			if (FloatIsUndefined(num))
			{
				measureMode = MeasureMode.Undefined;
			}
			measureMode2 = MeasureMode.Exactly;
			if (FloatIsUndefined(num2))
			{
				measureMode2 = MeasureMode.Undefined;
			}
			if (!flag && FloatIsUndefined(num) && widthMode != MeasureMode.Undefined && width > 0f)
			{
				num = width;
				measureMode = MeasureMode.AtMost;
			}
			layoutNodeInternal(child, num, num2, direction, measureMode, measureMode2, num, num2, performLayout: false, "abs-measure", config);
			num = child.nodeLayout.measuredDimensions[0] + nodeMarginForAxis(child, FlexDirection.Row, width);
			num2 = child.nodeLayout.measuredDimensions[1] + nodeMarginForAxis(child, FlexDirection.Column, width);
		}
		layoutNodeInternal(child, num, num2, direction, MeasureMode.Exactly, MeasureMode.Exactly, num, num2, performLayout: true, "abs-layout", config);
		if (nodeIsTrailingPosDefined(child, flexDirection) && !nodeIsLeadingPosDefined(child, flexDirection))
		{
			float axisSize = height;
			if (flag)
			{
				axisSize = width;
			}
			child.nodeLayout.Position[(int)leading[(int)flexDirection]] = node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] - nodeTrailingBorder(node, flexDirection) - nodeTrailingMargin(child, flexDirection, width) - nodeTrailingPosition(child, flexDirection, axisSize);
		}
		else if (!nodeIsLeadingPosDefined(child, flexDirection) && node.nodeStyle.JustifyContent == Justify.Center)
		{
			child.nodeLayout.Position[(int)leading[(int)flexDirection]] = (node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]]) / 2f;
		}
		else if (!nodeIsLeadingPosDefined(child, flexDirection) && node.nodeStyle.JustifyContent == Justify.FlexEnd)
		{
			child.nodeLayout.Position[(int)leading[(int)flexDirection]] = node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]];
		}
		if (nodeIsTrailingPosDefined(child, flexDirection2) && !nodeIsLeadingPosDefined(child, flexDirection2))
		{
			float axisSize2 = width;
			if (flag)
			{
				axisSize2 = height;
			}
			child.nodeLayout.Position[(int)leading[(int)flexDirection2]] = node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] - nodeTrailingBorder(node, flexDirection2) - nodeTrailingMargin(child, flexDirection2, width) - nodeTrailingPosition(child, flexDirection2, axisSize2);
		}
		else if (!nodeIsLeadingPosDefined(child, flexDirection2) && nodeAlignItem(node, child) == Align.Center)
		{
			child.nodeLayout.Position[(int)leading[(int)flexDirection2]] = (node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]]) / 2f;
		}
		else if (!nodeIsLeadingPosDefined(child, flexDirection2) && nodeAlignItem(node, child) == Align.FlexEnd != (node.nodeStyle.FlexWrap == Wrap.WrapReverse))
		{
			child.nodeLayout.Position[(int)leading[(int)flexDirection2]] = node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] - child.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]];
		}
	}

	internal static void nodeWithMeasureFuncSetMeasuredDimensions(Node node, float availableWidth, float availableHeight, MeasureMode widthMeasureMode, MeasureMode heightMeasureMode, float parentWidth, float parentHeight)
	{
		assertWithNode(node, node.measureFunc != null, "Expected node to have custom measure function");
		float num = nodePaddingAndBorderForAxis(node, FlexDirection.Row, availableWidth);
		float num2 = nodePaddingAndBorderForAxis(node, FlexDirection.Column, availableWidth);
		float num3 = nodeMarginForAxis(node, FlexDirection.Row, availableWidth);
		float num4 = nodeMarginForAxis(node, FlexDirection.Column, availableWidth);
		float width = InnerFunc.fmaxf(0f, availableWidth - num3 - num);
		if (FloatIsUndefined(availableWidth))
		{
			width = availableWidth;
		}
		float height = InnerFunc.fmaxf(0f, availableHeight - num4 - num2);
		if (FloatIsUndefined(availableHeight))
		{
			height = availableHeight;
		}
		if (widthMeasureMode == MeasureMode.Exactly && heightMeasureMode == MeasureMode.Exactly)
		{
			node.nodeLayout.measuredDimensions[0] = nodeBoundAxis(node, FlexDirection.Row, availableWidth - num3, parentWidth, parentWidth);
			node.nodeLayout.measuredDimensions[1] = nodeBoundAxis(node, FlexDirection.Column, availableHeight - num4, parentHeight, parentWidth);
			return;
		}
		Size size = node.measureFunc(node, width, widthMeasureMode, height, heightMeasureMode);
		float value = availableWidth - num3;
		if (widthMeasureMode == MeasureMode.Undefined || widthMeasureMode == MeasureMode.AtMost)
		{
			value = size.Width + num;
		}
		node.nodeLayout.measuredDimensions[0] = nodeBoundAxis(node, FlexDirection.Row, value, availableWidth, availableWidth);
		float value2 = availableHeight - num4;
		if (heightMeasureMode == MeasureMode.Undefined || heightMeasureMode == MeasureMode.AtMost)
		{
			value2 = size.Height + num2;
		}
		node.nodeLayout.measuredDimensions[1] = nodeBoundAxis(node, FlexDirection.Column, value2, availableHeight, availableWidth);
	}

	internal static void nodeEmptyContainerSetMeasuredDimensions(Node node, float availableWidth, float availableHeight, MeasureMode widthMeasureMode, MeasureMode heightMeasureMode, float parentWidth, float parentHeight)
	{
		float num = nodePaddingAndBorderForAxis(node, FlexDirection.Row, parentWidth);
		float num2 = nodePaddingAndBorderForAxis(node, FlexDirection.Column, parentWidth);
		float num3 = nodeMarginForAxis(node, FlexDirection.Row, parentWidth);
		float num4 = nodeMarginForAxis(node, FlexDirection.Column, parentWidth);
		float value = availableWidth - num3;
		if (widthMeasureMode == MeasureMode.Undefined || widthMeasureMode == MeasureMode.AtMost)
		{
			value = num;
		}
		node.nodeLayout.measuredDimensions[0] = nodeBoundAxis(node, FlexDirection.Row, value, parentWidth, parentWidth);
		float value2 = availableHeight - num4;
		if (heightMeasureMode == MeasureMode.Undefined || heightMeasureMode == MeasureMode.AtMost)
		{
			value2 = num2;
		}
		node.nodeLayout.measuredDimensions[1] = nodeBoundAxis(node, FlexDirection.Column, value2, parentHeight, parentWidth);
	}

	internal static bool nodeFixedSizeSetMeasuredDimensions(Node node, float availableWidth, float availableHeight, MeasureMode widthMeasureMode, MeasureMode heightMeasureMode, float parentWidth, float parentHeight)
	{
		if ((widthMeasureMode == MeasureMode.AtMost && availableWidth <= 0f) || (heightMeasureMode == MeasureMode.AtMost && availableHeight <= 0f) || (widthMeasureMode == MeasureMode.Exactly && heightMeasureMode == MeasureMode.Exactly))
		{
			float num = nodeMarginForAxis(node, FlexDirection.Column, parentWidth);
			float num2 = nodeMarginForAxis(node, FlexDirection.Row, parentWidth);
			float value = availableWidth - num2;
			if (FloatIsUndefined(availableWidth) || (widthMeasureMode == MeasureMode.AtMost && availableWidth < 0f))
			{
				value = 0f;
			}
			node.nodeLayout.measuredDimensions[0] = nodeBoundAxis(node, FlexDirection.Row, value, parentWidth, parentWidth);
			float value2 = availableHeight - num;
			if (FloatIsUndefined(availableHeight) || (heightMeasureMode == MeasureMode.AtMost && availableHeight < 0f))
			{
				value2 = 0f;
			}
			node.nodeLayout.measuredDimensions[1] = nodeBoundAxis(node, FlexDirection.Column, value2, parentHeight, parentWidth);
			return true;
		}
		return false;
	}

	internal static void zeroOutLayoutRecursivly(Node node)
	{
		node.nodeLayout.Dimensions[1] = 0f;
		node.nodeLayout.Dimensions[0] = 0f;
		node.nodeLayout.Position[1] = 0f;
		node.nodeLayout.Position[3] = 0f;
		node.nodeLayout.Position[0] = 0f;
		node.nodeLayout.Position[2] = 0f;
		node.nodeLayout.cachedLayout.availableHeight = 0f;
		node.nodeLayout.cachedLayout.availableWidth = 0f;
		node.nodeLayout.cachedLayout.heightMeasureMode = MeasureMode.Exactly;
		node.nodeLayout.cachedLayout.widthMeasureMode = MeasureMode.Exactly;
		node.nodeLayout.cachedLayout.computedWidth = 0f;
		node.nodeLayout.cachedLayout.computedHeight = 0f;
		node.hasNewLayout = true;
		foreach (Node child in node.Children)
		{
			zeroOutLayoutRecursivly(child);
		}
	}

	internal static void nodelayoutImpl(Node node, float availableWidth, float availableHeight, Direction parentDirection, MeasureMode widthMeasureMode, MeasureMode heightMeasureMode, float parentWidth, float parentHeight, bool performLayout, Config config)
	{
		Direction direction = nodeResolveDirection(node, parentDirection);
		node.nodeLayout.Direction = direction;
		FlexDirection axis = resolveFlexDirection(FlexDirection.Row, direction);
		FlexDirection axis2 = resolveFlexDirection(FlexDirection.Column, direction);
		node.nodeLayout.Margin[4] = nodeLeadingMargin(node, axis, parentWidth);
		node.nodeLayout.Margin[5] = nodeTrailingMargin(node, axis, parentWidth);
		node.nodeLayout.Margin[1] = nodeLeadingMargin(node, axis2, parentWidth);
		node.nodeLayout.Margin[3] = nodeTrailingMargin(node, axis2, parentWidth);
		node.nodeLayout.Border[4] = nodeLeadingBorder(node, axis);
		node.nodeLayout.Border[5] = nodeTrailingBorder(node, axis);
		node.nodeLayout.Border[1] = nodeLeadingBorder(node, axis2);
		node.nodeLayout.Border[3] = nodeTrailingBorder(node, axis2);
		node.nodeLayout.Padding[4] = nodeLeadingPadding(node, axis, parentWidth);
		node.nodeLayout.Padding[5] = nodeTrailingPadding(node, axis, parentWidth);
		node.nodeLayout.Padding[1] = nodeLeadingPadding(node, axis2, parentWidth);
		node.nodeLayout.Padding[3] = nodeTrailingPadding(node, axis2, parentWidth);
		if (node.measureFunc != null)
		{
			nodeWithMeasureFuncSetMeasuredDimensions(node, availableWidth, availableHeight, widthMeasureMode, heightMeasureMode, parentWidth, parentHeight);
			return;
		}
		int count = node.Children.Count;
		if (count == 0)
		{
			nodeEmptyContainerSetMeasuredDimensions(node, availableWidth, availableHeight, widthMeasureMode, heightMeasureMode, parentWidth, parentHeight);
		}
		else
		{
			if (!performLayout && nodeFixedSizeSetMeasuredDimensions(node, availableWidth, availableHeight, widthMeasureMode, heightMeasureMode, parentWidth, parentHeight))
			{
				return;
			}
			node.nodeLayout.HadOverflow = false;
			FlexDirection flexDirection = resolveFlexDirection(node.nodeStyle.FlexDirection, direction);
			FlexDirection flexDirection2 = flexDirectionCross(flexDirection, direction);
			bool flag = flexDirectionIsRow(flexDirection);
			Justify justifyContent = node.nodeStyle.JustifyContent;
			bool flag2 = node.nodeStyle.FlexWrap != Wrap.NoWrap;
			float num = parentHeight;
			float axisSize = parentWidth;
			if (flag)
			{
				num = parentWidth;
				axisSize = parentHeight;
			}
			Node node2 = null;
			Node node3 = null;
			float num2 = nodeLeadingPaddingAndBorder(node, flexDirection, parentWidth);
			float num3 = nodeTrailingPaddingAndBorder(node, flexDirection, parentWidth);
			float num4 = nodeLeadingPaddingAndBorder(node, flexDirection2, parentWidth);
			float num5 = nodePaddingAndBorderForAxis(node, flexDirection, parentWidth);
			float num6 = nodePaddingAndBorderForAxis(node, flexDirection2, parentWidth);
			MeasureMode measureMode = heightMeasureMode;
			MeasureMode measureMode2 = widthMeasureMode;
			if (flag)
			{
				measureMode = widthMeasureMode;
				measureMode2 = heightMeasureMode;
			}
			float num7 = num6;
			float num8 = num5;
			if (flag)
			{
				num7 = num5;
				num8 = num6;
			}
			float num9 = nodeMarginForAxis(node, FlexDirection.Row, parentWidth);
			float num10 = nodeMarginForAxis(node, FlexDirection.Column, parentWidth);
			float num11 = resolveValue(node.nodeStyle.MinDimensions[0], parentWidth) - num9 - num7;
			float num12 = resolveValue(node.nodeStyle.MaxDimensions[0], parentWidth) - num9 - num7;
			float num13 = resolveValue(node.nodeStyle.MinDimensions[1], parentHeight) - num10 - num8;
			float num14 = resolveValue(node.nodeStyle.MaxDimensions[1], parentHeight) - num10 - num8;
			float num15 = num13;
			float num16 = num14;
			if (flag)
			{
				num15 = num11;
				num16 = num12;
			}
			float num17 = availableWidth - num9 - num7;
			if (!FloatIsUndefined(num17))
			{
				num17 = InnerFunc.fmaxf(InnerFunc.fminf(num17, num12), num11);
			}
			float num18 = availableHeight - num10 - num8;
			if (!FloatIsUndefined(num18))
			{
				num18 = InnerFunc.fmaxf(InnerFunc.fminf(num18, num14), num13);
			}
			float num19 = num18;
			float num20 = num17;
			if (flag)
			{
				num19 = num17;
				num20 = num18;
			}
			Node node4 = null;
			if (measureMode == MeasureMode.Exactly)
			{
				foreach (Node child in node.Children)
				{
					if (node4 != null)
					{
						if (nodeIsFlex(child))
						{
							node4 = null;
							break;
						}
					}
					else if (resolveFlexGrow(child) > 0f && nodeResolveFlexShrink(child) > 0f)
					{
						node4 = child;
					}
				}
			}
			float num21 = 0f;
			foreach (Node child2 in node.Children)
			{
				if (child2.nodeStyle.Display == Display.None)
				{
					zeroOutLayoutRecursivly(child2);
					child2.hasNewLayout = true;
					child2.IsDirty = false;
					continue;
				}
				resolveDimensions(child2);
				if (performLayout)
				{
					Direction direction2 = nodeResolveDirection(child2, direction);
					nodeSetPosition(child2, direction2, num19, num20, num17);
				}
				if (child2.nodeStyle.PositionType == PositionType.Absolute)
				{
					if (node2 == null)
					{
						node2 = child2;
					}
					if (node3 != null)
					{
						node3.NextChild = child2;
					}
					node3 = child2;
					child2.NextChild = null;
				}
				else if (child2 == node4)
				{
					child2.nodeLayout.computedFlexBasisGeneration = currentGenerationCount;
					child2.nodeLayout.computedFlexBasis = 0f;
				}
				else
				{
					nodeComputeFlexBasisForChild(node, child2, num17, widthMeasureMode, num18, num17, num18, heightMeasureMode, direction, config);
				}
				num21 += child2.nodeLayout.computedFlexBasis + nodeMarginForAxis(child2, flexDirection, num17);
			}
			bool flag3 = num21 > num19;
			if (measureMode == MeasureMode.Undefined)
			{
				flag3 = false;
			}
			if (flag2 && flag3 && measureMode == MeasureMode.AtMost)
			{
				measureMode = MeasureMode.Exactly;
			}
			int num22 = 0;
			int num23 = 0;
			int num24 = 0;
			float num25 = 0f;
			float num26 = 0f;
			while (num23 < count)
			{
				int num27 = 0;
				float num28 = 0f;
				float num29 = 0f;
				float num30 = 0f;
				float num31 = 0f;
				Node node5 = null;
				Node node6 = null;
				for (int i = num22; i < count; i++)
				{
					Node node7 = node.Children[i];
					if (node7.nodeStyle.Display == Display.None)
					{
						num23++;
						continue;
					}
					node7.lineIndex = num24;
					if (node7.nodeStyle.PositionType != PositionType.Absolute)
					{
						float num32 = nodeMarginForAxis(node7, flexDirection, num17);
						float b = InnerFunc.fminf(resolveValue(node7.nodeStyle.MaxDimensions[(int)dim[(int)flexDirection]], num), node7.nodeLayout.computedFlexBasis);
						float num33 = InnerFunc.fmaxf(resolveValue(node7.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]], num), b);
						if (num29 + num33 + num32 > num19 && flag2 && num27 > 0)
						{
							break;
						}
						num29 += num33 + num32;
						num28 += num33 + num32;
						num27++;
						if (nodeIsFlex(node7))
						{
							num30 += resolveFlexGrow(node7);
							num31 += (0f - nodeResolveFlexShrink(node7)) * node7.nodeLayout.computedFlexBasis;
						}
						if (node5 == null)
						{
							node5 = node7;
						}
						if (node6 != null)
						{
							node6.NextChild = node7;
						}
						node6 = node7;
						node7.NextChild = null;
					}
					num23++;
				}
				if (num30 > 0f && num30 < 1f)
				{
					num30 = 1f;
				}
				if (num31 > 0f && num31 < 1f)
				{
					num31 = 1f;
				}
				bool flag4 = !performLayout && measureMode2 == MeasureMode.Exactly;
				float num34 = 0f;
				float num35 = 0f;
				if (measureMode != MeasureMode.Exactly)
				{
					if (!FloatIsUndefined(num15) && num28 < num15)
					{
						num19 = num15;
					}
					else if (!FloatIsUndefined(num16) && num28 > num16)
					{
						num19 = num16;
					}
					else if (!node.config.UseLegacyStretchBehaviour && (num30 == 0f || resolveFlexGrow(node) == 0f))
					{
						num19 = num28;
					}
				}
				float num36 = 0f;
				if (!FloatIsUndefined(num19))
				{
					num36 = num19 - num28;
				}
				else if (num28 < 0f)
				{
					num36 = 0f - num28;
				}
				float num37 = num36;
				float num38 = 0f;
				if (!flag4)
				{
					float num39 = 0f;
					float num40 = 0f;
					for (node6 = node5; node6 != null; node6 = node6.NextChild)
					{
						float num41 = InnerFunc.fminf(resolveValue(node6.nodeStyle.MaxDimensions[(int)dim[(int)flexDirection]], num), InnerFunc.fmaxf(resolveValue(node6.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]], num), node6.nodeLayout.computedFlexBasis));
						if (num36 < 0f)
						{
							float num42 = (0f - nodeResolveFlexShrink(node6)) * num41;
							if (num42 != 0f)
							{
								float num43 = num41 + num36 / num31 * num42;
								float num44 = nodeBoundAxis(node6, flexDirection, num43, num19, num17);
								if (num43 != num44)
								{
									num38 -= num44 - num41;
									num39 -= num42;
								}
							}
						}
						else if (num36 > 0f)
						{
							float num45 = resolveFlexGrow(node6);
							if (num45 != 0f)
							{
								float num43 = num41 + num36 / num30 * num45;
								float num44 = nodeBoundAxis(node6, flexDirection, num43, num19, num17);
								if (num43 != num44)
								{
									num38 -= num44 - num41;
									num40 -= num45;
								}
							}
						}
					}
					num31 += num39;
					num30 += num40;
					num36 += num38;
					num38 = 0f;
					for (node6 = node5; node6 != null; node6 = node6.NextChild)
					{
						float num41 = InnerFunc.fminf(resolveValue(node6.nodeStyle.MaxDimensions[(int)dim[(int)flexDirection]], num), InnerFunc.fmaxf(resolveValue(node6.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]], num), node6.nodeLayout.computedFlexBasis));
						float num46 = num41;
						if (num36 < 0f)
						{
							float num42 = (0f - nodeResolveFlexShrink(node6)) * num41;
							if (num42 != 0f)
							{
								float num47 = 0f;
								num47 = ((num31 != 0f) ? (num41 + num36 / num31 * num42) : (num41 + num42));
								num46 = nodeBoundAxis(node6, flexDirection, num47, num19, num17);
							}
						}
						else if (num36 > 0f)
						{
							float num45 = resolveFlexGrow(node6);
							if (num45 != 0f)
							{
								num46 = nodeBoundAxis(node6, flexDirection, num41 + num36 / num30 * num45, num19, num17);
							}
						}
						num38 -= num46 - num41;
						float num48 = nodeMarginForAxis(node6, flexDirection, num17);
						float num49 = nodeMarginForAxis(node6, flexDirection2, num17);
						float num50 = 0f;
						float size = num46 + num48;
						MeasureMode measureMode3 = MeasureMode.Undefined;
						MeasureMode mode = MeasureMode.Exactly;
						if (!FloatIsUndefined(num20) && !nodeIsStyleDimDefined(node6, flexDirection2, num20) && measureMode2 == MeasureMode.Exactly && !(flag2 && flag3) && nodeAlignItem(node, node6) == Align.Stretch)
						{
							num50 = num20;
							measureMode3 = MeasureMode.Exactly;
						}
						else if (!nodeIsStyleDimDefined(node6, flexDirection2, num20))
						{
							num50 = num20;
							measureMode3 = MeasureMode.AtMost;
							if (FloatIsUndefined(num50))
							{
								measureMode3 = MeasureMode.Undefined;
							}
						}
						else
						{
							num50 = resolveValue(node6.resolvedDimensions[(int)dim[(int)flexDirection2]], num20) + num49;
							bool flag5 = node6.resolvedDimensions[(int)dim[(int)flexDirection2]].unit == Unit.Percent && measureMode2 != MeasureMode.Exactly;
							measureMode3 = MeasureMode.Exactly;
							if (FloatIsUndefined(num50) || flag5)
							{
								measureMode3 = MeasureMode.Undefined;
							}
						}
						if (!FloatIsUndefined(node6.nodeStyle.AspectRatio))
						{
							float a = (size - num48) * node6.nodeStyle.AspectRatio;
							if (flag)
							{
								a = (size - num48) / node6.nodeStyle.AspectRatio;
							}
							num50 = InnerFunc.fmaxf(a, nodePaddingAndBorderForAxis(node6, flexDirection2, num17));
							measureMode3 = MeasureMode.Exactly;
							if (nodeIsFlex(node6))
							{
								num50 = InnerFunc.fminf(num50 - num49, num20);
								size = num48;
								size = ((!flag) ? (size + num50 / node6.nodeStyle.AspectRatio) : (size + num50 * node6.nodeStyle.AspectRatio));
							}
							num50 += num49;
						}
						constrainMaxSizeForMode(node6, flexDirection, num19, num17, ref mode, ref size);
						constrainMaxSizeForMode(node6, flexDirection2, num20, num17, ref measureMode3, ref num50);
						bool flag6 = !nodeIsStyleDimDefined(node6, flexDirection2, num20) && nodeAlignItem(node, node6) == Align.Stretch;
						float availableWidth2 = num50;
						if (flag)
						{
							availableWidth2 = size;
						}
						float availableHeight2 = num50;
						if (!flag)
						{
							availableHeight2 = size;
						}
						MeasureMode widthMeasureMode2 = measureMode3;
						if (flag)
						{
							widthMeasureMode2 = mode;
						}
						MeasureMode heightMeasureMode2 = measureMode3;
						if (!flag)
						{
							heightMeasureMode2 = mode;
						}
						layoutNodeInternal(node6, availableWidth2, availableHeight2, direction, widthMeasureMode2, heightMeasureMode2, num17, num18, performLayout && !flag6, "flex", config);
						if (node6.nodeLayout.HadOverflow)
						{
							node.nodeLayout.HadOverflow = true;
						}
					}
				}
				num36 = num37 + num38;
				if (num36 < 0f)
				{
					node.nodeLayout.HadOverflow = true;
				}
				if (measureMode == MeasureMode.AtMost && num36 > 0f)
				{
					num36 = ((node.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]].unit == Unit.Undefined || !(resolveValue(node.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]], num) >= 0f)) ? 0f : InnerFunc.fmaxf(0f, resolveValue(node.nodeStyle.MinDimensions[(int)dim[(int)flexDirection]], num) - (num19 - num36)));
				}
				int num51 = 0;
				for (int j = num22; j < num23; j++)
				{
					Node node8 = node.Children[j];
					if (node8.nodeStyle.PositionType == PositionType.Relative)
					{
						if (marginLeadingValue(node8, flexDirection).unit == Unit.Auto)
						{
							num51++;
						}
						if (marginTrailingValue(node8, flexDirection).unit == Unit.Auto)
						{
							num51++;
						}
					}
				}
				if (num51 == 0)
				{
					switch (justifyContent)
					{
					case Justify.Center:
						num34 = num36 / 2f;
						break;
					case Justify.FlexEnd:
						num34 = num36;
						break;
					case Justify.SpaceBetween:
						num35 = ((num27 <= 1) ? 0f : (InnerFunc.fmaxf(num36, 0f) / (float)(num27 - 1)));
						break;
					case Justify.SpaceAround:
						num35 = num36 / (float)num27;
						num34 = num35 / 2f;
						break;
					}
				}
				float num52 = num2 + num34;
				float num53 = 0f;
				for (int k = num22; k < num23; k++)
				{
					Node node9 = node.Children[k];
					if (node9.nodeStyle.Display == Display.None)
					{
						continue;
					}
					if (node9.nodeStyle.PositionType == PositionType.Absolute && nodeIsLeadingPosDefined(node9, flexDirection))
					{
						if (performLayout)
						{
							node9.nodeLayout.Position[(int)pos[(int)flexDirection]] = nodeLeadingPosition(node9, flexDirection, num19) + nodeLeadingBorder(node, flexDirection) + nodeLeadingMargin(node9, flexDirection, num17);
						}
					}
					else if (node9.nodeStyle.PositionType == PositionType.Relative)
					{
						if (marginLeadingValue(node9, flexDirection).unit == Unit.Auto)
						{
							num52 += num36 / (float)num51;
						}
						if (performLayout)
						{
							node9.nodeLayout.Position[(int)pos[(int)flexDirection]] += num52;
						}
						if (marginTrailingValue(node9, flexDirection).unit == Unit.Auto)
						{
							num52 += num36 / (float)num51;
						}
						if (flag4)
						{
							num52 += num35 + nodeMarginForAxis(node9, flexDirection, num17) + node9.nodeLayout.computedFlexBasis;
							num53 = num20;
						}
						else
						{
							num52 += num35 + nodeDimWithMargin(node9, flexDirection, num17);
							num53 = InnerFunc.fmaxf(num53, nodeDimWithMargin(node9, flexDirection2, num17));
						}
					}
					else if (performLayout)
					{
						node9.nodeLayout.Position[(int)pos[(int)flexDirection]] += nodeLeadingBorder(node, flexDirection) + num34;
					}
				}
				num52 += num3;
				float num54 = num20;
				if (measureMode2 == MeasureMode.Undefined || measureMode2 == MeasureMode.AtMost)
				{
					num54 = nodeBoundAxis(node, flexDirection2, num53 + num6, axisSize, parentWidth) - num6;
				}
				if (!flag2 && measureMode2 == MeasureMode.Exactly)
				{
					num53 = num20;
				}
				num53 = nodeBoundAxis(node, flexDirection2, num53 + num6, axisSize, parentWidth) - num6;
				if (performLayout)
				{
					for (int l = num22; l < num23; l++)
					{
						Node node10 = node.Children[l];
						if (node10.nodeStyle.Display == Display.None)
						{
							continue;
						}
						if (node10.nodeStyle.PositionType == PositionType.Absolute)
						{
							if (nodeIsLeadingPosDefined(node10, flexDirection2))
							{
								node10.nodeLayout.Position[(int)pos[(int)flexDirection2]] = nodeLeadingPosition(node10, flexDirection2, num20) + nodeLeadingBorder(node, flexDirection2) + nodeLeadingMargin(node10, flexDirection2, num17);
							}
							else
							{
								node10.nodeLayout.Position[(int)pos[(int)flexDirection2]] = nodeLeadingBorder(node, flexDirection2) + nodeLeadingMargin(node10, flexDirection2, num17);
							}
							continue;
						}
						float num55 = num4;
						Align align = nodeAlignItem(node, node10);
						if (align == Align.Stretch && marginLeadingValue(node10, flexDirection2).unit != Unit.Auto && marginTrailingValue(node10, flexDirection2).unit != Unit.Auto)
						{
							if (!nodeIsStyleDimDefined(node10, flexDirection2, num20))
							{
								float num56 = node10.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]];
								float size2 = num53;
								if (!FloatIsUndefined(node10.nodeStyle.AspectRatio))
								{
									size2 = nodeMarginForAxis(node10, flexDirection2, num17);
									size2 = ((!flag) ? (size2 + num56 * node10.nodeStyle.AspectRatio) : (size2 + num56 / node10.nodeStyle.AspectRatio));
								}
								num56 += nodeMarginForAxis(node10, flexDirection, num17);
								MeasureMode mode2 = MeasureMode.Exactly;
								MeasureMode mode3 = MeasureMode.Exactly;
								constrainMaxSizeForMode(node10, flexDirection, num19, num17, ref mode2, ref num56);
								constrainMaxSizeForMode(node10, flexDirection2, num20, num17, ref mode3, ref size2);
								float num57 = size2;
								if (flag)
								{
									num57 = num56;
								}
								float num58 = size2;
								if (!flag)
								{
									num58 = num56;
								}
								MeasureMode widthMeasureMode3 = MeasureMode.Exactly;
								if (FloatIsUndefined(num57))
								{
									widthMeasureMode3 = MeasureMode.Undefined;
								}
								MeasureMode heightMeasureMode3 = MeasureMode.Exactly;
								if (FloatIsUndefined(num58))
								{
									heightMeasureMode3 = MeasureMode.Undefined;
								}
								layoutNodeInternal(node10, num57, num58, direction, widthMeasureMode3, heightMeasureMode3, num17, num18, performLayout: true, "stretch", config);
							}
						}
						else
						{
							float num59 = num54 - nodeDimWithMargin(node10, flexDirection2, num17);
							if (marginLeadingValue(node10, flexDirection2).unit == Unit.Auto && marginTrailingValue(node10, flexDirection2).unit == Unit.Auto)
							{
								num55 += InnerFunc.fmaxf(0f, num59 / 2f);
							}
							else if (marginTrailingValue(node10, flexDirection2).unit != Unit.Auto)
							{
								if (marginLeadingValue(node10, flexDirection2).unit == Unit.Auto)
								{
									num55 += InnerFunc.fmaxf(0f, num59);
								}
								else
								{
									switch (align)
									{
									case Align.Center:
										num55 += num59 / 2f;
										break;
									default:
										num55 += num59;
										break;
									case Align.FlexStart:
										break;
									}
								}
							}
						}
						node10.nodeLayout.Position[(int)pos[(int)flexDirection2]] += num25 + num55;
					}
				}
				num25 += num53;
				num26 = InnerFunc.fmaxf(num26, num52);
				num24++;
				num22 = num23;
			}
			if (performLayout && (num24 > 1 || isBaselineLayout(node)) && !FloatIsUndefined(num20))
			{
				float num60 = num20 - num25;
				float num61 = 0f;
				float num62 = num4;
				switch (node.nodeStyle.AlignContent)
				{
				case Align.FlexEnd:
					num62 += num60;
					break;
				case Align.Center:
					num62 += num60 / 2f;
					break;
				case Align.Stretch:
					if (num20 > num25)
					{
						num61 = num60 / (float)num24;
					}
					break;
				case Align.SpaceAround:
					if (num20 > num25)
					{
						num62 += num60 / (float)(2 * num24);
						if (num24 > 1)
						{
							num61 = num60 / (float)num24;
						}
					}
					else
					{
						num62 += num60 / 2f;
					}
					break;
				case Align.SpaceBetween:
					if (num20 > num25 && num24 > 1)
					{
						num61 = num60 / (float)(num24 - 1);
					}
					break;
				}
				int num63 = 0;
				for (int m = 0; m < num24; m++)
				{
					int num64 = num63;
					int num65 = 0;
					float num66 = 0f;
					float num67 = 0f;
					float num68 = 0f;
					for (num65 = num64; num65 < count; num65++)
					{
						Node node11 = node.Children[num65];
						if (node11.nodeStyle.Display != Display.None && node11.nodeStyle.PositionType == PositionType.Relative)
						{
							if (node11.lineIndex != m)
							{
								break;
							}
							if (nodeIsLayoutDimDefined(node11, flexDirection2))
							{
								num66 = InnerFunc.fmaxf(num66, node11.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] + nodeMarginForAxis(node11, flexDirection2, num17));
							}
							if (nodeAlignItem(node, node11) == Align.Baseline)
							{
								float num69 = Baseline(node11) + nodeLeadingMargin(node11, FlexDirection.Column, num17);
								float b2 = node11.nodeLayout.measuredDimensions[1] + nodeMarginForAxis(node11, FlexDirection.Column, num17) - num69;
								num67 = InnerFunc.fmaxf(num67, num69);
								num68 = InnerFunc.fmaxf(num68, b2);
								num66 = InnerFunc.fmaxf(num66, num67 + num68);
							}
						}
					}
					num63 = num65;
					num66 += num61;
					if (performLayout)
					{
						for (num65 = num64; num65 < num63; num65++)
						{
							Node node12 = node.Children[num65];
							if (node12.nodeStyle.Display == Display.None || node12.nodeStyle.PositionType != PositionType.Relative)
							{
								continue;
							}
							switch (nodeAlignItem(node, node12))
							{
							case Align.FlexStart:
								node12.nodeLayout.Position[(int)pos[(int)flexDirection2]] = num62 + nodeLeadingMargin(node12, flexDirection2, num17);
								break;
							case Align.FlexEnd:
								node12.nodeLayout.Position[(int)pos[(int)flexDirection2]] = num62 + num66 - nodeTrailingMargin(node12, flexDirection2, num17) - node12.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]];
								break;
							case Align.Center:
							{
								float num72 = node12.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]];
								node12.nodeLayout.Position[(int)pos[(int)flexDirection2]] = num62 + (num66 - num72) / 2f;
								break;
							}
							case Align.Stretch:
								node12.nodeLayout.Position[(int)pos[(int)flexDirection2]] = num62 + nodeLeadingMargin(node12, flexDirection2, num17);
								if (!nodeIsStyleDimDefined(node12, flexDirection2, num20))
								{
									float num70 = num66;
									if (flag)
									{
										num70 = node12.nodeLayout.measuredDimensions[0] + nodeMarginForAxis(node12, flexDirection, num17);
									}
									float num71 = num66;
									if (!flag)
									{
										num71 = node12.nodeLayout.measuredDimensions[1] + nodeMarginForAxis(node12, flexDirection2, num17);
									}
									if (!FloatsEqual(num70, node12.nodeLayout.measuredDimensions[0]) || !FloatsEqual(num71, node12.nodeLayout.measuredDimensions[1]))
									{
										layoutNodeInternal(node12, num70, num71, direction, MeasureMode.Exactly, MeasureMode.Exactly, num17, num18, performLayout: true, "multiline-stretch", config);
									}
								}
								break;
							case Align.Baseline:
								node12.nodeLayout.Position[1] = num62 + num67 - Baseline(node12) + nodeLeadingPosition(node12, FlexDirection.Column, num20);
								break;
							}
						}
					}
					num62 += num66;
				}
			}
			node.nodeLayout.measuredDimensions[0] = nodeBoundAxis(node, FlexDirection.Row, availableWidth - num9, parentWidth, parentWidth);
			node.nodeLayout.measuredDimensions[1] = nodeBoundAxis(node, FlexDirection.Column, availableHeight - num10, parentHeight, parentWidth);
			if (measureMode == MeasureMode.Undefined || (node.nodeStyle.Overflow != Overflow.Scroll && measureMode == MeasureMode.AtMost))
			{
				node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] = nodeBoundAxis(node, flexDirection, num26, num, parentWidth);
			}
			else if (measureMode == MeasureMode.AtMost && node.nodeStyle.Overflow == Overflow.Scroll)
			{
				node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection]] = InnerFunc.fmaxf(InnerFunc.fminf(num19 + num5, nodeBoundAxisWithinMinAndMax(node, flexDirection, num26, num)), num5);
			}
			if (measureMode2 == MeasureMode.Undefined || (node.nodeStyle.Overflow != Overflow.Scroll && measureMode2 == MeasureMode.AtMost))
			{
				node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] = nodeBoundAxis(node, flexDirection2, num25 + num6, axisSize, parentWidth);
			}
			else if (measureMode2 == MeasureMode.AtMost && node.nodeStyle.Overflow == Overflow.Scroll)
			{
				node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] = InnerFunc.fmaxf(InnerFunc.fminf(num20 + num6, nodeBoundAxisWithinMinAndMax(node, flexDirection2, num25 + num6, axisSize)), num6);
			}
			if (performLayout && node.nodeStyle.FlexWrap == Wrap.WrapReverse)
			{
				foreach (Node child3 in node.Children)
				{
					if (child3.nodeStyle.PositionType == PositionType.Relative)
					{
						child3.nodeLayout.Position[(int)pos[(int)flexDirection2]] = node.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]] - child3.nodeLayout.Position[(int)pos[(int)flexDirection2]] - child3.nodeLayout.measuredDimensions[(int)dim[(int)flexDirection2]];
					}
				}
			}
			if (!performLayout)
			{
				return;
			}
			for (node3 = node2; node3 != null; node3 = node3.NextChild)
			{
				MeasureMode widthMode = measureMode2;
				if (flag)
				{
					widthMode = measureMode;
				}
				nodeAbsoluteLayoutChild(node, node3, num17, widthMode, num18, direction, config);
			}
			bool flag7 = flexDirection == FlexDirection.RowReverse || flexDirection == FlexDirection.ColumnReverse;
			bool flag8 = flexDirection2 == FlexDirection.RowReverse || flexDirection2 == FlexDirection.ColumnReverse;
			if (!(flag7 || flag8))
			{
				return;
			}
			foreach (Node child4 in node.Children)
			{
				if (child4.nodeStyle.Display != Display.None)
				{
					if (flag7)
					{
						nodeSetChildTrailingPosition(node, child4, flexDirection);
					}
					if (flag8)
					{
						nodeSetChildTrailingPosition(node, child4, flexDirection2);
					}
				}
			}
		}
	}

	internal static string spacer(int level)
	{
		if (level > "".Length)
		{
			level = "".Length;
		}
		return "".Substring(0, level);
	}

	internal static string measureModeName(MeasureMode mode, bool performLayout)
	{
		if (mode >= (MeasureMode)3)
		{
			return "";
		}
		if (performLayout)
		{
			return Constant.layoutModeNames[(int)mode];
		}
		return Constant.measureModeNames[(int)mode];
	}

	internal static bool measureModeSizeIsExactAndMatchesOldMeasuredSize(MeasureMode sizeMode, float size, float lastComputedSize)
	{
		return sizeMode == MeasureMode.Exactly && FloatsEqual(size, lastComputedSize);
	}

	internal static bool measureModeOldSizeIsUnspecifiedAndStillFits(MeasureMode sizeMode, float size, MeasureMode lastSizeMode, float lastComputedSize)
	{
		return sizeMode == MeasureMode.AtMost && lastSizeMode == MeasureMode.Undefined && (size >= lastComputedSize || FloatsEqual(size, lastComputedSize));
	}

	internal static bool measureModeNewMeasureSizeIsStricterAndStillValid(MeasureMode sizeMode, float size, MeasureMode lastSizeMode, float lastSize, float lastComputedSize)
	{
		return lastSizeMode == MeasureMode.AtMost && sizeMode == MeasureMode.AtMost && lastSize > size && (lastComputedSize <= size || FloatsEqual(size, lastComputedSize));
	}

	internal static bool nodeCanUseCachedMeasurement(MeasureMode widthMode, float width, MeasureMode heightMode, float height, MeasureMode lastWidthMode, float lastWidth, MeasureMode lastHeightMode, float lastHeight, float lastComputedWidth, float lastComputedHeight, float marginRow, float marginColumn, Config config)
	{
		if (lastComputedHeight < 0f || lastComputedWidth < 0f)
		{
			return false;
		}
		bool flag = config != null && config.PointScaleFactor != 0f;
		float b = width;
		float b2 = height;
		float a = lastWidth;
		float a2 = lastHeight;
		if (flag)
		{
			b = RoundValueToPixelGrid(width, config.PointScaleFactor, forceCeil: false, forceFloor: false);
			b2 = RoundValueToPixelGrid(height, config.PointScaleFactor, forceCeil: false, forceFloor: false);
			a = RoundValueToPixelGrid(lastWidth, config.PointScaleFactor, forceCeil: false, forceFloor: false);
			a2 = RoundValueToPixelGrid(lastHeight, config.PointScaleFactor, forceCeil: false, forceFloor: false);
		}
		bool flag2 = lastWidthMode == widthMode && FloatsEqual(a, b);
		bool flag3 = lastHeightMode == heightMode && FloatsEqual(a2, b2);
		bool flag4 = flag2 || measureModeSizeIsExactAndMatchesOldMeasuredSize(widthMode, width - marginRow, lastComputedWidth) || measureModeOldSizeIsUnspecifiedAndStillFits(widthMode, width - marginRow, lastWidthMode, lastComputedWidth) || measureModeNewMeasureSizeIsStricterAndStillValid(widthMode, width - marginRow, lastWidthMode, lastWidth, lastComputedWidth);
		bool flag5 = flag3 || measureModeSizeIsExactAndMatchesOldMeasuredSize(heightMode, height - marginColumn, lastComputedHeight) || measureModeOldSizeIsUnspecifiedAndStillFits(heightMode, height - marginColumn, lastHeightMode, lastComputedHeight) || measureModeNewMeasureSizeIsStricterAndStillValid(heightMode, height - marginColumn, lastHeightMode, lastHeight, lastComputedHeight);
		return flag4 && flag5;
	}

	internal static bool layoutNodeInternal(Node node, float availableWidth, float availableHeight, Direction parentDirection, MeasureMode widthMeasureMode, MeasureMode heightMeasureMode, float parentWidth, float parentHeight, bool performLayout, string reason, Config config)
	{
		Layout nodeLayout = node.nodeLayout;
		gDepth++;
		bool flag = (node.IsDirty && nodeLayout.generationCount != currentGenerationCount) || nodeLayout.lastParentDirection != parentDirection;
		if (flag)
		{
			nodeLayout.nextCachedMeasurementsIndex = 0;
			nodeLayout.cachedLayout.widthMeasureMode = MeasureMode.NeverUsed_1;
			nodeLayout.cachedLayout.heightMeasureMode = MeasureMode.NeverUsed_1;
			nodeLayout.cachedLayout.computedWidth = -1f;
			nodeLayout.cachedLayout.computedHeight = -1f;
		}
		CachedMeasurement cachedMeasurement = null;
		if (node.measureFunc != null)
		{
			float marginRow = nodeMarginForAxis(node, FlexDirection.Row, parentWidth);
			float marginColumn = nodeMarginForAxis(node, FlexDirection.Column, parentWidth);
			if (nodeCanUseCachedMeasurement(widthMeasureMode, availableWidth, heightMeasureMode, availableHeight, nodeLayout.cachedLayout.widthMeasureMode, nodeLayout.cachedLayout.availableWidth, nodeLayout.cachedLayout.heightMeasureMode, nodeLayout.cachedLayout.availableHeight, nodeLayout.cachedLayout.computedWidth, nodeLayout.cachedLayout.computedHeight, marginRow, marginColumn, config))
			{
				cachedMeasurement = nodeLayout.cachedLayout;
			}
			else
			{
				for (int i = 0; i < nodeLayout.nextCachedMeasurementsIndex; i++)
				{
					if (nodeCanUseCachedMeasurement(widthMeasureMode, availableWidth, heightMeasureMode, availableHeight, nodeLayout.cachedMeasurements[i].widthMeasureMode, nodeLayout.cachedMeasurements[i].availableWidth, nodeLayout.cachedMeasurements[i].heightMeasureMode, nodeLayout.cachedMeasurements[i].availableHeight, nodeLayout.cachedMeasurements[i].computedWidth, nodeLayout.cachedMeasurements[i].computedHeight, marginRow, marginColumn, config))
					{
						cachedMeasurement = nodeLayout.cachedMeasurements[i];
						break;
					}
				}
			}
		}
		else if (performLayout)
		{
			if (FloatsEqual(nodeLayout.cachedLayout.availableWidth, availableWidth) && FloatsEqual(nodeLayout.cachedLayout.availableHeight, availableHeight) && nodeLayout.cachedLayout.widthMeasureMode == widthMeasureMode && nodeLayout.cachedLayout.heightMeasureMode == heightMeasureMode)
			{
				cachedMeasurement = nodeLayout.cachedLayout;
			}
		}
		else
		{
			for (int j = 0; j < nodeLayout.nextCachedMeasurementsIndex; j++)
			{
				if (FloatsEqual(nodeLayout.cachedMeasurements[j].availableWidth, availableWidth) && FloatsEqual(nodeLayout.cachedMeasurements[j].availableHeight, availableHeight) && nodeLayout.cachedMeasurements[j].widthMeasureMode == widthMeasureMode && nodeLayout.cachedMeasurements[j].heightMeasureMode == heightMeasureMode)
				{
					cachedMeasurement = nodeLayout.cachedMeasurements[j];
					break;
				}
			}
		}
		if (!flag && cachedMeasurement != null)
		{
			nodeLayout.measuredDimensions[0] = cachedMeasurement.computedWidth;
			nodeLayout.measuredDimensions[1] = cachedMeasurement.computedHeight;
			bool flag2 = false;
		}
		else
		{
			bool flag3 = false;
			nodelayoutImpl(node, availableWidth, availableHeight, parentDirection, widthMeasureMode, heightMeasureMode, parentWidth, parentHeight, performLayout, config);
			bool flag4 = false;
			nodeLayout.lastParentDirection = parentDirection;
			if (cachedMeasurement == null)
			{
				if (nodeLayout.nextCachedMeasurementsIndex == 16)
				{
					bool flag5 = false;
					nodeLayout.nextCachedMeasurementsIndex = 0;
				}
				CachedMeasurement cachedMeasurement2 = null;
				if (performLayout)
				{
					cachedMeasurement2 = nodeLayout.cachedLayout;
				}
				else
				{
					cachedMeasurement2 = nodeLayout.cachedMeasurements[nodeLayout.nextCachedMeasurementsIndex];
					nodeLayout.nextCachedMeasurementsIndex++;
				}
				cachedMeasurement2.availableWidth = availableWidth;
				cachedMeasurement2.availableHeight = availableHeight;
				cachedMeasurement2.widthMeasureMode = widthMeasureMode;
				cachedMeasurement2.heightMeasureMode = heightMeasureMode;
				cachedMeasurement2.computedWidth = nodeLayout.measuredDimensions[0];
				cachedMeasurement2.computedHeight = nodeLayout.measuredDimensions[1];
			}
		}
		if (performLayout)
		{
			node.nodeLayout.Dimensions[0] = node.nodeLayout.measuredDimensions[0];
			node.nodeLayout.Dimensions[1] = node.nodeLayout.measuredDimensions[1];
			node.hasNewLayout = true;
			node.IsDirty = false;
		}
		gDepth--;
		nodeLayout.generationCount = currentGenerationCount;
		return flag || cachedMeasurement == null;
	}

	internal static void roundToPixelGrid(Node node, float pointScaleFactor, float absoluteLeft, float absoluteTop)
	{
		if ((double)pointScaleFactor == 0.0)
		{
			return;
		}
		float num = node.nodeLayout.Position[0];
		float num2 = node.nodeLayout.Position[1];
		float num3 = node.nodeLayout.Dimensions[0];
		float num4 = node.nodeLayout.Dimensions[1];
		float num5 = absoluteLeft + num;
		float num6 = absoluteTop + num2;
		float value = num5 + num3;
		float value2 = num6 + num4;
		bool flag = node.NodeType == NodeType.Text;
		node.nodeLayout.Position[0] = RoundValueToPixelGrid(num, pointScaleFactor, forceCeil: false, flag);
		node.nodeLayout.Position[1] = RoundValueToPixelGrid(num2, pointScaleFactor, forceCeil: false, flag);
		bool flag2 = !FloatsEqual(fmodf(num3 * pointScaleFactor, 1f), 0f) && !FloatsEqual(fmodf(num3 * pointScaleFactor, 1f), 1f);
		bool flag3 = !FloatsEqual(fmodf(num4 * pointScaleFactor, 1f), 0f) && !FloatsEqual(fmodf(num4 * pointScaleFactor, 1f), 1f);
		node.nodeLayout.Dimensions[0] = RoundValueToPixelGrid(value, pointScaleFactor, flag && flag2, flag && !flag2) - RoundValueToPixelGrid(num5, pointScaleFactor, forceCeil: false, flag);
		node.nodeLayout.Dimensions[1] = RoundValueToPixelGrid(value2, pointScaleFactor, flag && flag3, flag && !flag3) - RoundValueToPixelGrid(num6, pointScaleFactor, forceCeil: false, flag);
		foreach (Node child in node.Children)
		{
			roundToPixelGrid(child, pointScaleFactor, num5, num6);
		}
	}

	internal static void calcStartWidth(Node node, float parentWidth, out float out_width, out MeasureMode out_measureMode)
	{
		if (nodeIsStyleDimDefined(node, FlexDirection.Row, parentWidth))
		{
			float num = resolveValue(node.resolvedDimensions[(int)dim[2]], parentWidth);
			float num2 = nodeMarginForAxis(node, FlexDirection.Row, parentWidth);
			out_width = num + num2;
			out_measureMode = MeasureMode.Exactly;
			return;
		}
		if (resolveValue(node.nodeStyle.MaxDimensions[0], parentWidth) >= 0f)
		{
			out_width = resolveValue(node.nodeStyle.MaxDimensions[0], parentWidth);
			out_measureMode = MeasureMode.AtMost;
			return;
		}
		MeasureMode measureMode = MeasureMode.Exactly;
		if (FloatIsUndefined(parentWidth))
		{
			measureMode = MeasureMode.Undefined;
		}
		out_width = parentWidth;
		out_measureMode = measureMode;
	}

	internal static void calcStartHeight(Node node, float parentWidth, float parentHeight, out float out_height, out MeasureMode out_measureMode)
	{
		if (nodeIsStyleDimDefined(node, FlexDirection.Column, parentHeight))
		{
			float num = resolveValue(node.resolvedDimensions[(int)dim[0]], parentHeight);
			float num2 = nodeMarginForAxis(node, FlexDirection.Column, parentWidth);
			out_height = num + num2;
			out_measureMode = MeasureMode.Exactly;
			return;
		}
		if (resolveValue(node.nodeStyle.MaxDimensions[1], parentHeight) >= 0f)
		{
			out_height = resolveValue(node.nodeStyle.MaxDimensions[1], parentHeight);
			out_measureMode = MeasureMode.AtMost;
			return;
		}
		MeasureMode measureMode = MeasureMode.Exactly;
		if (FloatIsUndefined(parentHeight))
		{
			measureMode = MeasureMode.Undefined;
		}
		out_height = parentHeight;
		out_measureMode = measureMode;
	}

	internal static void log(Node node, LogLevel level, string format, params object[] args)
	{
		Console.WriteLine(format, args);
	}

	internal static void assertCond(bool cond, string format, params object[] args)
	{
		if (!cond)
		{
			throw new Exception(string.Format(format, args));
		}
	}

	internal static void assertWithNode(Node node, bool cond, string format, params object[] args)
	{
		assertCond(cond, format, args);
	}

	internal static float fmodf(float a, float b)
	{
		return a % b;
	}

	public static bool FloatsEqual(float a, float b)
	{
		if (FloatIsUndefined(a))
		{
			return FloatIsUndefined(b);
		}
		return Math.Abs(a - b) < 0.0001f;
	}

	public static float RoundValueToPixelGrid(float value, float pointScaleFactor, bool forceCeil, bool forceFloor)
	{
		float num = value * pointScaleFactor;
		float num2 = fmodf(num, 1f);
		if (FloatsEqual(num2, 0f))
		{
			num -= num2;
		}
		else if (FloatsEqual(num2, 1f))
		{
			num = num - num2 + 1f;
		}
		else if (forceCeil)
		{
			num = num - num2 + 1f;
		}
		else if (forceFloor)
		{
			num -= num2;
		}
		else
		{
			float num3 = 0f;
			if (num2 >= 0.5f)
			{
				num3 = 1f;
			}
			num = num - num2 + num3;
		}
		return num / pointScaleFactor;
	}

	public static void NodeCopyStyle(Node dstNode, Node srcNode)
	{
		if (!styleEq(dstNode.nodeStyle, srcNode.nodeStyle))
		{
			Style.Copy(dstNode.nodeStyle, srcNode.nodeStyle);
			nodeMarkDirtyInternal(dstNode);
		}
	}

	public static void Reset(ref Node node)
	{
		assertWithNode(node, node.Children.Count == 0, "Cannot reset a node which still has children attached");
		assertWithNode(node, node.Parent == null, "Cannot reset a node still attached to a parent");
		node.Children.Clear();
		Config config = node.config;
		node = CreateDefaultNode();
		if (config.UseWebDefaults)
		{
			node.nodeStyle.FlexDirection = FlexDirection.Row;
			node.nodeStyle.AlignContent = Align.Stretch;
		}
		node.config = config;
	}

	public static Node CreateDefaultNode()
	{
		return new Node();
	}

	public static Node CreateDefaultNode(Config config)
	{
		Node node = new Node();
		if (config.UseWebDefaults)
		{
			node.nodeStyle.FlexDirection = FlexDirection.Row;
			node.nodeStyle.AlignContent = Align.Stretch;
		}
		node.config = config;
		return node;
	}

	public static Config CreateDefaultConfig()
	{
		return new Config();
	}

	public static void CalculateLayout(Node node, float parentWidth, float parentHeight, Direction parentDirection)
	{
		currentGenerationCount++;
		resolveDimensions(node);
		calcStartWidth(node, parentWidth, out var out_width, out var out_measureMode);
		calcStartHeight(node, parentWidth, parentHeight, out var out_height, out var out_measureMode2);
		if (layoutNodeInternal(node, out_width, out_height, parentDirection, out_measureMode, out_measureMode2, parentWidth, parentHeight, performLayout: true, "initial", node.config))
		{
			nodeSetPosition(node, node.nodeLayout.Direction, parentWidth, parentHeight, parentWidth);
			roundToPixelGrid(node, node.config.PointScaleFactor, 0f, 0f);
			bool flag = false;
		}
	}
}
