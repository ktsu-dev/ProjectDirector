// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System.IO;
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
}
