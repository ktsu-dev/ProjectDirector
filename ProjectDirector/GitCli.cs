// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.ObjectModel;
using System.Text;

using ktsu.RunCommand;

/// <summary>
/// The result of running a git command: its exit code plus whatever it wrote to each stream.
/// </summary>
/// <param name="ExitCode">The process exit code, where zero means success.</param>
/// <param name="Output">The raw standard output.</param>
/// <param name="Error">The raw standard error.</param>
internal sealed record GitResult(int ExitCode, string Output, string Error)
{
	/// <summary>
	/// Gets a value indicating whether git reported success.
	/// </summary>
	internal bool Succeeded => ExitCode == 0;

	/// <summary>
	/// Gets the standard output trimmed, which is what single-value queries want.
	/// </summary>
	internal string OutputText => Output.Trim();

	/// <summary>
	/// Gets whichever stream explains a failure, preferring standard error.
	/// </summary>
	internal string FailureText => Error.Trim().Length > 0 ? Error.Trim() : Output.Trim();

	/// <summary>
	/// Gets both streams as trimmed, non-empty lines, which is what the log panel displays.
	/// Transfer commands report their whole progress narrative on standard error.
	/// </summary>
	internal Collection<string> AllLines
	{
		get
		{
			Collection<string> lines = [];
			foreach (string stream in (string[])[Output, Error])
			{
				if (string.IsNullOrEmpty(stream))
				{
					continue;
				}

				foreach (string line in stream.Split('\n'))
				{
					string trimmed = line.Trim();
					if (trimmed.Length > 0)
					{
						lines.Add(trimmed);
					}
				}
			}

			return lines;
		}
	}
}

/// <summary>
/// The outcome of staging and committing: what each step reported.
/// </summary>
/// <param name="Staged">The result of <c>git add --all</c>.</param>
/// <param name="Committed">
/// The result of <c>git commit</c>, or <see langword="null"/> when staging failed and the commit
/// was therefore never attempted.
/// </param>
internal sealed record GitCommitOutcome(GitResult Staged, GitResult? Committed);

/// <summary>
/// Runs the git command line.
/// </summary>
/// <remarks>
/// Shelling out to git rather than binding libgit2 is what makes Git LFS work. The clean filter that
/// turns a tracked binary into a pointer, the smudge filter that turns it back on checkout, and the
/// hooks that transfer the objects those pointers refer to are all features of the git command. A
/// library reading and writing the object database directly silently bypasses them, so a clone lands
/// pointer files where the real content should be.
/// </remarks>
internal static class GitCli
{
	/// <summary>
	/// Runs git with the given arguments, each passed separately so paths need no quoting.
	/// </summary>
	/// <param name="arguments">The arguments to pass to git.</param>
	/// <returns>The exit code and captured output.</returns>
	internal static GitResult Run(params string[] arguments)
	{
		Ensure.NotNull(arguments);

		StringBuilder output = new();
		StringBuilder error = new();

		// The raw handler is deliberate: the line-splitting handler drops a trailing fragment that
		// was never newline terminated, and git does not always terminate its final line.
		OutputHandler handler = new(
			onStandardOutput: data => output.Append(data),
			onStandardError: data => error.Append(data));

		int exitCode = RunCommand.Execute("git", arguments, handler);

		return new GitResult(exitCode, output.ToString(), error.ToString());
	}

	/// <summary>
	/// Runs git against a specific repository using <c>-C</c>, which leaves the process working
	/// directory untouched and so stays safe when repositories are fetched concurrently.
	/// </summary>
	/// <param name="repositoryPath">The working tree to operate on.</param>
	/// <param name="arguments">The arguments to pass to git.</param>
	/// <returns>The exit code and captured output.</returns>
	internal static GitResult RunIn(string repositoryPath, params string[] arguments)
	{
		Ensure.NotNull(repositoryPath);
		Ensure.NotNull(arguments);

		return Run(["-C", repositoryPath, .. arguments]);
	}

	/// <summary>
	/// Determines whether the given directory is inside a git working tree.
	/// </summary>
	/// <param name="path">The directory to test.</param>
	/// <returns><see langword="true"/> if the path is inside a working tree.</returns>
	internal static bool IsRepository(string path) =>
		!string.IsNullOrEmpty(path)
		&& Directory.Exists(path)
		&& RunIn(path, "rev-parse", "--is-inside-work-tree").Succeeded;

	/// <summary>
	/// Gets the URL configured for a remote, or an empty string when the remote does not exist.
	/// </summary>
	/// <param name="repositoryPath">The working tree to query.</param>
	/// <param name="remoteName">The remote to look up.</param>
	/// <returns>The remote URL, or an empty string.</returns>
	internal static string GetRemoteUrl(string repositoryPath, string remoteName)
	{
		GitResult result = RunIn(repositoryPath, "remote", "get-url", remoteName);

		return result.Succeeded ? result.OutputText : string.Empty;
	}

	/// <summary>
	/// Lists the repository-relative paths of every tracked file, using forward slashes as git
	/// reports them.
	/// </summary>
	/// <param name="repositoryPath">The working tree to query.</param>
	/// <returns>The tracked paths, or an empty collection when the path is not a repository.</returns>
	internal static Collection<string> ListTrackedFiles(string repositoryPath)
	{
		// -z separates entries with NUL and turns off the quoting git otherwise applies to paths
		// holding unusual characters, so the names arrive exactly as recorded.
		GitResult result = RunIn(repositoryPath, "ls-files", "-z");

		Collection<string> files = [];
		if (!result.Succeeded)
		{
			return files;
		}

		foreach (string entry in result.Output.Split('\0'))
		{
			if (entry.Length > 0)
			{
				files.Add(entry);
			}
		}

		return files;
	}

	/// <summary>
	/// Lists the repository-relative paths of every file that <c>git commit</c> would include
	/// after <c>git add --all</c>, tracked or otherwise.
	/// </summary>
	/// <param name="repositoryPath">The working tree to query.</param>
	/// <returns>The pending paths, or an empty collection when the path is not a repository.</returns>
	/// <remarks>
	/// Asks the same <c>status --porcelain</c> that <see cref="HasUncommittedChanges"/> does, but
	/// with -z so entries are NUL-separated and git applies none of the quoting it otherwise uses
	/// for paths holding unusual characters -- the names arrive exactly as recorded.
	///
	/// Each record is two status characters, a space, then the path. A rename or copy additionally
	/// emits its <em>source</em> path as a bare following entry with no status prefix, which is why
	/// the loop consumes that entry rather than testing every entry for a prefix: a source path
	/// such as <c>ab cd.txt</c> has a space in the third position and so is indistinguishable from
	/// a record by inspection alone. Reporting it would list a file that no longer exists.
	/// </remarks>
	internal static Collection<string> ListPendingChanges(string repositoryPath)
	{
		GitResult result = RunIn(repositoryPath, "status", "--porcelain", "-z");

		Collection<string> changes = [];
		if (!result.Succeeded)
		{
			return changes;
		}

		string[] entries = result.Output.Split('\0');
		for (int i = 0; i < entries.Length; ++i)
		{
			string entry = entries[i];

			// A record needs a status pair, its separator, and at least one character of path.
			if (entry.Length < 4 || entry[2] != ' ')
			{
				continue;
			}

			changes.Add(entry[3..]);

			// A rename or copy is recorded against the index, in the first status character.
			if (entry[0] is 'R' or 'C')
			{
				++i;
			}
		}

		return changes;
	}

	/// <summary>
	/// Stages every change, tracked or otherwise, then commits them.
	/// </summary>
	/// <param name="repositoryPath">The working tree to commit.</param>
	/// <param name="message">The commit message.</param>
	/// <returns>What each step reported, with a null commit result when staging failed.</returns>
	/// <remarks>
	/// The commit is skipped when staging fails, rather than committing whatever happened to make
	/// it into the index: recording a subset of what the user agreed to, without saying so, is
	/// worse than recording nothing. A clean tree is not a failure of this method -- git itself
	/// refuses with a non-zero exit, and that refusal is returned as the commit result so the
	/// caller can report git's own wording.
	/// </remarks>
	internal static GitCommitOutcome StageAllAndCommit(string repositoryPath, string message)
	{
		GitResult staged = RunIn(repositoryPath, "add", "--all");

		return staged.Succeeded
			? new GitCommitOutcome(staged, RunIn(repositoryPath, "commit", "-m", message))
			: new GitCommitOutcome(staged, null);
	}

	/// <summary>
	/// Pushes the current branch to its configured upstream.
	/// </summary>
	/// <param name="repositoryPath">The working tree to push from.</param>
	/// <returns>The exit code and captured output.</returns>
	/// <remarks>
	/// No refspec and no force, so a branch with no upstream is refused by git rather than guessed
	/// at here, and a diverged branch is refused rather than overwritten. Both refusals come back
	/// as a non-zero exit carrying git's own explanation, which is what the log panel shows.
	/// </remarks>
	internal static GitResult Push(string repositoryPath) => RunIn(repositoryPath, "push");

	/// <summary>
	/// Determines whether the working tree has any uncommitted change, tracked or otherwise.
	/// </summary>
	/// <param name="repositoryPath">The working tree to query.</param>
	/// <returns><see langword="true"/> if anything differs from HEAD.</returns>
	internal static bool HasUncommittedChanges(string repositoryPath)
	{
		GitResult result = RunIn(repositoryPath, "status", "--porcelain");

		// Standard output alone. git reports line-ending conversion as a warning on standard
		// error, and treating one of those as a change would mark every clean repository dirty.
		return result.Succeeded && result.Output.Trim().Length > 0;
	}
}
