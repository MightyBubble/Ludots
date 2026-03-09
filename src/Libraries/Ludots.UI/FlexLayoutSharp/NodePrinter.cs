using System.Text;

namespace FlexLayoutSharp;

public class NodePrinter
{
	private StringBuilder writer;

	private bool PrintOptionsLayout;

	private bool PrintOptionsStyle;

	private bool PrintOptionsChildren;

	public static string PrintToString(Node root)
	{
		StringBuilder stringBuilder = new StringBuilder();
		NodePrinter nodePrinter = new NodePrinter(stringBuilder, PrintOptionsLayout: true, PrintOptionsStyle: true, PrintOptionsChildren: true);
		nodePrinter.Print(root);
		return stringBuilder.ToString();
	}

	public NodePrinter(StringBuilder writer, bool PrintOptionsLayout, bool PrintOptionsStyle, bool PrintOptionsChildren)
	{
		this.writer = writer;
		this.PrintOptionsLayout = PrintOptionsLayout;
		this.PrintOptionsStyle = PrintOptionsStyle;
		this.PrintOptionsChildren = PrintOptionsChildren;
	}

	public void Print(Node node)
	{
		PrintNode(node, 0);
	}

	private void PrintNode(Node node, int level)
	{
		printIndent(level);
		printf("<div ");
		if (node.printFunc != null)
		{
			node.printFunc(node);
		}
		if (PrintOptionsLayout)
		{
			printf("layout=\"");
			printf($"width: {node.LayoutGetWidth()}; ");
			printf($"height: {node.LayoutGetHeight()}; ");
			printf($"top: {node.LayoutGetTop()}; ");
			printf($"left: {node.LayoutGetLeft()};");
			printf("\" ");
		}
		if (PrintOptionsStyle)
		{
			printf("style=\"");
			if (node.nodeStyle.FlexDirection != Constant.nodeDefaults.nodeStyle.FlexDirection)
			{
				printf("flex-direction: " + Flex.FlexDirectionToString(node.nodeStyle.FlexDirection) + "; ");
			}
			if (node.nodeStyle.JustifyContent != Constant.nodeDefaults.nodeStyle.JustifyContent)
			{
				printf("justify-content: " + Flex.JustifyToString(node.nodeStyle.JustifyContent) + "; ");
			}
			if (node.nodeStyle.AlignItems != Constant.nodeDefaults.nodeStyle.AlignItems)
			{
				printf("align-items: " + Flex.AlignToString(node.nodeStyle.AlignItems) + "; ");
			}
			if (node.nodeStyle.AlignContent != Constant.nodeDefaults.nodeStyle.AlignContent)
			{
				printf("align-content: " + Flex.AlignToString(node.nodeStyle.AlignContent) + "; ");
			}
			if (node.nodeStyle.AlignSelf != Constant.nodeDefaults.nodeStyle.AlignSelf)
			{
				printf("align-self: " + Flex.AlignToString(node.nodeStyle.AlignSelf) + "; ");
			}
			printFloatIfNotUndefined(node, "flex-grow", node.nodeStyle.FlexGrow);
			printFloatIfNotUndefined(node, "flex-shrink", node.nodeStyle.FlexShrink);
			printNumberIfNotAuto(node, "flex-basis", node.nodeStyle.FlexBasis);
			printFloatIfNotUndefined(node, "flex", node.nodeStyle.Flex);
			if (node.nodeStyle.FlexWrap != Constant.nodeDefaults.nodeStyle.FlexWrap)
			{
				printf("flexWrap: " + Flex.WrapToString(node.nodeStyle.FlexWrap) + "; ");
			}
			if (node.nodeStyle.Overflow != Constant.nodeDefaults.nodeStyle.Overflow)
			{
				printf("overflow: " + Flex.OverflowToString(node.nodeStyle.Overflow) + "; ");
			}
			if (node.nodeStyle.Display != Constant.nodeDefaults.nodeStyle.Display)
			{
				printf("display: " + Flex.DisplayToString(node.nodeStyle.Display) + "; ");
			}
			printEdges(node, "margin", node.nodeStyle.Margin);
			printEdges(node, "padding", node.nodeStyle.Padding);
			printEdges(node, "border", node.nodeStyle.Border);
			printNumberIfNotAuto(node, "width", node.nodeStyle.Dimensions[0]);
			printNumberIfNotAuto(node, "height", node.nodeStyle.Dimensions[1]);
			printNumberIfNotAuto(node, "max-width", node.nodeStyle.MaxDimensions[0]);
			printNumberIfNotAuto(node, "max-height", node.nodeStyle.MaxDimensions[1]);
			printNumberIfNotAuto(node, "min-width", node.nodeStyle.MinDimensions[0]);
			printNumberIfNotAuto(node, "min-height", node.nodeStyle.MinDimensions[1]);
			if (node.nodeStyle.PositionType != Constant.nodeDefaults.nodeStyle.PositionType)
			{
				printf("position: " + Flex.PositionTypeToString(node.nodeStyle.PositionType) + "; ");
			}
			printEdgeIfNotUndefined(node, "left", node.nodeStyle.Position, Edge.Left);
			printEdgeIfNotUndefined(node, "right", node.nodeStyle.Position, Edge.Right);
			printEdgeIfNotUndefined(node, "top", node.nodeStyle.Position, Edge.Top);
			printEdgeIfNotUndefined(node, "bottom", node.nodeStyle.Position, Edge.Bottom);
			printf("\"");
			if (node.measureFunc != null)
			{
				printf(" has-custom-measure=\"true\"");
			}
		}
		printf(">");
		int count = node.Children.Count;
		if (PrintOptionsChildren && count > 0)
		{
			for (int i = 0; i < count; i++)
			{
				printf("\n");
				PrintNode(node.Children[i], level + 1);
			}
			printIndent(level);
			printf("\n");
		}
		if (count != 0)
		{
			printIndent(level);
		}
		printf("</div>");
	}

	private void printEdges(Node node, string str, Value[] edges)
	{
		if (fourValuesEqual(edges))
		{
			printNumberIfNotZero(node, str, edges[0]);
			printNumberIfNotZero(node, str, edges[8]);
			return;
		}
		for (int i = 0; i < 9; i++)
		{
			string str2 = str + "-" + Flex.EdgeToString((Edge)i);
			printNumberIfNotZero(node, str2, edges[i]);
		}
	}

	private void printEdgeIfNotUndefined(Node node, string str, Value[] edges, Edge edge)
	{
		printNumberIfNotUndefined(node, str, Flex.computedEdgeValue(edges, edge, Value.UndefinedValue));
	}

	private void printFloatIfNotUndefined(Node node, string str, float number)
	{
		if (!float.IsNaN(number))
		{
			printf($"{str}: {number}; ");
		}
	}

	private void printNumberIfNotUndefined(Node node, string str, Value number)
	{
		if (number.unit == Unit.Undefined)
		{
			return;
		}
		if (number.unit == Unit.Auto)
		{
			printf(str + ": auto; ");
			return;
		}
		string value = "%";
		if (number.unit == Unit.Point)
		{
			value = "px";
		}
		printf($"{str}: {number.value}{value}; ");
	}

	private void printNumberIfNotAuto(Node node, string str, Value number)
	{
		if (number.unit != Unit.Auto)
		{
			printNumberIfNotUndefined(node, str, number);
		}
	}

	private void printNumberIfNotZero(Node node, string str, Value number)
	{
		if (!Flex.FloatsEqual(number.value, 0f))
		{
			printNumberIfNotUndefined(node, str, number);
		}
	}

	private void printf(string str)
	{
		writer.Append(str);
	}

	private void printIndent(int n)
	{
		for (int i = 0; i < n; i++)
		{
			writer.Append("  ");
		}
	}

	private bool fourValuesEqual(Value[] four)
	{
		return Flex.ValueEqual(four[0], four[1]) && Flex.ValueEqual(four[0], four[2]) && Flex.ValueEqual(four[0], four[3]);
	}
}
