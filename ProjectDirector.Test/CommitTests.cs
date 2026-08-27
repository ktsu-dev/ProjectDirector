// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.ObjectModel;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the two pieces of the Commit path that can be tested: what git reports as pending, and
/// how that list is described to the user before they agree to commit it.
/// </summary>
/// <remarks>
/// Commit stages everything, untracked files included, so the list shown in the prompt is the only
/// thing standing between the user and committing something they did not mean to. Both halves are
/// separated from the ImGui drawing around them for exactly that reason -- the drawing needs a live
/// context and a display, and these do not.
/// </remarks>
[TestClass]
public sealed class CommitTests
{
	private static string CreateCommittedRepository()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_commit_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		Assert.IsTrue(GitCli.Run("init", root).Succeeded, "git init failed.");

		// Scope identity to this throwaway repository so the test neither depends on nor disturbs
		// whatever global configuration the machine happens to carry.
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.name", "ProjectDirector").Succeeded);
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.email", "ProjectDirector@ktsu.dev").Succeeded);

		File.WriteAllText(Path.Join(root, "tracked.txt"), "original\n");
		Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded, "git add failed.");

		GitResult committed = GitCli.RunIn(root, "commit", "-m", "initial");
		Assert.IsTrue(committed.Succeeded, $"git commit failed: {committed.FailureText}");

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

	/// <summary>
	/// A clean tree has nothing pending, which is what makes the "nothing to commit" path reachable
	/// instead of opening an empty prompt.
	/// </summary>
	[TestMethod]
	public void ACleanWorkingTreeHasNoPendingChanges()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(0, changes.Count);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A modified tracked file is pending.
	/// </summary>
	[TestMethod]
	public void AModifiedTrackedFileIsPending()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root, "tracked.txt"), "modified\n");

			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(1, changes.Count);
			Assert.AreEqual("tracked.txt", changes[0]);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// An untracked file is pending too, because staging is <c>add --all</c>. This is the case the
	/// prompt's file list exists for: it is the only thing that tells the user a file they never
	/// added is about to be committed.
	/// </summary>
	[TestMethod]
	public void AnUntrackedFileIsPending()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root, "untracked.txt"), "new\n");

			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(1, changes.Count);
			Assert.AreEqual("untracked.txt", changes[0]);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A path containing a space survives intact, which is what -z buys: without it git quotes such
	/// a path and the quoting would be shown to the user as part of the name.
	/// </summary>
	[TestMethod]
	public void APathContainingASpaceIsReportedIntact()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root, "with space.txt"), "new\n");

			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(1, changes.Count);
			Assert.AreEqual("with space.txt", changes[0]);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A rename reports only its destination. git emits the source path as a bare following entry
	/// with no status prefix, and listing that too would show the user a file that no longer exists.
	/// </summary>
	[TestMethod]
	public void ARenameReportsOnlyItsDestination()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			Assert.IsTrue(GitCli.RunIn(root, "mv", "tracked.txt", "renamed.txt").Succeeded, "git mv failed.");

			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(1, changes.Count);
			Assert.AreEqual("renamed.txt", changes[0]);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A rename whose source path has a space in its third position is the case that a
	/// prefix-shaped test cannot tell apart from a real record. Consuming the entry is what gets
	/// this right; inspecting it cannot.
	/// </summary>
	[TestMethod]
	public void ARenameWhoseSourceLooksLikeARecordIsStillNotListed()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			// "ab cd.txt" has a space at index 2, exactly where a status record's separator sits.
			File.WriteAllText(Path.Join(root, "ab cd.txt"), "original\n");
			Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded);
			Assert.IsTrue(GitCli.RunIn(root, "commit", "-m", "second").Succeeded);

			Assert.IsTrue(GitCli.RunIn(root, "mv", "ab cd.txt", "renamed.txt").Succeeded, "git mv failed.");

			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(1, changes.Count);
			Assert.AreEqual("renamed.txt", changes[0]);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A path that is not a repository at all reports nothing rather than throwing, so the button
	/// degrades to "nothing to commit" instead of taking the application down.
	/// </summary>
	[TestMethod]
	public void APathThatIsNotARepositoryReportsNothing()
	{
		// Arrange
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_commit_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		try
		{
			// Act
			Collection<string> changes = GitCli.ListPendingChanges(root);

			// Assert
			Assert.AreEqual(0, changes.Count);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// The whole point of the button: an edit ends up in a commit.
	/// </summary>
	[TestMethod]
	public void StagingAndCommittingRecordsAModifiedFile()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root, "tracked.txt"), "modified\n");

			// Act
			GitCommitOutcome outcome = GitCli.StageAllAndCommit(root, "the message");

			// Assert
			Assert.IsTrue(outcome.Staged.Succeeded, $"git add failed: {outcome.Staged.FailureText}");
			Assert.IsNotNull(outcome.Committed);
			Assert.IsTrue(outcome.Committed.Succeeded, $"git commit failed: {outcome.Committed.FailureText}");

			Assert.IsFalse(GitCli.HasUncommittedChanges(root), "The tree is still dirty after committing.");
			Assert.AreEqual("the message", GitCli.RunIn(root, "log", "-1", "--format=%s").OutputText);
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// Staging is <c>add --all</c>, so an untracked file is committed too. This is the behaviour
	/// the prompt's file list warns about, and it has to actually be the behaviour.
	/// </summary>
	[TestMethod]
	public void StagingAndCommittingSweepsUpAnUntrackedFile()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root, "untracked.txt"), "new\n");

			// Act
			GitCommitOutcome outcome = GitCli.StageAllAndCommit(root, "sweep");

			// Assert
			Assert.IsNotNull(outcome.Committed);
			Assert.IsTrue(outcome.Committed.Succeeded, $"git commit failed: {outcome.Committed.FailureText}");

			Collection<string> tracked = GitCli.ListTrackedFiles(root);
			Assert.IsTrue(tracked.Contains("untracked.txt"), "The untracked file was not committed.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A clean tree is git's refusal to report, not this code's to invent. The commit result comes
	/// back non-null and failed so the log panel can show git's own wording, and HEAD must not move.
	/// </summary>
	[TestMethod]
	public void CommittingACleanTreeFailsWithoutMovingHead()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			string before = GitCli.RunIn(root, "rev-parse", "HEAD").OutputText;

			// Act
			GitCommitOutcome outcome = GitCli.StageAllAndCommit(root, "nothing here");

			// Assert
			Assert.IsTrue(outcome.Staged.Succeeded);
			Assert.IsNotNull(outcome.Committed, "A clean tree is a failed commit, not a failed stage.");
			Assert.IsFalse(outcome.Committed.Succeeded, "git accepted an empty commit.");
			Assert.AreEqual(before, GitCli.RunIn(root, "rev-parse", "HEAD").OutputText, "HEAD moved.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// When staging fails there is no commit result at all, because the commit was never attempted.
	/// That distinction is what stops a partial index being recorded as if it were the whole change.
	/// </summary>
	[TestMethod]
	public void AFailedStageSkipsTheCommitEntirely()
	{
		// Arrange
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_commit_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		try
		{
			// Act
			GitCommitOutcome outcome = GitCli.StageAllAndCommit(root, "never runs");

			// Assert
			Assert.IsFalse(outcome.Staged.Succeeded, "git add succeeded outside a repository.");
			Assert.IsNull(outcome.Committed, "The commit was attempted after staging failed.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// Pushing lands the commit in the remote. A bare repository on the local filesystem stands in
	/// for one, so this exercises real push negotiation with no network and no credentials.
	/// </summary>
	[TestMethod]
	public void PushingSendsTheCommitToTheRemote()
	{
		// Arrange
		string root = CreateCommittedRepository();
		string remote = Path.Join(Path.GetTempPath(), $"ktsu_pd_remote_{Guid.NewGuid():N}");

		try
		{
			Assert.IsTrue(GitCli.Run("init", "--bare", remote).Succeeded, "git init --bare failed.");
			Assert.IsTrue(GitCli.RunIn(root, "remote", "add", "origin", remote).Succeeded);

			string branch = GitCli.RunIn(root, "rev-parse", "--abbrev-ref", "HEAD").OutputText;
			GitResult upstream = GitCli.RunIn(root, "push", "--set-upstream", "origin", branch);
			Assert.IsTrue(upstream.Succeeded, $"establishing the upstream failed: {upstream.FailureText}");

			File.WriteAllText(Path.Join(root, "tracked.txt"), "modified\n");
			GitCommitOutcome outcome = GitCli.StageAllAndCommit(root, "to push");
			Assert.IsNotNull(outcome.Committed);
			Assert.IsTrue(outcome.Committed.Succeeded);

			string local = GitCli.RunIn(root, "rev-parse", "HEAD").OutputText;

			// Act
			GitResult pushed = GitCli.Push(root);

			// Assert
			Assert.IsTrue(pushed.Succeeded, $"git push failed: {pushed.FailureText}");
			Assert.AreEqual(local, GitCli.RunIn(remote, "rev-parse", branch).OutputText, "The remote did not receive the commit.");
		}
		finally
		{
			Cleanup(remote);
			Cleanup(root);
		}
	}

	/// <summary>
	/// A branch with no upstream is refused by git rather than guessed at here, and the refusal
	/// carries git's own explanation for the log panel instead of being swallowed.
	/// </summary>
	[TestMethod]
	public void PushingWithNoUpstreamFailsAndSaysWhy()
	{
		// Arrange
		string root = CreateCommittedRepository();

		try
		{
			// Act
			GitResult pushed = GitCli.Push(root);

			// Assert
			Assert.IsFalse(pushed.Succeeded, "git push succeeded with no remote configured.");
			Assert.AreNotEqual(0, pushed.FailureText.Length, "git said nothing about why the push failed.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A commit that ran is reported as a commit.
	/// </summary>
	[TestMethod]
	public void AnAttemptedCommitIsReportedAsACommit()
	{
		// Arrange
		GitResult staged = new(0, string.Empty, string.Empty);
		GitResult committed = new(0, "done", string.Empty);

		// Act
		(string description, GitResult result) = ProjectDirector.DescribeCommitOutcome("repo", new GitCommitOutcome(staged, committed));

		// Assert
		Assert.AreEqual("Committing repo", description);
		Assert.AreSame(committed, result);
	}

	/// <summary>
	/// A commit that never ran is reported as the staging failure it actually was. Labelling it as
	/// a commit would send the user looking at the wrong step.
	/// </summary>
	[TestMethod]
	public void ACommitThatNeverRanIsReportedAsStaging()
	{
		// Arrange
		GitResult staged = new(128, string.Empty, "not a git repository");

		// Act
		(string description, GitResult result) = ProjectDirector.DescribeCommitOutcome("repo", new GitCommitOutcome(staged, null));

		// Assert
		Assert.AreEqual("Staging repo", description);
		Assert.AreSame(staged, result);
	}

	/// <summary>
	/// A single change reads as one file, not "1 files".
	/// </summary>
	[TestMethod]
	public void OneChangeIsDescribedInTheSingular()
	{
		// Act
		string description = ProjectDirector.DescribePendingChanges(["only.txt"]);

		// Assert
		StringAssert.StartsWith(description, "1 file will be committed:", StringComparison.Ordinal);
		StringAssert.Contains(description, "only.txt", StringComparison.Ordinal);
	}

	/// <summary>
	/// Every path is listed while the list is short enough to show in full.
	/// </summary>
	[TestMethod]
	public void EveryPathIsListedWhileTheListIsShort()
	{
		// Arrange
		Collection<string> changes = ["a.txt", "b.txt", "c.txt"];

		// Act
		string description = ProjectDirector.DescribePendingChanges(changes);

		// Assert
		StringAssert.StartsWith(description, "3 files will be committed:", StringComparison.Ordinal);
		foreach (string change in changes)
		{
			StringAssert.Contains(description, change, StringComparison.Ordinal);
		}

		Assert.IsFalse(description.Contains("more", StringComparison.Ordinal));
	}

	/// <summary>
	/// Exactly as many paths as the cap allows are all shown, with no summary line -- the summary
	/// must not appear claiming that zero further files exist.
	/// </summary>
	[TestMethod]
	public void ExactlyTheCapIsListedInFull()
	{
		// Arrange
		Collection<string> changes = [];
		for (int i = 0; i < 20; ++i)
		{
			changes.Add($"file{i}.txt");
		}

		// Act
		string description = ProjectDirector.DescribePendingChanges(changes);

		// Assert
		StringAssert.Contains(description, "file19.txt", StringComparison.Ordinal);
		Assert.IsFalse(description.Contains("more", StringComparison.Ordinal));
	}

	/// <summary>
	/// Past the cap the remainder is summarised rather than listed, because a prompt taller than
	/// the display cannot be dismissed.
	/// </summary>
	[TestMethod]
	public void PastTheCapTheRemainderIsSummarised()
	{
		// Arrange
		Collection<string> changes = [];
		for (int i = 0; i < 25; ++i)
		{
			changes.Add($"file{i}.txt");
		}

		// Act
		string description = ProjectDirector.DescribePendingChanges(changes);

		// Assert
		StringAssert.StartsWith(description, "25 files will be committed:", StringComparison.Ordinal);
		StringAssert.Contains(description, "file19.txt", StringComparison.Ordinal);
		Assert.IsFalse(description.Contains("file20.txt", StringComparison.Ordinal));
		StringAssert.Contains(description, "... and 5 more", StringComparison.Ordinal);
	}
}
