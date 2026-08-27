// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.IO;

using ktsu.Semantics.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the rule that decides whether pulling a repository interrupts the user first.
/// </summary>
/// <remarks>
/// The Pull button used to do nothing at all. Now it either pulls or asks, and which one it does
/// is the only part of that path with a rule in it -- everything around it draws ImGui and needs a
/// live context and a display. <see cref="ProjectDirector.DecidePull"/> exists separately so this
/// rule can be driven against real throwaway repositories, the way <see cref="GitCliTests"/> does.
///
/// Getting it wrong in either direction is user-visible: nagging on a clean tree makes the button
/// annoying, and staying silent on a dirty one is the case the confirmation exists for.
/// </remarks>
[TestClass]
public sealed class PullDecisionTests
{
	private static FullyQualifiedLocalRepoPath CreateCommittedRepository()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_pull_{Guid.NewGuid():N}");
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

		return root.As<FullyQualifiedLocalRepoPath>();
	}

	private static void Cleanup(FullyQualifiedLocalRepoPath root)
	{
		try
		{
			Directory.Delete(root.WeakString, recursive: true);
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
	/// A clean tree must pull without interrupting the user.
	/// </summary>
	[TestMethod]
	public void ACleanWorkingTreePullsWithoutAsking()
	{
		// Arrange
		FullyQualifiedLocalRepoPath root = CreateCommittedRepository();

		try
		{
			// Act & Assert
			Assert.AreEqual(PullDecision.PullNow, ProjectDirector.DecidePull(root));
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A modification to a tracked file must trigger the confirmation.
	/// </summary>
	[TestMethod]
	public void AModifiedTrackedFileAsksFirst()
	{
		// Arrange
		FullyQualifiedLocalRepoPath root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root.WeakString, "tracked.txt"), "modified\n");

			// Act & Assert
			Assert.AreEqual(PullDecision.Confirm, ProjectDirector.DecidePull(root));
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// An untracked file counts too: a pull can still clobber it, so the user should be asked.
	/// </summary>
	[TestMethod]
	public void AnUntrackedFileAsksFirst()
	{
		// Arrange
		FullyQualifiedLocalRepoPath root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root.WeakString, "untracked.txt"), "new\n");

			// Act & Assert
			Assert.AreEqual(PullDecision.Confirm, ProjectDirector.DecidePull(root));
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// Staging a change does not make it committed, so it must still ask.
	/// </summary>
	[TestMethod]
	public void AStagedButUncommittedChangeAsksFirst()
	{
		// Arrange
		FullyQualifiedLocalRepoPath root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root.WeakString, "staged.txt"), "staged\n");
			Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded, "git add failed.");

			// Act & Assert
			Assert.AreEqual(PullDecision.Confirm, ProjectDirector.DecidePull(root));
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// Committing the change makes the tree clean again, so the confirmation must stop firing --
	/// the decision has to track the tree's current state, not merely that it was ever dirty.
	/// </summary>
	[TestMethod]
	public void CommittingTheChangeStopsTheConfirmation()
	{
		// Arrange
		FullyQualifiedLocalRepoPath root = CreateCommittedRepository();

		try
		{
			File.WriteAllText(Path.Join(root.WeakString, "tracked.txt"), "modified\n");
			Assert.AreEqual(PullDecision.Confirm, ProjectDirector.DecidePull(root));

			// Act
			Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded);
			Assert.IsTrue(GitCli.RunIn(root, "commit", "-m", "second").Succeeded);

			// Assert
			Assert.AreEqual(PullDecision.PullNow, ProjectDirector.DecidePull(root));
		}
		finally
		{
			Cleanup(root);
		}
	}
}
