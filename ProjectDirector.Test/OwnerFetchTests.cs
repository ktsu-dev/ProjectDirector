// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Octokit;

/// <summary>
/// Tests that an owner GitHub refuses is answered as a failure rather than thrown.
/// </summary>
/// <remarks>
/// Adding an owner and scanning owners both run on the render thread. The repository listing used
/// <c>.Result</c>, which wraps a failure in an <see cref="AggregateException"/> that the
/// <see cref="ApiException"/> handler around it never matched, and the account lookup before it had
/// no handler at all. So a typo in a new owner's name, a renamed owner, a revoked token or an
/// exhausted rate limit took the scan, and the window, down. These point a real
/// <see cref="GitHubClient"/> at a local listener that answers every request with the status under
/// test, so Octokit's own exception mapping is what gets exercised.
/// </remarks>
[TestClass]
public sealed class OwnerFetchTests
{
	private static GitHubOwnerName Owner(string value) => GitHubOwnerName.Create<GitHubOwnerName>(value);

	private static int FreePort()
	{
		using TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		return ((IPEndPoint)probe.LocalEndpoint).Port;
	}

	/// <summary>
	/// Answers each request with whatever the route function returns for its path, until disposed.
	/// </summary>
	private sealed class FakeGitHub : IDisposable
	{
		private readonly HttpListener listener = new();
		private readonly CancellationTokenSource stop = new();
		private readonly Task loop;

		internal FakeGitHub(int status, string? rateLimitRemaining = null)
			: this(_ => (status, """{"message":"refused by the fake"}"""), rateLimitRemaining)
		{
		}

		internal FakeGitHub(Func<string, (int Status, string Body)> route, string? rateLimitRemaining = null)
		{
			int port = FreePort();
			BaseAddress = new Uri($"http://127.0.0.1:{port}/");
			listener.Prefixes.Add(BaseAddress.ToString());
			listener.Start();
			loop = Task.Run(async () =>
			{
				while (!stop.IsCancellationRequested)
				{
					HttpListenerContext context;
					try
					{
						context = await listener.GetContextAsync().ConfigureAwait(false);
					}
					catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
					{
						return;
					}

					(int status, string json) = route(context.Request.Url!.AbsolutePath);
					context.Response.StatusCode = status;
					context.Response.ContentType = "application/json";
					if (rateLimitRemaining is not null)
					{
						context.Response.Headers["X-RateLimit-Limit"] = "60";
						context.Response.Headers["X-RateLimit-Remaining"] = rateLimitRemaining;
						context.Response.Headers["X-RateLimit-Reset"] = "1999999999";
					}

					byte[] body = Encoding.UTF8.GetBytes(json);
					await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
					context.Response.Close();
				}
			});
		}

		internal Uri BaseAddress { get; }

		public void Dispose()
		{
			stop.Cancel();
			listener.Stop();
			listener.Close();
			_ = loop.Wait(TimeSpan.FromSeconds(5));
			stop.Dispose();
		}
	}

	private static GitHubClient ClientFor(Uri baseAddress) => new(new ProductHeaderValue("ktsu-pd-test"), baseAddress);

	[TestMethod]
	[DataRow(404, null, DisplayName = "Owner does not exist")]
	[DataRow(401, null, DisplayName = "Token revoked")]
	[DataRow(403, null, DisplayName = "Forbidden")]
	[DataRow(403, "0", DisplayName = "Rate limit exhausted")]
	public void AnOwnerGitHubRefusesIsReportedRatherThanThrown(int status, string? rateLimitRemaining)
	{
		using FakeGitHub fake = new(status, rateLimitRemaining);

		ProjectDirector.OwnerFetchResult result = ProjectDirector.FetchOwner(ClientFor(fake.BaseAddress), Owner("nobody"));

		Assert.IsNotNull(result.Failure, "A refused owner should come back with the reason it was skipped.");
		Assert.IsNull(result.Owner);
		Assert.IsEmpty(result.Repos);
	}

	[TestMethod]
	public void AnUnreachableGitHubIsReportedRatherThanThrown()
	{
		// Nothing listens on a port that was free a moment ago, so the connection is refused the way
		// it is when the machine is offline.
		Uri nowhere = new($"http://127.0.0.1:{FreePort()}/");

		ProjectDirector.OwnerFetchResult result = ProjectDirector.FetchOwner(ClientFor(nowhere), Owner("nobody"));

		Assert.IsNotNull(result.Failure);
		Assert.IsNull(result.Owner);
	}

	private static string RepoJson(string owner, string name) =>
		$$$"""{"id":1,"name":"{{{name}}}","full_name":"{{{owner}}}/{{{name}}}","clone_url":"https://github.com/{{{owner}}}/{{{name}}}.git","owner":{"login":"{{{owner}}}","id":1}}""";

	/// <summary>
	/// The path that is not an error: an organization's account, its own listing and its org listing
	/// are all read, and every repository lands in the known repositories under the dev directory.
	/// </summary>
	[TestMethod]
	public void AnOrganizationsRepositoriesAreReadAndRecorded()
	{
		using FakeGitHub fake = new(path => path switch
		{
			_ when path.EndsWith("/users/ktsu-dev", StringComparison.Ordinal) => (200, """{"login":"ktsu-dev","id":1,"type":"Organization"}"""),
			_ when path.EndsWith("/users/ktsu-dev/repos", StringComparison.Ordinal) => (200, $"[{RepoJson("ktsu-dev", "Alpha")}]"),
			_ when path.EndsWith("/orgs/ktsu-dev/repos", StringComparison.Ordinal) => (200, $"[{RepoJson("ktsu-dev", "Beta")}]"),
			_ => (404, """{"message":"not routed"}"""),
		});

		ProjectDirector.OwnerFetchResult result = ProjectDirector.FetchOwner(ClientFor(fake.BaseAddress), Owner("ktsu-dev"));

		Assert.IsNull(result.Failure, result.Failure);
		Assert.AreEqual(AccountType.Organization, result.Owner!.Type);
		Assert.AreSequenceEqual(["Alpha", "Beta"], [.. result.Repos.Select(repo => repo.Name)]);

		string dev = Path.Join(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}");
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = [];
		ProjectDirector.MergeRemoteRepos(repos, AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(dev), Owner("ktsu-dev"), result.Repos);

		Assert.HasCount(2, repos);
		GitHubRepository alpha = (GitHubRepository)repos[FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>("ktsu-dev.Alpha")];
		Assert.AreEqual(Owner("ktsu-dev"), alpha.OwnerName);
		Assert.AreEqual("Alpha", (string)alpha.RepoName);
		Assert.AreEqual(Path.GetFullPath(Path.Join(dev, "ktsu-dev", "Alpha")), (string)alpha.LocalPath);
	}

	[TestMethod]
	public void SyncingARefusedOwnerLogsItAndLeavesTheOptionsAlone()
	{
		using FakeGitHub fake = new(404);
		using ProjectDirectorOptions options = new();
		List<string> log = [];

		bool changed = ProjectDirector.SyncOwner(ClientFor(fake.BaseAddress), options, Owner("nobody"), GitHubToken.Create<GitHubToken>(string.Empty), log.Add);

		Assert.IsFalse(changed);
		Assert.HasCount(1, log);
		Assert.Contains("Skipped GitHub owner nobody", log[0]);
		Assert.IsEmpty(options.GitHubOwnerInfo);
		Assert.IsEmpty(options.Repos);
	}

	[TestMethod]
	public void SyncingAnOwnerRecordsItWithItsOwnCredentials()
	{
		using FakeGitHub fake = new(path => path switch
		{
			_ when path.EndsWith("/users/alpha", StringComparison.Ordinal) => (200, """{"login":"alpha","id":1,"type":"User"}"""),
			_ when path.EndsWith("/users/alpha/repos", StringComparison.Ordinal) => (200, $"[{RepoJson("alpha", "One")}]"),
			_ => (404, """{"message":"not routed"}"""),
		});
		using ProjectDirectorOptions options = new()
		{
			DevDirectory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(Path.Join(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}")),
		};
		GitHubClient client = ClientFor(fake.BaseAddress);
		List<string> log = [];

		bool changed = ProjectDirector.SyncOwner(client, options, Owner("alpha"), GitHubToken.Create<GitHubToken>("alpha-pat"), log.Add);

		Assert.IsTrue(changed);
		Assert.IsEmpty(log);
		Assert.AreEqual("alpha", client.Credentials.Login, "The owner should be read with its own token.");
		Assert.IsTrue(options.GitHubOwnerInfo.ContainsKey(Owner("alpha")));
		Assert.IsTrue(options.Repos.ContainsKey(FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>("alpha.One")));
	}
}
