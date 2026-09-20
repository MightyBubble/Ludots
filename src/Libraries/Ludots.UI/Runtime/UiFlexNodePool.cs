using System.Collections.Generic;
using FlexLayoutSharp;

namespace Ludots.UI.Runtime;

internal sealed class UiFlexNodePool
{
	private readonly Stack<Node> _free = new Stack<Node>(128);
	private readonly List<Node> _rented = new List<Node>(128);

	public Node Rent()
	{
		Node node = _free.Count > 0 ? _free.Pop() : Flex.CreateDefaultNode();
		Flex.ResetInPlace(node);
		_rented.Add(node);
		return node;
	}

	public void ReleaseAll()
	{
		for (int i = 0; i < _rented.Count; i++)
		{
			Node node = _rented[i];
			Flex.ResetInPlace(node);
			_free.Push(node);
		}
		_rented.Clear();
	}
}
