// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.IO;

using DiffPlex.Model;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Semantics.Paths;

/// <summary>
/// Covers comparing a repository against its siblings, which moved off the render thread.
/// </summary>
/// <remarks>
/// The comparison is a pair of git subprocesses and a whole-file diff per sibling, so it runs on a
/// background task now. That makes two things worth pinning: which answer is on screen while a run
/// is in flight, and which run wins when several are. Both live on <see cref="GitRepository"/> as
/// plain methods so they can be driven without a live ImGui context, as the repository's other
/// decision logic already is.
/// </remarks>
[TestClass]
public sealed class SimilarReposTests
{
	private static GitHubRepository Repository(string localPath, string repoName) => new()
	{
		OwnerName = GitHubOwnerName.Create<GitHubOwnerName>("ktsu-dev"),
		RepoName = GitHubRepoName.Create<GitHubRepoName>(repoName),
		LocalPath = FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(localPath),
	};

	private static FullyQualifiedGitHubRepoName Name(string repoName) =>
		FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>($"ktsu-dev.{repoName}");

	private static Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> DiffsNaming(string repoName) =>
		new() { [Name(repoName)] = [] };

	[TestMethod]
	public void ARepositoryIsNotPendingBeforeAnythingIsAsked()
	{
		GitHubRepository repo = Repository("/tmp/a", "A");

		Assert.IsFalse(repo.SimilarReposPending);
		Assert.IsEmpty(repo.SimilarRepoDiffs);
	}

	[TestMethod]
	public void RequestingAComparisonClearsTheLastAnswerAndReportsPending()
	{
		GitHubRepository repo = Repository("/tmp/a", "A");
		Assert.IsTrue(repo.TryApplySimilarRepoDiffs(repo.RequestSimilarRepoDiffs(), DiffsNaming("B")));

		_ = repo.RequestSimilarRepoDiffs();

		// A comparison describes whichever repository was selected when it ran, so leaving the
		// previous one on screen while the next is computed would be actively misleading.
		Assert.IsEmpty(repo.SimilarRepoDiffs, "The superseded answer stays on screen.");
		Assert.IsTrue(repo.SimilarReposPending);
	}

	[TestMethod]
	public void ApplyingAComparisonPublishesItAndClearsPending()
	{
		GitHubRepository repo = Repository("/tmp/a", "A");

		int token = repo.RequestSimilarRepoDiffs();
		bool applied = repo.TryApplySimilarRepoDiffs(token, DiffsNaming("B"));

		Assert.IsTrue(applied);
		Assert.IsFalse(repo.SimilarReposPending);
		Assert.IsTrue(repo.SimilarRepoDiffs.ContainsKey(Name("B")));
	}

	[TestMethod]
	public void ASupersededComparisonCannotPublish()
	{
		GitHubRepository repo = Repository("/tmp/a", "A");

		int first = repo.RequestSimilarRepoDiffs();
		int second = repo.RequestSimilarRepoDiffs();

		// Clicking through several repositories leaves that many runs in flight, and they do not
		// finish in the order they started. The stale one must not land on the newer selection.
		Assert.IsFalse(repo.TryApplySimilarRepoDiffs(first, DiffsNaming("Stale")), "An older run published over a newer request.");
		Assert.IsEmpty(repo.SimilarRepoDiffs);
		Assert.IsTrue(repo.SimilarReposPending, "The newer request is still outstanding.");

		Assert.IsTrue(repo.TryApplySimilarRepoDiffs(second, DiffsNaming("Fresh")));
		Assert.IsTrue(repo.SimilarRepoDiffs.ContainsKey(Name("Fresh")));
	}

	[TestMethod]
	public void ALateComparisonCannotOverwriteAnAlreadyPublishedNewerOne()
	{
		GitHubRepository repo = Repository("/tmp/a", "A");

		int first = repo.RequestSimilarRepoDiffs();
		int second = repo.RequestSimilarRepoDiffs();
		Assert.IsTrue(repo.TryApplySimilarRepoDiffs(second, DiffsNaming("Fresh")));

		Assert.IsFalse(repo.TryApplySimilarRepoDiffs(first, DiffsNaming("Stale")));
		Assert.IsTrue(repo.SimilarRepoDiffs.ContainsKey(Name("Fresh")), "A late run replaced the answer the user is looking at.");
	}

	[TestMethod]
	public void ComparingAgainstSiblingsKeysEveryOneOfThemEvenWhenNothingIsShared()
	{
		string a = CreateRepository([("shared.txt", "one\n"), ("only-a.txt", "a\n")]);
		string b = CreateRepository([("shared.txt", "two\n")]);
		string c = CreateRepository([("unrelated.txt", "c\n")]);

		try
		{
			GitHubRepository repoA = Repository(a, "A");
			Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> diffs =
				ProjectDirector.DiffAgainstAll(
					repoA,
					[
						new(Name("B"), Repository(b, "B")),
						new(Name("C"), Repository(c, "C")),
					]);

			Assert.AreEqual(2, diffs.Count);

			// Only the tracked files both repositories carry are diffed.
			Assert.AreEqual(1, diffs[Name("B")].Count);
			Assert.IsTrue(diffs[Name("B")].ContainsKey(RelativeFilePath.Create<RelativeFilePath>("shared.txt")));
			Assert.IsNotEmpty(diffs[Name("B")][RelativeFilePath.Create<RelativeFilePath>("shared.txt")].DiffBlocks, "shared.txt differs between the two.");

			// A sibling sharing nothing still gets an entry, because the similar-repos table counts
			// its matches and would otherwise have no row to report zero on.
			Assert.IsEmpty(diffs[Name("C")]);
		}
		finally
		{
			TryDeleteDirectory(a);
			TryDeleteDirectory(b);
			TryDeleteDirectory(c);
		}
	}

	[TestMethod]
	public void IdenticalSharedFilesDiffToNoBlocks()
	{
		string a = CreateRepository([("shared.txt", "same\n")]);
		string b = CreateRepository([("shared.txt", "same\n")]);

		try
		{
			Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> diffs =
				ProjectDirector.DiffAgainstAll(Repository(a, "A"), [new(Name("B"), Repository(b, "B"))]);

			// This is what the "Exact" column counts.
			Assert.IsEmpty(diffs[Name("B")][RelativeFilePath.Create<RelativeFilePath>("shared.txt")].DiffBlocks);
		}
		finally
		{
			TryDeleteDirectory(a);
			TryDeleteDirectory(b);
		}
	}

	[TestMethod]
	public void ARepositoryThatIsNotCheckedOutComparesToNothingRatherThanThrowing()
	{
		string b = CreateRepository([("shared.txt", "one\n")]);
		string missing = Path.Combine(Path.GetTempPath(), $"ktsu_pd_absent_{Guid.NewGuid():N}");

		try
		{
			Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> diffs =
				ProjectDirector.DiffAgainstAll(Repository(missing, "Missing"), [new(Name("B"), Repository(b, "B"))]);

			// Repositories are listed before they are cloned, so this is an ordinary state rather
			// than an error — and it now runs on a background task, where a throw goes unobserved.
			Assert.IsEmpty(diffs[Name("B")]);
		}
		finally
		{
			TryDeleteDirectory(b);
		}
	}

	[TestMethod]
	public void ComparingAgainstNoSiblingsProducesNoEntries()
	{
		string a = CreateRepository([("shared.txt", "one\n")]);

		try
		{
			Assert.IsEmpty(ProjectDirector.DiffAgainstAll(Repository(a, "A"), []));
		}
		finally
		{
			TryDeleteDirectory(a);
		}
	}

	private static string CreateRepository(IEnumerable<(string RelativePath, string Contents)> files)
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_pd_similar_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		Assert.IsTrue(GitCli.Run("init", root).Succeeded, "git init failed.");

		// Scope identity to this throwaway repository so the test neither depends on nor disturbs
		// whatever global configuration the machine happens to carry.
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.name", "ProjectDirector").Succeeded);
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.email", "ProjectDirector@ktsu.dev").Succeeded);

		foreach ((string relativePath, string contents) in files)
		{
			File.WriteAllText(Path.Combine(root, relativePath), contents);
		}

		Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded, "git add failed.");

		GitResult committed = GitCli.RunIn(root, "commit", "-m", "Add files");
		Assert.IsTrue(committed.Succeeded, $"git commit failed: {committed.FailureText}");

		return root;
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			// Git marks objects read-only, which blocks a plain recursive delete on Windows.
			foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Covers a missing directory too. A best-effort cleanup of a temp directory is not
			// worth failing a test over.
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
