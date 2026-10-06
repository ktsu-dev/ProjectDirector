// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using Semantics.Strings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public sealed record class GitHubOwnerName : SemanticString<GitHubOwnerName> { }
public sealed record class GitHubRepoName : SemanticString<GitHubRepoName> { }
public sealed record class FullyQualifiedGitHubRepoName : SemanticString<FullyQualifiedGitHubRepoName> { }
public sealed record class GitHubLogin : SemanticString<GitHubLogin> { }
public sealed record class GitHubToken : SemanticString<GitHubToken> { }

public sealed class GitHubRepository : GitRepository
{
	public GitHubOwnerName OwnerName { get; set; } = new();
	public GitHubRepoName RepoName { get; set; } = new();

	private static readonly string[] RemotePrefixes = ["https://github.com/", "ssh://git@github.com/", "git@github.com:"];

	internal static bool IsRemotePathValid(GitRemotePath remotePath)
	{
		Ensure.NotNull(remotePath);
		return TryParseRemote(remotePath, out _, out _);
	}

	/// <summary>
	/// Reads the owner and repository name out of a GitHub remote.
	/// </summary>
	/// <param name="remote">The remote URL, as <c>git remote get-url</c> reports it.</param>
	/// <param name="owner">The owner, empty when the remote is not a GitHub remote.</param>
	/// <param name="repo">The repository name without any <c>.git</c> suffix, empty when the remote is not a GitHub remote.</param>
	/// <returns>Whether <paramref name="remote"/> names a repository on GitHub.</returns>
	/// <remarks>
	/// Accepts the three forms git itself clones from: <c>https://github.com/owner/repo</c>,
	/// the scp-style <c>git@github.com:owner/repo</c> and <c>ssh://git@github.com/owner/repo</c>,
	/// each with or without a <c>.git</c> suffix and a trailing <c>/</c>. The host is compared
	/// ignoring case, as DNS does, and the owner and repository name are kept as given. Git's
	/// credential helper and SSH agent are what make the SSH forms work, so turning them away here
	/// would drop every repository cloned over SSH.
	/// </remarks>
	internal static bool TryParseRemote(string remote, out string owner, out string repo)
	{
		owner = string.Empty;
		repo = string.Empty;

		if (string.IsNullOrWhiteSpace(remote))
		{
			return false;
		}

		string? path = null;
		foreach (string prefix in RemotePrefixes)
		{
			if (remote.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				path = remote[prefix.Length..];
				break;
			}
		}

		if (path is null)
		{
			return false;
		}

		path = path.TrimEnd('/');
		if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
		{
			path = path[..^".git".Length];
		}

		string[] parts = path.Split('/');
		if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
		{
			return false;
		}

		owner = parts[0];
		repo = parts[1];
		return true;
	}
}
