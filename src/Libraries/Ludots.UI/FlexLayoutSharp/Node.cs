using System;
using System.Collections.Generic;

namespace FlexLayoutSharp;

public class Node
{
	internal readonly Style nodeStyle = new Style();

	internal readonly Flex.Layout nodeLayout = new Flex.Layout();

	internal int lineIndex;

	internal Node Parent = null;

	internal readonly List<Node> Children = new List<Node>();

	internal Node NextChild;

	internal MeasureFunc measureFunc;

	internal BaselineFunc baselineFunc;

	internal PrintFunc printFunc;

	internal Config config = Constant.configDefaults;

	internal bool hasNewLayout = true;

	internal NodeType NodeType = NodeType.Default;

	internal readonly Value[] resolvedDimensions = new Value[2]
	{
		Flex.ValueUndefined,
		Flex.ValueUndefined
	};

	public object Context;

	public int ChildrenCount => Children.Count;

	public bool IsDirty { get; internal set; }

	public void CopyStyle(Node other)
	{
		if (other == null)
		{
			throw new ArgumentNullException("other");
		}
		Style.Copy(nodeStyle, other.nodeStyle);
	}

	public void MarkAsDirty()
	{
		Flex.nodeMarkDirtyInternal(this);
	}

	public void StyleSetWidth(float width)
	{
		Value value = nodeStyle.Dimensions[0];
		if (value.value != width || value.unit != Unit.Point)
		{
			value.value = width;
			value.unit = Unit.Point;
			if (Flex.FloatIsUndefined(width))
			{
				value.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetWidthPercent(float width)
	{
		Value value = nodeStyle.Dimensions[0];
		if (value.value != width || value.unit != Unit.Percent)
		{
			value.value = width;
			value.unit = Unit.Percent;
			if (Flex.FloatIsUndefined(width))
			{
				value.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetWidthAuto()
	{
		Value value = nodeStyle.Dimensions[0];
		if (value.unit != Unit.Auto)
		{
			value.value = float.NaN;
			value.unit = Unit.Auto;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetWidth()
	{
		return nodeStyle.Dimensions[0];
	}

	public void StyleSetHeight(float height)
	{
		Value value = nodeStyle.Dimensions[1];
		if (value.value != height || value.unit != Unit.Point)
		{
			value.value = height;
			value.unit = Unit.Point;
			if (Flex.FloatIsUndefined(height))
			{
				value.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetHeightPercent(float height)
	{
		Value value = nodeStyle.Dimensions[1];
		if (value.value != height || value.unit != Unit.Percent)
		{
			value.value = height;
			value.unit = Unit.Percent;
			if (Flex.FloatIsUndefined(height))
			{
				value.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetHeightAuto()
	{
		Value value = nodeStyle.Dimensions[1];
		if (value.unit != Unit.Auto)
		{
			value.value = float.NaN;
			value.unit = Unit.Auto;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetHeight()
	{
		return nodeStyle.Dimensions[1];
	}

	public void StyleSetPositionType(PositionType positionType)
	{
		if (nodeStyle.PositionType != positionType)
		{
			nodeStyle.PositionType = positionType;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public PositionType StyleGetPositionType()
	{
		return nodeStyle.PositionType;
	}

	public void StyleSetPosition(Edge edge, float position)
	{
		Value value = nodeStyle.Position[(int)edge];
		if (value.value != position || value.unit != Unit.Point)
		{
			value.value = position;
			value.unit = Unit.Point;
			if (Flex.FloatIsUndefined(position))
			{
				value.unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetPositionPercent(Edge edge, float position)
	{
		Value value = nodeStyle.Position[(int)edge];
		if (value.value != position || value.unit != Unit.Percent)
		{
			value.value = position;
			value.unit = Unit.Percent;
			if (Flex.FloatIsUndefined(position))
			{
				value.unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetPosition(Edge edge)
	{
		return nodeStyle.Position[(int)edge];
	}

	public void StyleSetDirection(Direction direction)
	{
		if (nodeStyle.Direction != direction)
		{
			nodeStyle.Direction = direction;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Direction StyleGetDirection()
	{
		return nodeStyle.Direction;
	}

	public void StyleSetFlexDirection(FlexDirection flexDirection)
	{
		if (nodeStyle.FlexDirection != flexDirection)
		{
			nodeStyle.FlexDirection = flexDirection;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public FlexDirection StyleGetFlexDirection()
	{
		return nodeStyle.FlexDirection;
	}

	public void StyleSetJustifyContent(Justify justifyContent)
	{
		if (nodeStyle.JustifyContent != justifyContent)
		{
			nodeStyle.JustifyContent = justifyContent;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Justify StyleGetJustifyContent()
	{
		return nodeStyle.JustifyContent;
	}

	public void StyleSetAlignContent(Align alignContent)
	{
		if (nodeStyle.AlignContent != alignContent)
		{
			nodeStyle.AlignContent = alignContent;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Align StyleGetAlignContent()
	{
		return nodeStyle.AlignContent;
	}

	public void StyleSetAlignItems(Align alignItems)
	{
		if (nodeStyle.AlignItems != alignItems)
		{
			nodeStyle.AlignItems = alignItems;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Align StyleGetAlignItems()
	{
		return nodeStyle.AlignItems;
	}

	public void StyleSetAlignSelf(Align alignSelf)
	{
		if (nodeStyle.AlignSelf != alignSelf)
		{
			nodeStyle.AlignSelf = alignSelf;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Align StyleGetAlignSelf()
	{
		return nodeStyle.AlignSelf;
	}

	public void StyleSetFlexWrap(Wrap flexWrap)
	{
		if (nodeStyle.FlexWrap != flexWrap)
		{
			nodeStyle.FlexWrap = flexWrap;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Wrap StyleGetFlexWrap()
	{
		return nodeStyle.FlexWrap;
	}

	public void StyleSetOverflow(Overflow overflow)
	{
		if (nodeStyle.Overflow != overflow)
		{
			nodeStyle.Overflow = overflow;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Overflow StyleGetOverflow()
	{
		return nodeStyle.Overflow;
	}

	public void StyleSetDisplay(Display display)
	{
		if (nodeStyle.Display != display)
		{
			nodeStyle.Display = display;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Display StyleGetDisplay()
	{
		return nodeStyle.Display;
	}

	public void StyleSetFlex(float flex)
	{
		if (nodeStyle.Flex != flex)
		{
			nodeStyle.Flex = flex;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public float StyleGetFlex()
	{
		return nodeStyle.Flex;
	}

	public void StyleSetFlexGrow(float flexGrow)
	{
		if (nodeStyle.FlexGrow != flexGrow)
		{
			nodeStyle.FlexGrow = flexGrow;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public float StyleGetFlexGrow()
	{
		if (float.IsNaN(nodeStyle.FlexGrow))
		{
			return 0f;
		}
		return nodeStyle.FlexGrow;
	}

	public float StyleGetFlexShrink()
	{
		if (float.IsNaN(nodeStyle.FlexShrink))
		{
			if (config.UseWebDefaults)
			{
				return 1f;
			}
			return 0f;
		}
		return nodeStyle.FlexShrink;
	}

	public void StyleSetFlexShrink(float flexShrink)
	{
		if (nodeStyle.FlexShrink != flexShrink)
		{
			nodeStyle.FlexShrink = flexShrink;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetFlexBasis(float flexBasis)
	{
		if (nodeStyle.FlexBasis.value != flexBasis || nodeStyle.FlexBasis.unit != Unit.Point)
		{
			nodeStyle.FlexBasis.value = flexBasis;
			nodeStyle.FlexBasis.unit = Unit.Point;
			if (Flex.FloatIsUndefined(flexBasis))
			{
				nodeStyle.FlexBasis.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetFlexBasisPercent(float flexBasis)
	{
		if (nodeStyle.FlexBasis.value != flexBasis || nodeStyle.FlexBasis.unit != Unit.Percent)
		{
			nodeStyle.FlexBasis.value = flexBasis;
			nodeStyle.FlexBasis.unit = Unit.Percent;
			if (Flex.FloatIsUndefined(flexBasis))
			{
				nodeStyle.FlexBasis.unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void NodeStyleSetFlexBasisAuto()
	{
		if (nodeStyle.FlexBasis.unit != Unit.Auto)
		{
			nodeStyle.FlexBasis.value = float.NaN;
			nodeStyle.FlexBasis.unit = Unit.Auto;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value NodeStyleGetFlexBasis()
	{
		return nodeStyle.FlexBasis;
	}

	public void StyleSetMargin(Edge edge, float margin)
	{
		if (nodeStyle.Margin[(int)edge].value != margin || nodeStyle.Margin[(int)edge].unit != Unit.Point)
		{
			nodeStyle.Margin[(int)edge].value = margin;
			nodeStyle.Margin[(int)edge].unit = Unit.Point;
			if (Flex.FloatIsUndefined(margin))
			{
				nodeStyle.Margin[(int)edge].unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetMarginPercent(Edge edge, float margin)
	{
		if (nodeStyle.Margin[(int)edge].value != margin || nodeStyle.Margin[(int)edge].unit != Unit.Percent)
		{
			nodeStyle.Margin[(int)edge].value = margin;
			nodeStyle.Margin[(int)edge].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(margin))
			{
				nodeStyle.Margin[(int)edge].unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetMargin(Edge edge)
	{
		return nodeStyle.Margin[(int)edge];
	}

	public void StyleSetMarginAuto(Edge edge)
	{
		if (nodeStyle.Margin[(int)edge].unit != Unit.Auto)
		{
			nodeStyle.Margin[(int)edge].value = float.NaN;
			nodeStyle.Margin[(int)edge].unit = Unit.Auto;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetPadding(Edge edge, float padding)
	{
		if (nodeStyle.Padding[(int)edge].value != padding || nodeStyle.Padding[(int)edge].unit != Unit.Point)
		{
			nodeStyle.Padding[(int)edge].value = padding;
			nodeStyle.Padding[(int)edge].unit = Unit.Point;
			if (Flex.FloatIsUndefined(padding))
			{
				nodeStyle.Padding[(int)edge].unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetPaddingPercent(Edge edge, float padding)
	{
		if (nodeStyle.Padding[(int)edge].value != padding || nodeStyle.Padding[(int)edge].unit != Unit.Percent)
		{
			nodeStyle.Padding[(int)edge].value = padding;
			nodeStyle.Padding[(int)edge].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(padding))
			{
				nodeStyle.Padding[(int)edge].unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetPadding(Edge edge)
	{
		return nodeStyle.Padding[(int)edge];
	}

	public void StyleSetBorder(Edge edge, float border)
	{
		if (nodeStyle.Border[(int)edge].value != border || nodeStyle.Border[(int)edge].unit != Unit.Point)
		{
			nodeStyle.Border[(int)edge].value = border;
			nodeStyle.Border[(int)edge].unit = Unit.Point;
			if (Flex.FloatIsUndefined(border))
			{
				nodeStyle.Border[(int)edge].unit = Unit.Undefined;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public float StyleGetBorder(Edge edge)
	{
		return nodeStyle.Border[(int)edge].value;
	}

	public void StyleSetMinWidth(float minWidth)
	{
		if (nodeStyle.MinDimensions[0].value != minWidth || nodeStyle.MinDimensions[0].unit != Unit.Point)
		{
			nodeStyle.MinDimensions[0].value = minWidth;
			nodeStyle.MinDimensions[0].unit = Unit.Point;
			if (Flex.FloatIsUndefined(minWidth))
			{
				nodeStyle.MinDimensions[0].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetMinWidthPercent(float minWidth)
	{
		if (nodeStyle.MinDimensions[0].value != minWidth || nodeStyle.MinDimensions[0].unit != Unit.Percent)
		{
			nodeStyle.MinDimensions[0].value = minWidth;
			nodeStyle.MinDimensions[0].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(minWidth))
			{
				nodeStyle.MinDimensions[0].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetMinWidth()
	{
		return nodeStyle.MinDimensions[0];
	}

	public void StyleSetMinHeight(float minHeight)
	{
		if (nodeStyle.MinDimensions[1].value != minHeight || nodeStyle.MinDimensions[1].unit != Unit.Point)
		{
			nodeStyle.MinDimensions[1].value = minHeight;
			nodeStyle.MinDimensions[1].unit = Unit.Point;
			if (Flex.FloatIsUndefined(minHeight))
			{
				nodeStyle.MinDimensions[1].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetMinHeightPercent(float minHeight)
	{
		if (nodeStyle.MinDimensions[1].value != minHeight || nodeStyle.MinDimensions[1].unit != Unit.Percent)
		{
			nodeStyle.MinDimensions[1].value = minHeight;
			nodeStyle.MinDimensions[1].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(minHeight))
			{
				nodeStyle.MinDimensions[1].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetMinHeight()
	{
		return nodeStyle.MinDimensions[1];
	}

	public void StyleSetMaxWidth(float maxWidth)
	{
		if (nodeStyle.MaxDimensions[0].value != maxWidth || nodeStyle.MaxDimensions[0].unit != Unit.Point)
		{
			nodeStyle.MaxDimensions[0].value = maxWidth;
			nodeStyle.MaxDimensions[0].unit = Unit.Point;
			if (Flex.FloatIsUndefined(maxWidth))
			{
				nodeStyle.MaxDimensions[0].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetMaxWidthPercent(float maxWidth)
	{
		if (nodeStyle.MaxDimensions[0].value != maxWidth || nodeStyle.MaxDimensions[0].unit != Unit.Percent)
		{
			nodeStyle.MaxDimensions[0].value = maxWidth;
			nodeStyle.MaxDimensions[0].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(maxWidth))
			{
				nodeStyle.MaxDimensions[0].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetMaxWidth()
	{
		return nodeStyle.MaxDimensions[0];
	}

	public void StyleSetMaxHeight(float maxHeight)
	{
		if (nodeStyle.MaxDimensions[1].value != maxHeight || nodeStyle.MaxDimensions[1].unit != Unit.Point)
		{
			nodeStyle.MaxDimensions[1].value = maxHeight;
			nodeStyle.MaxDimensions[1].unit = Unit.Point;
			if (Flex.FloatIsUndefined(maxHeight))
			{
				nodeStyle.MaxDimensions[1].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public void StyleSetMaxHeightPercent(float maxHeight)
	{
		if (nodeStyle.MaxDimensions[1].value != maxHeight || nodeStyle.MaxDimensions[1].unit != Unit.Percent)
		{
			nodeStyle.MaxDimensions[1].value = maxHeight;
			nodeStyle.MaxDimensions[1].unit = Unit.Percent;
			if (Flex.FloatIsUndefined(maxHeight))
			{
				nodeStyle.MaxDimensions[1].unit = Unit.Auto;
			}
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public Value StyleGetMaxHeight()
	{
		return nodeStyle.MaxDimensions[1];
	}

	public void StyleSetAspectRatio(float aspectRatio)
	{
		if (nodeStyle.AspectRatio != aspectRatio)
		{
			nodeStyle.AspectRatio = aspectRatio;
			Flex.nodeMarkDirtyInternal(this);
		}
	}

	public float LayoutGetLeft()
	{
		return nodeLayout.Position[0];
	}

	public float LayoutGetTop()
	{
		return nodeLayout.Position[1];
	}

	public float LayoutGetRight()
	{
		return nodeLayout.Position[2];
	}

	public float LayoutGetBottom()
	{
		return nodeLayout.Position[3];
	}

	public float LayoutGetWidth()
	{
		return nodeLayout.Dimensions[0];
	}

	public float LayoutGetHeight()
	{
		return nodeLayout.Dimensions[1];
	}

	public float LayoutGetMargin(Edge edge)
	{
		Flex.assertWithNode(this, edge < Edge.End, "Cannot get layout properties of multi-edge shorthands");
		switch (edge)
		{
		case Edge.Left:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Margin[5];
			}
			return nodeLayout.Margin[4];
		case Edge.Right:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Margin[4];
			}
			return nodeLayout.Margin[5];
		default:
			return nodeLayout.Margin[(int)edge];
		}
	}

	public float LayoutGetBorder(Edge edge)
	{
		Flex.assertWithNode(this, edge < Edge.End, "Cannot get layout properties of multi-edge shorthands");
		switch (edge)
		{
		case Edge.Left:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Border[5];
			}
			return nodeLayout.Border[4];
		case Edge.Right:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Border[4];
			}
			return nodeLayout.Border[5];
		default:
			return nodeLayout.Border[(int)edge];
		}
	}

	public float LayoutGetPadding(Edge edge)
	{
		Flex.assertWithNode(this, edge < Edge.End, "Cannot get layout properties of multi-edge shorthands");
		switch (edge)
		{
		case Edge.Left:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Padding[5];
			}
			return nodeLayout.Padding[4];
		case Edge.Right:
			if (nodeLayout.Direction == Direction.RTL)
			{
				return nodeLayout.Padding[4];
			}
			return nodeLayout.Padding[5];
		default:
			return nodeLayout.Padding[(int)edge];
		}
	}

	public Direction LayoutGetDirection()
	{
		return nodeLayout.Direction;
	}

	public bool LayoutGetHadOverflow()
	{
		return nodeLayout.HadOverflow;
	}

	public void SetMeasureFunc(MeasureFunc measureFunc)
	{
		Flex.SetMeasureFunc(this, measureFunc);
	}

	public MeasureFunc GetMeasureFunc()
	{
		return measureFunc;
	}

	public void SetBaselineFunc(BaselineFunc baselineFunc)
	{
		this.baselineFunc = baselineFunc;
	}

	public BaselineFunc GetBaselineFunc()
	{
		return baselineFunc;
	}

	public void SetPrintFunc(PrintFunc printFunc)
	{
		this.printFunc = printFunc;
	}

	public PrintFunc GetPrintFunc()
	{
		return printFunc;
	}

	public Node GetParent()
	{
		return Parent;
	}

	public IEnumerable<Node> GetChildrenIter()
	{
		return Children;
	}

	public Node GetChild(int idx)
	{
		return Flex.GetChild(this, idx);
	}

	public void AddChild(Node child)
	{
		if (child != null && child.Parent != this)
		{
			Flex.InsertChild(this, child, ChildrenCount);
		}
	}

	public int IndexOfChild(Node child)
	{
		return Children.IndexOf(child);
	}

	public void InsertChild(Node child, int idx)
	{
		if (child != null)
		{
			Flex.InsertChild(this, child, idx);
		}
	}

	public void RemoveChild(Node child)
	{
		Flex.RemoveChild(this, child);
	}

	public bool ReplaceChild(int index, Node child)
	{
		if (child == null)
		{
			return false;
		}
		if (0 <= index && index < ChildrenCount)
		{
			child.Parent = this;
			Children[index] = child;
			MarkAsDirty();
			return true;
		}
		return false;
	}

	public void SetParent(Node parent)
	{
		if (parent != Parent)
		{
			RemoveParent();
			parent.AddChild(this);
		}
	}

	public void RemoveParent()
	{
		if (Parent != null)
		{
			Parent.RemoveChild(this);
		}
	}

	public void CalculateLayout(float parentWidth, float parentHeight, Direction parentDirection)
	{
		Flex.CalculateLayout(this, parentWidth, parentHeight, parentDirection);
	}
}
