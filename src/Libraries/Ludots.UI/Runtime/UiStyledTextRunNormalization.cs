using System;
using System.Collections.Generic;
using System.Text;

namespace Ludots.UI.Runtime;

public static class UiStyledTextRunNormalization
{
	public static IReadOnlyList<UiStyledTextRun> NormalizeWordBoundaries(IReadOnlyList<UiStyledTextRun> runs)
	{
		if (runs == null || runs.Count <= 1)
		{
			return runs ?? Array.Empty<UiStyledTextRun>();
		}

		var mutable = new List<UiStyledTextRun>(runs.Count);
		for (int i = 0; i < runs.Count; i++)
		{
			mutable.Add(runs[i] with { Text = runs[i].Text ?? string.Empty });
		}

		for (int i = 0; i < mutable.Count - 1; i++)
		{
			string left = mutable[i].Text;
			string right = mutable[i + 1].Text;
			if (left.Length == 0 || right.Length == 0)
			{
				continue;
			}

			if (!IsLatinWordChar(left[^1]) || !IsLatinWordChar(right[0]))
			{
				continue;
			}

			int wordStart = left.Length - 1;
			while (wordStart > 0 && IsLatinWordChar(left[wordStart - 1]))
			{
				wordStart--;
			}

			string moved = left.Substring(wordStart);
			mutable[i] = mutable[i] with { Text = left.Substring(0, wordStart) };
			mutable[i + 1] = mutable[i + 1] with { Text = moved + right };
		}

		var compacted = new List<UiStyledTextRun>(mutable.Count);
		for (int i = 0; i < mutable.Count; i++)
		{
			if (mutable[i].Text.Length == 0)
			{
				continue;
			}

			compacted.Add(mutable[i]);
		}

		return compacted;
	}

	public static string ConcatPlain(IReadOnlyList<UiStyledTextRun> runs)
	{
		if (runs == null || runs.Count == 0)
		{
			return string.Empty;
		}

		if (runs.Count == 1)
		{
			return runs[0].Text ?? string.Empty;
		}

		var builder = new StringBuilder();
		for (int i = 0; i < runs.Count; i++)
		{
			builder.Append(runs[i].Text);
		}

		return builder.ToString();
	}

	private static bool IsLatinWordChar(char ch) =>
		char.IsAsciiLetterOrDigit(ch) || ch == '\'' || ch == '’';
}
