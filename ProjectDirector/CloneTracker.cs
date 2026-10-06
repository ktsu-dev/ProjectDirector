// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Tracks the clones running in the background and hands their completion back to the render thread.
/// </summary>
/// <remarks>
/// A clone finishes on a worker thread, and refreshing the page from there mutated
/// <see cref="ProjectDirectorOptions.ClonedRepos"/>, <see cref="ProjectDirectorOptions.Repos"/> and the
/// repo browser while the render thread was enumerating them. So a finished clone only records that a
/// refresh is wanted, and the tick that runs on the render thread takes the request and refreshes.
/// The same record of what is in flight keeps a second click from starting another clone into the
/// folder the first one is still writing.
/// </remarks>
internal sealed class CloneTracker
{
	private readonly HashSet<FullyQualifiedLocalRepoPath> inFlight = [];
	private readonly Lock gate = new();
	private int refreshRequested;

	/// <summary>
	/// Records a clone into <paramref name="localPath"/> as started, unless one already is.
	/// </summary>
	/// <param name="localPath">The folder being cloned into.</param>
	/// <returns><see langword="true"/> if the caller should start the clone.</returns>
	internal bool TryStart(FullyQualifiedLocalRepoPath localPath)
	{
		lock (gate)
		{
			return inFlight.Add(localPath);
		}
	}

	/// <summary>
	/// Gets a value indicating whether a clone into <paramref name="localPath"/> is running.
	/// </summary>
	/// <param name="localPath">The folder to ask about.</param>
	/// <returns><see langword="true"/> while the clone runs.</returns>
	internal bool IsInFlight(FullyQualifiedLocalRepoPath localPath)
	{
		lock (gate)
		{
			return inFlight.Contains(localPath);
		}
	}

	/// <summary>
	/// Records a clone as finished, whether or not it succeeded, and asks for a refresh. Safe to call
	/// from any thread.
	/// </summary>
	/// <param name="localPath">The folder that was cloned into.</param>
	internal void Complete(FullyQualifiedLocalRepoPath localPath)
	{
		lock (gate)
		{
			_ = inFlight.Remove(localPath);
		}

		_ = Interlocked.Exchange(ref refreshRequested, 1);
	}

	/// <summary>
	/// Takes the pending refresh request, if any. Call from the render thread.
	/// </summary>
	/// <returns><see langword="true"/> once for each run of completions since the last call.</returns>
	internal bool TakeRefreshRequest() => Interlocked.Exchange(ref refreshRequested, 0) != 0;
}
