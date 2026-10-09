// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.ObjectModel;
using System.IO;
using ktsu.Semantics.Paths;

/// <summary>
/// The path handling behind the repo and compare browsers, kept apart from the drawing so it can be
/// driven against real throwaway directories.
/// </summary>
/// <remarks>
/// Every entry is relative to the repository root, not to the folder being browsed, so it already
/// includes the browse path. Combining it with the browse path again is what used to send the
/// browsers to <c>src/src/...</c>. Whether an entry is a folder is asked of the filesystem rather
/// than read from a trailing separator, because <see cref="RelativePath"/> normalises that away.
/// </remarks>
internal static class RepoBrowsing
{
	/// <summary>
	/// Lists one folder of a repository.
	/// </summary>
	/// <param name="repoRoot">The repository's local path.</param>
	/// <param name="browsePath">The folder to list, relative to <paramref name="repoRoot"/>.</param>
	/// <returns>Each entry relative to <paramref name="repoRoot"/>, or nothing when the folder is not there.</returns>
	internal static Collection<RelativePath> List(string repoRoot, string browsePath)
	{
		try
		{
			return [.. Directory.EnumerateFileSystemEntries(Path.Join(repoRoot, browsePath))
				.Select(entry => RelativePath.Create<RelativePath>(Path.GetRelativePath(repoRoot, entry)))];
		}
		catch (DirectoryNotFoundException)
		{
			// The other repository may not have this folder at all.
			return [];
		}
	}

	/// <summary>
	/// Gets whether an entry is a folder in any of the given repositories.
	/// </summary>
	/// <param name="entry">An entry from <see cref="List"/>.</param>
	/// <param name="repoRoots">The repositories it may have come from.</param>
	/// <returns><see langword="true"/> when it is a folder in at least one of them.</returns>
	internal static bool IsDirectory(RelativePath entry, params string[] repoRoots) =>
		repoRoots.Any(root => Directory.Exists(Path.Join(root, entry)));

	/// <summary>
	/// Copies an entry that exists in one repository into the same place in another.
	/// </summary>
	/// <param name="entry">An entry from <see cref="List"/>.</param>
	/// <param name="fromRoot">The repository that has it.</param>
	/// <param name="toRoot">The repository that does not.</param>
	/// <returns>Why the copy failed, or null when it succeeded.</returns>
	/// <remarks>A folder is created empty, as the browser always has, rather than copied with its contents.</remarks>
	internal static string? Copy(RelativePath entry, string fromRoot, string toRoot)
	{
		string source = Path.Join(fromRoot, entry);
		string destination = Path.Join(toRoot, entry);

		try
		{
			if (Directory.Exists(source))
			{
				_ = Directory.CreateDirectory(destination);
			}
			else
			{
				_ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				File.Copy(source, destination);
			}

			return null;
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			return failure.Message;
		}
	}

	/// <summary>
	/// Deletes an entry from one repository.
	/// </summary>
	/// <param name="entry">An entry from <see cref="List"/>.</param>
	/// <param name="repoRoot">The repository to delete it from.</param>
	/// <returns>Why the delete failed, or null when it succeeded.</returns>
	/// <remarks>A folder is only deleted when it is empty, so one click cannot take a tree with it.</remarks>
	internal static string? Delete(RelativePath entry, string repoRoot)
	{
		string path = Path.Join(repoRoot, entry);

		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path);
			}
			else
			{
				File.Delete(path);
			}

			return null;
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			return failure.Message;
		}
	}
}
