using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed class UiStyleSheet
{
	private readonly List<UiStyleRule> _rules = new List<UiStyleRule>();

	private readonly Dictionary<string, UiKeyframeDefinition> _keyframes = new Dictionary<string, UiKeyframeDefinition>(StringComparer.OrdinalIgnoreCase);

	public IReadOnlyList<UiStyleRule> Rules => _rules;

	public IReadOnlyCollection<UiKeyframeDefinition> Keyframes => _keyframes.Values;

	public UiStyleSheet AddRule(string selectorText, Action<UiStyleDeclaration> configure)
	{
		ArgumentNullException.ThrowIfNull(configure, "configure");
		UiStyleDeclaration uiStyleDeclaration = new UiStyleDeclaration();
		configure(uiStyleDeclaration);
		foreach (UiSelector item in UiSelectorParser.ParseMany(selectorText))
		{
			_rules.Add(new UiStyleRule(item, uiStyleDeclaration, _rules.Count));
		}
		return this;
	}

	public UiStyleSheet AddRule(UiSelector selector, UiStyleDeclaration declaration)
	{
		_rules.Add(new UiStyleRule(selector, declaration, _rules.Count));
		return this;
	}

	public UiStyleSheet AddKeyframes(UiKeyframeDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition, "definition");
		_keyframes[definition.Name] = definition;
		return this;
	}

	public bool TryGetKeyframes(string name, out UiKeyframeDefinition? definition)
	{
		return _keyframes.TryGetValue(name, out definition);
	}
}
