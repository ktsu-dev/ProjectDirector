// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs background fetches a few at a time, and never two in the same repository at once.
/// </summary>
/// <remarks>
/// Every cloned repository used to start its own <c>git fetch</c> the frame it went stale. They all
/// went stale together, so a user with forty clones got forty concurrent fetches every minute, enough
/// for GitHub to drop SSH handshakes. A fetch slower than the interval was also started again in the
/// same repository while the first still ran, and the two fought over ref locks. A fetch waiting for a
/// slot counts as in flight, so a stale repository is queued at most once.
/// </remarks>
/// <param name="maxConcurrent">The most fetches allowed to run at once.</param>
internal sealed class FetchScheduler(int maxConcurrent)
{
	/// <summary>
	/// The bound used by the application.
	/// </summary>
	internal const int DefaultMaxConcurrent = 4;

	private readonly Queue<(FullyQualifiedLocalRepoPath LocalPath, Action Fetch, TaskCompletionSource Done)> waiting = new();
	private readonly HashSet<FullyQualifiedLocalRepoPath> inFlight = [];
	private readonly Lock gate = new();
	private int running;

	/// <summary>
	/// Gets a value indicating whether a fetch in <paramref name="localPath"/> is running or waiting to run.
	/// </summary>
	/// <param name="localPath">The repository to ask about.</param>
	/// <returns><see langword="true"/> until the fetch ends.</returns>
	internal bool IsInFlight(FullyQualifiedLocalRepoPath localPath)
	{
		lock (gate)
		{
			return inFlight.Contains(localPath);
		}
	}

	/// <summary>
	/// Queues <paramref name="fetch"/> to run once a slot is free, unless a fetch in
	/// <paramref name="localPath"/> is already running or queued.
	/// </summary>
	/// <param name="localPath">The repository being fetched.</param>
	/// <param name="fetch">The fetch itself. It must not touch UI state.</param>
	/// <param name="run">The queued fetch, or a completed task if the fetch was refused.</param>
	/// <returns><see langword="true"/> if the fetch was queued.</returns>
	/// <remarks>
	/// The fetch releases its slot and its repository however it ends, so a fetch that throws can be retried.
	/// </remarks>
	internal bool TryRun(FullyQualifiedLocalRepoPath localPath, Action fetch, out Task run)
	{
		TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (gate)
		{
			if (!inFlight.Add(localPath))
			{
				run = Task.CompletedTask;
				return false;
			}

			waiting.Enqueue((localPath, fetch, done));
		}

		run = done.Task;
		StartWaitingFetches();
		return true;
	}

	private void StartWaitingFetches()
	{
		lock (gate)
		{
			while (running < maxConcurrent && waiting.TryDequeue(out (FullyQualifiedLocalRepoPath LocalPath, Action Fetch, TaskCompletionSource Done) next))
			{
				running++;
				_ = Task.Run(() => Execute(next.LocalPath, next.Fetch, next.Done));
			}
		}
	}

	private void Execute(FullyQualifiedLocalRepoPath localPath, Action fetch, TaskCompletionSource done)
	{
		Exception? failure = null;
		try
		{
			fetch();
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			failure = ex;
		}
		finally
		{
			lock (gate)
			{
				running--;
				_ = inFlight.Remove(localPath);
			}
		}

		if (failure is null)
		{
			done.SetResult();
		}
		else
		{
			done.SetException(failure);
		}

		StartWaitingFetches();
	}

	/// <summary>
	/// Picks how far to backdate a repository's fetch time, so repositories fetched together go stale
	/// at different moments rather than all refiring on the same frame.
	/// </summary>
	/// <param name="intervalSeconds">The repository's fetch interval.</param>
	/// <param name="random">The source of randomness.</param>
	/// <returns>A duration from zero up to, but not including, a quarter of the interval.</returns>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Spreads fetch times; nothing depends on it being unpredictable.")]
	internal static TimeSpan Jitter(int intervalSeconds, Random random)
	{
		Ensure.NotNull(random);
		return intervalSeconds <= 0
			? TimeSpan.Zero
			: TimeSpan.FromSeconds(random.NextDouble() * intervalSeconds / 4);
	}
}
