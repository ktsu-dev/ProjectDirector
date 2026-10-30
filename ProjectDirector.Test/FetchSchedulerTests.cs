// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers how background fetches are scheduled.
/// </summary>
/// <remarks>
/// Every stale repository used to start its own <c>git fetch</c> on the same frame, with no bound and
/// no check for a fetch already running in that repository.
/// </remarks>
[TestClass]
public sealed class FetchSchedulerTests
{
	private static FullyQualifiedLocalRepoPath LocalPath(string name) =>
		FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(Path.Join(Path.GetTempPath(), "dev", name));

	[TestMethod]
	public async Task FortyRepositoriesNeverFetchMoreThanTheBoundAtOnce()
	{
		FetchScheduler fetches = new(FetchScheduler.DefaultMaxConcurrent);
		int running = 0;
		int mostRunning = 0;
		int finished = 0;

		Task[] runs = [.. Enumerable.Range(0, 40).Select(i =>
		{
			Assert.IsTrue(fetches.TryRun(LocalPath($"repo{i}"), () =>
			{
				int now = Interlocked.Increment(ref running);
				InterlockedMax(ref mostRunning, now);
				Thread.Sleep(20);
				_ = Interlocked.Decrement(ref running);
				_ = Interlocked.Increment(ref finished);
			}, out Task run));
			return run;
		})];

		await Task.WhenAll(runs).ConfigureAwait(false);

		Assert.AreEqual(40, finished);
		Assert.IsLessThanOrEqualTo(FetchScheduler.DefaultMaxConcurrent, mostRunning);
		Assert.IsGreaterThan(1, mostRunning, "the bound should still allow fetches to run side by side");
	}

	[TestMethod]
	public async Task ASecondFetchInTheSameRepositoryIsRefusedWhileTheFirstRuns()
	{
		FetchScheduler fetches = new(FetchScheduler.DefaultMaxConcurrent);
		using ManualResetEventSlim release = new();
		int started = 0;

		Assert.IsTrue(fetches.TryRun(LocalPath("A"), () =>
		{
			_ = Interlocked.Increment(ref started);
			release.Wait();
		}, out Task first));

		Assert.IsTrue(fetches.IsInFlight(LocalPath("A")));
		Assert.IsFalse(fetches.TryRun(LocalPath("A"), () => Interlocked.Increment(ref started), out Task second));
		Assert.IsTrue(second.IsCompleted);
		Assert.IsTrue(fetches.TryRun(LocalPath("B"), () => { }, out Task other));

		release.Set();
		await Task.WhenAll(first, other).ConfigureAwait(false);

		Assert.AreEqual(1, started);
		Assert.IsFalse(fetches.IsInFlight(LocalPath("A")));
	}

	[TestMethod]
	public async Task ARepositoryCanBeFetchedAgainAfterItsFetchThrows()
	{
		FetchScheduler fetches = new(1);

		Assert.IsTrue(fetches.TryRun(LocalPath("A"), () => throw new InvalidOperationException("fetch failed"), out Task failed));
		_ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failed).ConfigureAwait(false);

		bool ranAgain = false;
		Assert.IsTrue(fetches.TryRun(LocalPath("A"), () => ranAgain = true, out Task retry));
		await retry.ConfigureAwait(false);

		Assert.IsTrue(ranAgain, "the failed fetch should have released both its slot and its repository");
	}

	[TestMethod]
	public void JitterStaysWithinAQuarterOfTheIntervalAndSpreadsRepositoriesOut()
	{
		Random random = new(448);
		TimeSpan[] jitters = [.. Enumerable.Range(0, 200).Select(_ => FetchScheduler.Jitter(60, random))];

		Assert.IsTrue(jitters.All(j => j >= TimeSpan.Zero && j < TimeSpan.FromSeconds(15)));
		Assert.IsGreaterThan(100, jitters.Distinct().Count());
		Assert.AreEqual(TimeSpan.Zero, FetchScheduler.Jitter(0, random));
	}

	private static void InterlockedMax(ref int target, int value)
	{
		int current = Volatile.Read(ref target);
		while (value > current)
		{
			int seen = Interlocked.CompareExchange(ref target, value, current);
			if (seen == current)
			{
				return;
			}

			current = seen;
		}
	}
}
