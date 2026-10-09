// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.UITests.Gallery;

using System.Diagnostics;

using ktsu.Semantics.Paths;

/// <summary>
/// Seeds the three sibling repositories the gallery photographs, and the settings that point the
/// application at them.
/// </summary>
/// <remarks>
/// <para>
/// The repositories are real git repositories, made offline with fixed names, dates and contents,
/// because the comparison the application draws runs <c>git</c> against them. Nothing here touches
/// a remote: the settings carry each repository's GitHub address only as text to show, and every
/// fetch interval is zero, which is how the application spells "never fetch on a timer".
/// </para>
/// <para>
/// They live at one fixed path rather than a fresh temporary one, because the application prints
/// the dev directory and the selected repository's local path, and a path that changed per run
/// would change every picture with it.
/// </para>
/// </remarks>
internal static class GalleryRepositories
{
	/// <summary>The GitHub owner the repositories are filed under.</summary>
	internal const string Owner = "ktsu-dev";

	/// <summary>The repository the gallery selects.</summary>
	internal const string Alpha = "Alpha";

	/// <summary>The sibling sharing the most files with <see cref="Alpha"/>.</summary>
	internal const string Beta = "Beta";

	/// <summary>A sibling sharing fewer files with <see cref="Alpha"/>.</summary>
	internal const string Gamma = "Gamma";

	/// <summary>The file the diff pictures compare, which differs in several places.</summary>
	internal const string WidgetSource = "src/Widget.cs";

	/// <summary>
	/// The repositories in the order the settings list them. Order matters: the similar repos table
	/// breaks ties by the order the settings hold the siblings in.
	/// </summary>
	private static readonly string[] Names = [Alpha, Beta, Gamma];

	/// <summary>Gets the directory every repository is created under, which the application shows as its dev directory.</summary>
	internal static string Root => OperatingSystem.IsWindows()
		? Path.Combine(Path.GetTempPath(), "projectdirector-gallery")
		: "/tmp/projectdirector-gallery";

	/// <summary>Gets the name the application keys a repository by.</summary>
	/// <param name="repo">The repository's own name.</param>
	/// <returns>The owner-qualified name.</returns>
	internal static FullyQualifiedGitHubRepoName FullName(string repo) =>
		FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>($"{Owner}.{repo}");

	/// <summary>Creates the repositories, replacing any left by an earlier run.</summary>
	internal static void Seed()
	{
		Delete();

		Write(Alpha, "README.md", """
			# Alpha

			A small library in the ktsu-dev family.

			## Usage

			Install the package and call `Widget.Build()`.

			## License

			MIT
			""");
		Write(Alpha, ".editorconfig", EditorConfig);
		Write(Alpha, "LICENSE.md", License);
		Write(Alpha, WidgetSource, """
			namespace Alpha;

			public static class Widget
			{
				public static string Build() => "alpha";

				public static int Size => 3;
			}
			""");

		Write(Beta, "README.md", """
			# Beta

			A small library in the ktsu-dev family.

			## Usage

			Install the package and call `Widget.Build()`.

			## License

			MIT
			""");
		Write(Beta, ".editorconfig", EditorConfig);
		Write(Beta, "LICENSE.md", License);
		Write(Beta, WidgetSource, """
			namespace Beta;

			public static class Widget
			{
				public static string Build() => "beta";

				public static int Size => 4;

				public static bool IsReady => true;
			}
			""");

		Write(Gamma, "README.md", """
			# Gamma

			A command line tool in the ktsu-dev family.

			## Usage

			Run `gamma --help` for the list of commands.

			## License

			MIT
			""");
		Write(Gamma, ".editorconfig", EditorConfig);
		Write(Gamma, "LICENSE.md", License);
		Write(Gamma, "docs/commands.md", """
			# Commands

			- `gamma build`
			- `gamma check`
			""");

		foreach (string path in Names.Select(PathOf))
		{
			Git(path, "init", "--quiet", "--initial-branch=main");
			Git(path, "add", "--all");
			Git(path, "commit", "--quiet", "--no-gpg-sign", "--message", "Initial commit");
		}
	}

	/// <summary>Removes the repositories.</summary>
	internal static void Delete()
	{
		if (Directory.Exists(Root))
		{
			Directory.Delete(Root, recursive: true);
		}
	}

	/// <summary>
	/// Builds the settings the application starts from: one owner, its three repositories, all
	/// recorded as cloned, and nothing selected.
	/// </summary>
	/// <returns>Fresh settings, which the application is free to modify.</returns>
	internal static ProjectDirectorOptions CreateOptions()
	{
		GitHubOwnerName owner = GitHubOwnerName.Create<GitHubOwnerName>(Owner);
		ProjectDirectorOptions options = new()
		{
			DevDirectory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Root),
		};

		_ = options.GitHubOwners.Add(owner);
		foreach (string name in Names)
		{
			GitHubRepository repo = new()
			{
				OwnerName = owner,
				RepoName = GitHubRepoName.Create<GitHubRepoName>(name),
				LocalPath = FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(PathOf(name)),
				RemotePath = GitRemotePath.Create<GitRemotePath>($"https://github.com/{Owner}/{name}"),
				MinFetchIntervalSeconds = 0,
			};

			options.Repos[FullName(name)] = repo;
			options.ClonedRepos[repo.LocalPath] = FullName(name);
		}

		return options;
	}

	private const string EditorConfig = """
		root = true

		[*]
		indent_style = tab
		end_of_line = lf
		""";

	private const string License = """
		MIT License

		Copyright (c) ktsu-dev contributors
		""";

	private static string PathOf(string repo) => Path.Combine(Root, Owner, repo);

	private static void Write(string repo, string relativePath, string content)
	{
		string path = Path.Combine(PathOf(repo), relativePath);
		_ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content.ReplaceLineEndings("\n") + "\n");
	}

	/// <summary>
	/// Runs git with every input that could vary between machines pinned: no system or user
	/// configuration, and a fixed author, committer and date.
	/// </summary>
	private static void Git(string workingDirectory, params string[] arguments)
	{
		ProcessStartInfo start = new("git")
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		foreach (string argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}

		start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
		start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
		start.Environment["GIT_AUTHOR_NAME"] = "ktsu-dev gallery";
		start.Environment["GIT_AUTHOR_EMAIL"] = "gallery@ktsu.dev";
		start.Environment["GIT_AUTHOR_DATE"] = "2026-01-01T00:00:00Z";
		start.Environment["GIT_COMMITTER_NAME"] = "ktsu-dev gallery";
		start.Environment["GIT_COMMITTER_EMAIL"] = "gallery@ktsu.dev";
		start.Environment["GIT_COMMITTER_DATE"] = "2026-01-01T00:00:00Z";

		using Process git = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
		string error = git.StandardError.ReadToEnd();
		_ = git.StandardOutput.ReadToEnd();
		git.WaitForExit();
		if (git.ExitCode != 0)
		{
			throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed in {workingDirectory}: {error}");
		}
	}
}
