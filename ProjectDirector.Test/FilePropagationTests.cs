// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

using ktsu.Semantics.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests what propagating one file to several repositories does when one of them refuses the copy.
/// </summary>
/// <remarks>
/// Propagating is a batch the user confirms once, for a list of repositories they picked. It used to
/// run an unguarded <see cref="File.Copy(string, string, bool)"/> per repository, so the first locked
/// destination ended the loop -- every repository after it silently never got the file, and nothing
/// was written to the log panel either way.
///
/// <see cref="FilePropagation"/> exists apart from <see cref="PopupPropagateFile"/> so that rule can
/// be driven against real throwaway directories, the way <see cref="PullDecisionTests"/> drives
/// <see cref="ProjectDirector.DecidePull"/>. A destination that is an existing *directory* is the
/// portable way to make a copy fail: both Windows and Linux refuse it, without needing a lock or a
/// permission change the test would then have to undo.
/// </remarks>
[TestClass]
public sealed class FilePropagationTests
{
	private const string SourceContent = "root = true\n";

	private static FullyQualifiedGitHubRepoName Repo(string name) => name.As<FullyQualifiedGitHubRepoName>();

	private static string CreateWorkspace()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_propagate_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);
		return root;
	}

	private static void Cleanup(string root)
	{
		try
		{
			Directory.Delete(root, recursive: true);
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing an otherwise passing test over.
		}
		catch (UnauthorizedAccessException)
		{
			// Same.
		}
	}

	[TestMethod]
	public void ARefusedCopyDoesNotStopTheRepositoriesAfterIt()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			string first = Path.Join(root, "first", ".editorconfig");
			string blocked = Path.Join(root, "blocked", ".editorconfig");
			string last = Path.Join(root, "last", ".editorconfig");

			// Occupy the middle destination with a directory of the same name, which neither platform
			// will let File.Copy overwrite.
			_ = Directory.CreateDirectory(blocked);

			// An ordered sequence rather than a dictionary, so "after the failure" means what it says.
			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/first"), first),
				new(Repo("ktsu-dev/blocked"), blocked),
				new(Repo("ktsu-dev/last"), last),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);

			Assert.IsTrue(File.Exists(first), "The repository before the failure should have the file.");
			Assert.AreEqual(SourceContent, File.ReadAllText(first));
			Assert.IsTrue(File.Exists(last), "The repository after the failure should still have been attempted.");
			Assert.AreEqual(SourceContent, File.ReadAllText(last));

			Assert.HasCount(3, report.Results, "Every requested repository should be accounted for.");
			Assert.IsTrue(report.Results[0].Succeeded);
			Assert.IsFalse(report.Results[1].Succeeded, "The occupied destination should be reported as a failure.");
			Assert.IsFalse(string.IsNullOrWhiteSpace(report.Results[1].Failure), "A failure should carry its reason.");
			Assert.IsTrue(report.Results[2].Succeeded);
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void TheSummaryCountsTheRunAndNamesTheRepositoriesThatMissedOut()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			string blocked = Path.Join(root, "blocked", ".editorconfig");
			_ = Directory.CreateDirectory(blocked);

			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/first"), Path.Join(root, "first", ".editorconfig")),
				new(Repo("ktsu-dev/blocked"), blocked),
				new(Repo("ktsu-dev/last"), Path.Join(root, "last", ".editorconfig")),
			];

			Collection<string> lines = FilePropagation.Propagate(source, destinations).Summarize();

			Assert.Contains("2 of 3", lines[0], StringComparison.Ordinal);
			Assert.Contains("ktsu-dev/blocked", lines[0], StringComparison.Ordinal);
			Assert.HasCount(2, lines, "One summary line, then one detail line for the single failure.");
			Assert.Contains("ktsu-dev/blocked", lines[1], StringComparison.Ordinal);
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ARunWhereEveryCopyWorksSaysSoInOneLine()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/first"), Path.Join(root, "first", ".editorconfig")),
				new(Repo("ktsu-dev/last"), Path.Join(root, "last", "nested", ".editorconfig")),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);
			Collection<string> lines = report.Summarize();

			Assert.IsTrue(report.SourceExists);
			Assert.HasCount(1, lines);
			Assert.Contains("2 of 2", lines[0], StringComparison.Ordinal);
			Assert.IsFalse(lines[0].Contains("failed", StringComparison.Ordinal));
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ADestinationWhoseParentIsAFileIsReportedRatherThanThrown()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			// A plain file where the destination expects a directory: creating the containing
			// directory fails rather than the copy itself, which is the other half of the guard.
			string fileInTheWay = Path.Join(root, "blocked");
			File.WriteAllText(fileInTheWay, "not a directory\n");

			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/blocked"), Path.Join(fileInTheWay, "nested", ".editorconfig")),
				new(Repo("ktsu-dev/last"), Path.Join(root, "last", ".editorconfig")),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);

			Assert.IsFalse(report.Results[0].Succeeded);
			Assert.IsFalse(string.IsNullOrWhiteSpace(report.Results[0].Failure));
			Assert.IsTrue(report.Results[1].Succeeded, "The repository after the failure should still have been attempted.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ADestinationWithNoContainingDirectoryIsReportedRatherThanSkipped()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			// A bare filename has no directory part. The old code silently skipped this case; a
			// repository the user checked and heard nothing about is the bug, not the edge case.
			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/bare"), "nocontainingdirectory.txt"),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);

			Assert.HasCount(1, report.Results, "The repository should still be accounted for.");
			Assert.IsFalse(report.Results[0].Succeeded);
			Assert.Contains("containing directory", report.Results[0].Failure!, StringComparison.Ordinal);
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ADestinationThatIsNotAUsablePathIsReportedRatherThanThrown()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			_ = Directory.CreateDirectory(Path.GetDirectoryName(source)!);
			File.WriteAllText(source, SourceContent);

			// An embedded null is rejected by the path APIs on every platform, which is the
			// ArgumentException arm of the guard.
			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/invalid"), Path.Join(root, "in\0valid", ".editorconfig")),
				new(Repo("ktsu-dev/last"), Path.Join(root, "last", ".editorconfig")),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);

			Assert.IsFalse(report.Results[0].Succeeded);
			Assert.IsTrue(report.Results[1].Succeeded, "The repository after the failure should still have been attempted.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void OnlyTheCheckedRepositoriesGetADestination()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[Repo("ktsu-dev/first")] = new GitHubRepository { LocalPath = Path.Join("dev", "first").As<FullyQualifiedLocalRepoPath>() },
			[Repo("ktsu-dev/second")] = new GitHubRepository { LocalPath = Path.Join("dev", "second").As<FullyQualifiedLocalRepoPath>() },
			[Repo("ktsu-dev/third")] = new GitHubRepository { LocalPath = Path.Join("dev", "third").As<FullyQualifiedLocalRepoPath>() },
		};

		KeyValuePair<FullyQualifiedGitHubRepoName, bool>[] selection =
		[
			new(Repo("ktsu-dev/first"), true),
			new(Repo("ktsu-dev/second"), false),
			new(Repo("ktsu-dev/third"), true),
		];

		Dictionary<FullyQualifiedGitHubRepoName, string> destinations =
			FilePropagation.ResolveDestinations(selection, repos, Path.Join("src", ".editorconfig"));

		Assert.HasCount(2, destinations, "An unchecked repository should not get a destination.");
		Assert.IsFalse(destinations.ContainsKey(Repo("ktsu-dev/second")));
		Assert.AreEqual(Path.Join("dev", "first", "src", ".editorconfig"), destinations[Repo("ktsu-dev/first")]);
		Assert.AreEqual(Path.Join("dev", "third", "src", ".editorconfig"), destinations[Repo("ktsu-dev/third")]);
	}

	[TestMethod]
	public void OnlyTheSummaryLineCarriesTheTimestamp()
	{
		DateTimeOffset at = new(2026, 9, 17, 11, 30, 0, TimeSpan.Zero);
		FilePropagationReport report = new(
			".editorconfig",
			SourceExists: true,
			[
				new(Repo("ktsu-dev/first"), "first", null),
				new(Repo("ktsu-dev/blocked"), "blocked", "denied"),
			]);

		Collection<string> lines = FilePropagation.DescribeForLog(report, at);

		Assert.HasCount(2, lines);
		Assert.StartsWith($"[{at}] ", lines[0], StringComparison.Ordinal);
		Assert.Contains("1 of 2", lines[0], StringComparison.Ordinal);
		Assert.StartsWith("    ", lines[1], StringComparison.Ordinal);
		Assert.IsFalse(lines[1].Contains($"[{at}]", StringComparison.Ordinal), "Detail lines are indented under the summary, not stamped again.");
	}

	[TestMethod]
	public void AMissingSourceIsReportedOnceAndLeavesEveryRepositoryAlone()
	{
		string root = CreateWorkspace();
		try
		{
			string source = Path.Join(root, "source", ".editorconfig");
			string destination = Path.Join(root, "first", ".editorconfig");

			KeyValuePair<FullyQualifiedGitHubRepoName, string>[] destinations =
			[
				new(Repo("ktsu-dev/first"), destination),
			];

			FilePropagationReport report = FilePropagation.Propagate(source, destinations);
			Collection<string> lines = report.Summarize();

			Assert.IsFalse(report.SourceExists);
			Assert.IsEmpty(report.Results, "No repository should be touched when there is nothing to copy.");
			Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(destination)!), "A failed run should not create destination directories.");
			Assert.HasCount(1, lines, "A missing source is one failure, not one per repository.");
			Assert.Contains("does not exist", lines[0], StringComparison.Ordinal);
		}
		finally
		{
			Cleanup(root);
		}
	}
}
