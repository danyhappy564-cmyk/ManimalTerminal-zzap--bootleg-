using System;
using System.Collections.Generic;

namespace Manimal.Terminal;

internal sealed class TerminalArtilleryPolicy
{
	internal readonly struct Rule
	{
		internal readonly string Id;

		internal readonly string Include;

		internal readonly string Exclude;

		internal readonly bool Planned;

		internal readonly bool Active;

		internal Rule(string id, bool planned, bool active, string include, string exclude)
		{
			Id = id;
			Planned = planned;
			Active = active;
			Include = include;
			Exclude = exclude;
		}
	}

	private readonly Rule[] _rules;

	private readonly HashSet<string> _enabled = new HashSet<string>(StringComparer.Ordinal);

	internal TerminalArtilleryPolicy(Rule[] rules)
	{
		_rules = rules ?? Array.Empty<Rule>();
		Reset();
	}

	internal void Reset()
	{
		_enabled.Clear();
		Rule[] rules = _rules;
		for (int i = 0; i < rules.Length; i++)
		{
			Rule rule = rules[i];
			if (rule.Planned && string.IsNullOrEmpty(rule.Include))
			{
				_enabled.Add(rule.Id);
			}
		}
	}

	internal bool Contains(string id)
	{
		if (id != null)
		{
			return _enabled.Contains(id);
		}
		return false;
	}

	internal void Apply(string trigger, List<string> added, List<string> removed)
	{
		added.Clear();
		removed.Clear();
		if (string.IsNullOrEmpty(trigger))
		{
			return;
		}
		Rule[] rules = _rules;
		for (int i = 0; i < rules.Length; i++)
		{
			Rule rule = rules[i];
			if (rule.Planned && rule.Active && string.Equals(rule.Include, trigger, StringComparison.Ordinal) && _enabled.Add(rule.Id))
			{
				added.Add(rule.Id);
			}
		}
		rules = _rules;
		for (int i = 0; i < rules.Length; i++)
		{
			Rule rule2 = rules[i];
			if (rule2.Planned && rule2.Active && string.Equals(rule2.Exclude, trigger, StringComparison.Ordinal))
			{
				removed.Add(rule2.Id);
			}
		}
	}
}
