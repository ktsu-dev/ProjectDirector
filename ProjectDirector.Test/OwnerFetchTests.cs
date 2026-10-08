// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
	/// Serves every request with one status until disposed.
	/// </summary>
	private sealed class FakeGitHub : IDisposable
	{
		private readonly HttpListener listener = new();
		private readonly CancellationTokenSource stop = new();
		private readonly Task loop;

		internal FakeGitHub(int status, string? rateLimitRemaining = null)
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

					context.Response.StatusCode = status;
					context.Response.ContentType = "application/json";
					if (rateLimitRemaining is not null)
					{
						context.Response.Headers["X-RateLimit-Limit"] = "60";
						context.Response.Headers["X-RateLimit-Remaining"] = rateLimitRemaining;
						context.Response.Headers["X-RateLimit-Reset"] = "1999999999";
					}

					byte[] body = Encoding.UTF8.GetBytes("""{"message":"refused by the fake"}""");
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
}
