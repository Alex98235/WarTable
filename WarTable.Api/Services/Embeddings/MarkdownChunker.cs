using System.Text;
using System.Text.RegularExpressions;

namespace WarTable.Api.Services.Embeddings;

public sealed record ChunkDraft(int Index, string HeadingPath, string Text);

/// <summary>
/// Splits markdown into heading-aware passages. The target is deliberately
/// approximate (characters / four); the profile's max input is the guardrail.
/// </summary>
public static class MarkdownChunker
{
	private static readonly Regex HeadingPattern =
		new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Compiled);

	private static readonly Regex ParagraphBreak =
		new(@"\n\s*\n", RegexOptions.Compiled);

	public static IReadOnlyList<ChunkDraft> Chunk(string? title, string? body, int maxInputTokens)
	{
		var maxChars = Math.Clamp(maxInputTokens > 0 ? maxInputTokens : 512, 200, 1000) * 4;
		var text = (body ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
		if (text.Length == 0)
			text = (title ?? "").Trim();
		if (text.Length == 0)
			return Array.Empty<ChunkDraft>();

		var sections = SplitSections(text);
		if (sections.Count == 0)
			sections.Add(("", text));

		var chunks = new List<ChunkDraft>();
		foreach (var section in sections)
		{
			foreach (var chunkText in SplitText(section.Text, maxChars))
			{
				var trimmed = chunkText.Trim();
				if (trimmed.Length > 0)
					chunks.Add(new ChunkDraft(chunks.Count, section.Heading, trimmed));
			}
		}

		return chunks;
	}

	private static List<(string Heading, string Text)> SplitSections(string text)
	{
		var sections = new List<(string Heading, string Text)>();
		var headings = new List<(int Level, string Text)>();
		var buffer = new StringBuilder();

		void Flush()
		{
			var value = buffer.ToString().Trim();
			if (value.Length > 0)
				sections.Add((CurrentHeading(headings), value));
			buffer.Clear();
		}

		foreach (var line in text.Split('\n'))
		{
			var match = HeadingPattern.Match(line);
			if (match.Success)
			{
				Flush();
				var level = match.Groups[1].Value.Length;
				while (headings.Count >= level)
					headings.RemoveAt(headings.Count - 1);
				headings.Add((level, match.Groups[2].Value.Trim()));
			}
			else
			{
				buffer.AppendLine(line);
			}
		}

		Flush();
		return sections;
	}

	private static string CurrentHeading(List<(int Level, string Text)> headings)
		=> headings.Count == 0 ? "" : string.Join(" > ", headings.Select(h => h.Text));

	private static List<string> SplitText(string text, int maxChars)
	{
		var result = new List<string>();
		var current = new StringBuilder();

		void Flush()
		{
			var value = current.ToString().Trim();
			if (value.Length > 0)
				result.Add(value);
			current.Clear();
		}

		foreach (var rawParagraph in ParagraphBreak.Split(text))
		{
			var paragraph = rawParagraph.Trim();
			if (paragraph.Length == 0)
				continue;

			if (paragraph.Length > maxChars)
			{
				Flush();
				result.AddRange(SplitLong(paragraph, maxChars));
				continue;
			}

			if (current.Length > 0 && current.Length + 2 + paragraph.Length > maxChars)
				Flush();
			if (current.Length > 0)
				current.Append("\n\n");
			current.Append(paragraph);
		}

		Flush();
		return result;
	}

	private static IEnumerable<string> SplitLong(string text, int maxChars)
	{
		var remaining = text.Trim();
		while (remaining.Length > maxChars)
		{
			var window = remaining[..maxChars];
			var cut = window.LastIndexOfAny(new[] { '.', '!', '?', '\n' });
			if (cut < maxChars / 2)
				cut = window.LastIndexOf(' ');
			if (cut <= 0)
				cut = maxChars;

			var part = remaining[..cut].Trim();
			if (part.Length > 0)
				yield return part;
			remaining = remaining[cut..].TrimStart();
		}

		if (remaining.Length > 0)
			yield return remaining;
	}
}
