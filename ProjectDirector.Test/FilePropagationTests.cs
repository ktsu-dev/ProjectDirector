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

			Assert.AreEqual(3, report.Results.Count, "Every requested repository should be accounted for.");
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

			StringAssert.Contains(lines[0], "2 of 3", StringComparison.Ordinal);
			StringAssert.Contains(lines[0], "ktsu-dev/blocked", StringComparison.Ordinal);
			Assert.AreEqual(2, lines.Count, "One summary line, then one detail line for the single failure.");
			StringAssert.Contains(lines[1], "ktsu-dev/blocked", StringComparison.Ordinal);
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
			Assert.AreEqual(1, lines.Count);
			StringAssert.Contains(lines[0], "2 of 2", StringComparison.Ordinal);
			Assert.IsFalse(lines[0].Contains("failed", StringComparison.Ordinal));
		}
		finally
		{
			Cleanup(root);
		}
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
			Assert.AreEqual(0, report.Results.Count, "No repository should be touched when there is nothing to copy.");
			Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(destination)!), "A failed run should not create destination directories.");
			Assert.AreEqual(1, lines.Count, "A missing source is one failure, not one per repository.");
			StringAssert.Contains(lines[0], "does not exist", StringComparison.Ordinal);
		}
		finally
		{
			Cleanup(root);
		}
	}
}
