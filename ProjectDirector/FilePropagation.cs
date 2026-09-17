// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.ObjectModel;

/// <summary>
/// What copying the propagated file into one repository did.
/// </summary>
/// <param name="Repo">The repository the file was copied into.</param>
/// <param name="Destination">The full path the file was copied to.</param>
/// <param name="Failure">Why the copy failed, or null when it succeeded.</param>
internal sealed record FilePropagationResult(FullyQualifiedGitHubRepoName Repo, string Destination, string? Failure)
{
	/// <summary>
	/// Gets a value indicating whether the file reached this repository.
	/// </summary>
	internal bool Succeeded => Failure is null;
}

/// <summary>
/// The outcome of one propagation run, one entry per repository the user asked for.
/// </summary>
/// <param name="Source">The file that was propagated.</param>
/// <param name="SourceExists">Whether <paramref name="Source"/> was there to copy. When false no repository was touched.</param>
/// <param name="Results">One result per requested repository, in the order they were attempted.</param>
internal sealed record FilePropagationReport(string Source, bool SourceExists, Collection<FilePropagationResult> Results)
{
	/// <summary>
	/// Describes the run for the log panel: a summary line, then one line per failure.
	/// </summary>
	/// <returns>At least one line, the first of which answers "did that work" on its own.</returns>
	/// <remarks>
	/// The summary line carries the counts and names the repositories that missed out, because after
	/// a partial run the question is which repositories have the file -- and reconstructing that from
	/// fifteen individual lines is the work this is meant to save.
	/// </remarks>
	internal Collection<string> Summarize()
	{
		if (!SourceExists)
		{
			return [$"Propagating {Source} failed: the source file does not exist"];
		}

		Collection<FilePropagationResult> failures = [.. Results.Where(result => !result.Succeeded)];
		int succeeded = Results.Count - failures.Count;
		string failed = failures.Count > 0
			? $"; failed: {string.Join(", ", failures.Select(failure => failure.Repo.WeakString))}"
			: string.Empty;

		Collection<string> lines = [$"Propagated {Source} to {succeeded} of {Results.Count} repos{failed}"];
		foreach (FilePropagationResult failure in failures)
		{
			lines.Add($"    {failure.Repo.WeakString}: {failure.Failure}");
		}

		return lines;
	}
}

/// <summary>
/// Copies one file into a set of repositories.
/// </summary>
/// <remarks>
/// Separate from <see cref="PopupPropagateFile"/> so the part with a rule in it can be driven
/// without a live ImGui context, the way <see cref="ProjectDirector.DecidePull"/> is.
///
/// The rule is that a batch the user confirmed runs to the end. A locked destination in the seventh
/// of fifteen repositories used to end the loop there, so the remaining eight silently never got the
/// file -- and because every other long-running action in this application reports through the log
/// panel, that silence read as success.
/// </remarks>
internal static class FilePropagation
{
	/// <summary>
	/// Copies <paramref name="source"/> to every destination, continuing past a failure.
	/// </summary>
	/// <param name="source">The file to copy.</param>
	/// <param name="destinations">The repository each copy is for, and the full path to copy it to.</param>
	/// <returns>A report naming what happened to every requested repository.</returns>
	/// <remarks>
	/// A missing source is checked once, before anything is copied. It is a different failure from a
	/// locked destination -- one the user can only have caused by asking for the wrong file -- and
	/// reporting it as one failure per repository would bury that.
	/// </remarks>
	internal static FilePropagationReport Propagate(string source, IEnumerable<KeyValuePair<FullyQualifiedGitHubRepoName, string>> destinations)
	{
		Ensure.NotNull(destinations);

		Collection<FilePropagationResult> results = [];

		if (!File.Exists(source))
		{
			return new(source, SourceExists: false, results);
		}

		foreach ((FullyQualifiedGitHubRepoName repo, string destination) in destinations)
		{
			results.Add(Copy(source, repo, destination));
		}

		return new(source, SourceExists: true, results);
	}

	private static FilePropagationResult Copy(string source, FullyQualifiedGitHubRepoName repo, string destination)
	{
		string? directory = Path.GetDirectoryName(destination);
		if (string.IsNullOrEmpty(directory))
		{
			return new(repo, destination, "the destination has no containing directory");
		}

		try
		{
			_ = Directory.CreateDirectory(directory);
			File.Copy(source, destination, overwrite: true);
			return new(repo, destination, null);
		}
		catch (IOException ex)
		{
			return new(repo, destination, ex.Message);
		}
		catch (UnauthorizedAccessException ex)
		{
			return new(repo, destination, ex.Message);
		}
		catch (NotSupportedException ex)
		{
			return new(repo, destination, ex.Message);
		}
		catch (ArgumentException ex)
		{
			return new(repo, destination, ex.Message);
		}
	}
}
