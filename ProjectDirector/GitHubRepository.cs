// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

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

	internal static bool IsRemotePathValid(GitRemotePath remotePath)
	{
		ArgumentNullException.ThrowIfNull(remotePath);
		return remotePath.StartsWith("https://github.com/", StringComparison.Ordinal);
	}
}
