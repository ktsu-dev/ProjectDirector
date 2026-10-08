// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

using DiffPlex.Model;

/// <summary>
/// Builds and writes the file that taking one diff block produces.
/// </summary>
/// <remarks>
/// DiffPlex hands back lines with their terminators stripped, and the file was read as text, which
/// drops any byte order mark. Rebuilding the file from those pieces with
/// <see cref="Environment.NewLine"/> and a plain <see cref="File.WriteAllText(string, string?)"/>
/// therefore rewrote every line ending and lost the BOM, turning a one-line take into a whole-file
/// change. The destination's own line ending and encoding are read back from disk here instead, so
/// only the taken lines differ.
/// </remarks>
internal static class DiffTake
{
	/// <summary>
	/// Builds the lines of the right-hand file after the left-hand side of <paramref name="block"/> is taken into it.
	/// </summary>
	/// <param name="diff">The diff of the left-hand file (old) against the right-hand file (new).</param>
	/// <param name="block">The block to take.</param>
	/// <returns>The right-hand file's lines with the block replaced by the left-hand lines.</returns>
	internal static Collection<string> TakeOldIntoNew(DiffResult diff, DiffBlock block)
	{
		Ensure.NotNull(diff);
		Ensure.NotNull(block);

		return
		[
			.. diff.PiecesNew.Take(block.InsertStartB),
			.. diff.PiecesOld.Skip(block.DeleteStartA).Take(block.DeleteCountA),
			.. diff.PiecesNew.Skip(block.InsertStartB + block.InsertCountB),
		];
	}

	/// <summary>
	/// Builds the lines of the left-hand file after the right-hand side of <paramref name="block"/> is taken into it.
	/// </summary>
	/// <param name="diff">The diff of the left-hand file (old) against the right-hand file (new).</param>
	/// <param name="block">The block to take.</param>
	/// <returns>The left-hand file's lines with the block replaced by the right-hand lines.</returns>
	internal static Collection<string> TakeNewIntoOld(DiffResult diff, DiffBlock block)
	{
		Ensure.NotNull(diff);
		Ensure.NotNull(block);

		return
		[
			.. diff.PiecesOld.Take(block.DeleteStartA),
			.. diff.PiecesNew.Skip(block.InsertStartB).Take(block.InsertCountB),
			.. diff.PiecesOld.Skip(block.DeleteStartA + block.DeleteCountA),
		];
	}

	/// <summary>
	/// Writes <paramref name="lines"/> over <paramref name="path"/>, keeping the line ending and
	/// encoding (including any byte order mark) the file already has.
	/// </summary>
	/// <param name="path">The file to overwrite.</param>
	/// <param name="lines">The file's new lines, without terminators.</param>
	/// <remarks>
	/// A file that does not exist yet, or has no line break to copy, gets
	/// <see cref="Environment.NewLine"/> and UTF-8 without a BOM, which is what was written before.
	/// </remarks>
	internal static void WriteLinesPreservingFormat(string path, IEnumerable<string> lines)
	{
		Ensure.NotNull(path);
		Ensure.NotNull(lines);

		Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		string newLine = Environment.NewLine;

		if (File.Exists(path))
		{
			using StreamReader reader = new(path, encoding, detectEncodingFromByteOrderMarks: true);
			string existing = reader.ReadToEnd();
			encoding = reader.CurrentEncoding;
			newLine = DetectNewLine(existing) ?? newLine;
		}

		File.WriteAllText(path, string.Join(newLine, lines), encoding);
	}

	/// <summary>
	/// Finds the line ending a text uses, judged by its first line break.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns>The first line break's characters, or <see langword="null"/> when there is none.</returns>
	internal static string? DetectNewLine(string text)
	{
		Ensure.NotNull(text);

		int index = text.IndexOfAny(['\r', '\n']);
		if (index < 0)
		{
			return null;
		}

		return text[index] == '\r'
			? index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r"
			: "\n";
	}
}
