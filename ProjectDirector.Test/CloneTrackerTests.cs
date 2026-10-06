// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers handing a background clone's completion back to the render thread.
/// </summary>
/// <remarks>
/// The Clone button used to refresh the page from the worker thread that ran the clone, racing the
/// render thread over the repository dictionaries and the repo browser, and nothing stopped a second
/// click from starting another clone into the same folder.
/// </remarks>
[TestClass]
public sealed class CloneTrackerTests
{
	private static FullyQualifiedLocalRepoPath LocalPath(string name) =>
		FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(Path.Join(Path.GetTempPath(), "dev", name));

	[TestMethod]
	public void ASecondCloneIntoTheSameFolderIsRefusedWhileTheFirstRuns()
	{
		CloneTracker clones = new();

		Assert.IsTrue(clones.TryStart(LocalPath("A")));
		Assert.IsTrue(clones.IsInFlight(LocalPath("A")));
		Assert.IsFalse(clones.TryStart(LocalPath("A")));
	}

	[TestMethod]
	public void ACloneIntoAnotherFolderIsNotBlocked()
	{
		CloneTracker clones = new();

		Assert.IsTrue(clones.TryStart(LocalPath("A")));
		Assert.IsTrue(clones.TryStart(LocalPath("B")));
	}

	[TestMethod]
	public void AFolderCanBeClonedAgainOnceItsCloneCompletes()
	{
		CloneTracker clones = new();
		Assert.IsTrue(clones.TryStart(LocalPath("A")));

		clones.Complete(LocalPath("A"));

		Assert.IsFalse(clones.IsInFlight(LocalPath("A")));
		Assert.IsTrue(clones.TryStart(LocalPath("A")));
	}

	[TestMethod]
	public void NoRefreshIsRequestedUntilACloneCompletes()
	{
		CloneTracker clones = new();
		Assert.IsTrue(clones.TryStart(LocalPath("A")));

		Assert.IsFalse(clones.TakeRefreshRequest());
	}

	[TestMethod]
	public async Task ACloneCompletedOnAWorkerThreadIsRefreshedByTheCallerThatTakesTheRequestOnce()
	{
		CloneTracker clones = new();
		Assert.IsTrue(clones.TryStart(LocalPath("A")));

		await Task.Run(() => clones.Complete(LocalPath("A"))).ConfigureAwait(false);

		Assert.IsTrue(clones.TakeRefreshRequest());
		Assert.IsFalse(clones.TakeRefreshRequest());
	}

	[TestMethod]
	public async Task TryRunRefusesASecondCloneWhileTheFirstRunsAndRequestsARefreshWhenItEnds()
	{
		CloneTracker clones = new();
		using ManualResetEventSlim release = new();

		Assert.IsTrue(clones.TryRun(LocalPath("A"), release.Wait, out Task first));
		Assert.IsTrue(clones.IsInFlight(LocalPath("A")));
		Assert.IsFalse(clones.TryRun(LocalPath("A"), () => Assert.Fail("A duplicate clone ran."), out Task refused));
		Assert.IsTrue(refused.IsCompleted);

		release.Set();
		await first.ConfigureAwait(false);

		Assert.IsFalse(clones.IsInFlight(LocalPath("A")));
		Assert.IsTrue(clones.TakeRefreshRequest());
	}

	[TestMethod]
	public async Task ACloneThatThrowsIsStillRecordedAsComplete()
	{
		CloneTracker clones = new();

		Assert.IsTrue(clones.TryRun(LocalPath("A"), () => throw new InvalidOperationException("clone failed"), out Task run));
		_ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run).ConfigureAwait(false);

		Assert.IsFalse(clones.IsInFlight(LocalPath("A")));
		Assert.IsTrue(clones.TakeRefreshRequest());
	}

	[TestMethod]
	public void RefreshIfRequestedRefreshesOnceForACompletedClone()
	{
		CloneTracker clones = new();
		int refreshes = 0;

		clones.RefreshIfRequested(() => refreshes++);
		Assert.AreEqual(0, refreshes);

		clones.Complete(LocalPath("A"));
		clones.RefreshIfRequested(() => refreshes++);
		clones.RefreshIfRequested(() => refreshes++);

		Assert.AreEqual(1, refreshes);
	}

	[TestMethod]
	public void MakeCloneClonesTheRemoteIntoTheFolderAndLogsTheResult()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}");
		try
		{
			string origin = Path.Join(root, "origin");
			string clone = Path.Join(root, "clone");
			Assert.IsTrue(GitCli.Run("init", origin).Succeeded, "git init failed.");
			string? description = null;
			GitResult? result = null;

			ProjectDirector.MakeClone(
				GitRemotePath.Create<GitRemotePath>(origin),
				FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(clone),
				(d, r) => (description, result) = (d, r))();

			Assert.AreEqual($"Cloning {origin}", description);
			Assert.IsNotNull(result);
			Assert.IsTrue(result.Succeeded);
			Assert.IsTrue(GitCli.IsRepository(clone));
		}
		finally
		{
			TryDeleteDirectory(root);
		}
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
			// A best-effort cleanup of a temp directory is not worth failing a test over.
		}
		catch (UnauthorizedAccessException)
		{
			// As above.
		}
	}
}
