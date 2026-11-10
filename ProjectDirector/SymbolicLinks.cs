// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.IO;

/// <summary>
/// Keeps the file writers from writing through a symbolic link.
/// </summary>
/// <remarks>
/// <see cref="File.Copy(string, string, bool)"/> and <see cref="File.WriteAllText(string, string?)"/>
/// both open a link's target, so overwriting a destination that is a link changes a different file
/// and leaves the link alone. A repository that tracks <c>AGENTS.md</c> as a link to <c>CLAUDE.md</c>
/// would lose its <c>CLAUDE.md</c>, and a link that leads out of the repository would change a file
/// no managed repository owns. The writers refuse such a destination and report it instead.
/// </remarks>
internal static class SymbolicLinks
{
	/// <summary>
	/// Describes why <paramref name="path"/> must not be written through, if it is a symbolic link.
	/// </summary>
	/// <param name="path">The file about to be overwritten.</param>
	/// <returns>The reason to refuse, naming the link's target, or null when the path is not a link.</returns>
	internal static string? RefuseToWriteThrough(string path)
	{
		string? target;
		try
		{
			target = new FileInfo(path).LinkTarget;
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return failure.Message;
		}

		return target is null
			? null
			: $"the destination is a symbolic link to {target}, so it was left unchanged rather than written through";
	}
}
