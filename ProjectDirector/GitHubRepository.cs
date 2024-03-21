namespace ktsu.io.ProjectDirector;

using ktsu.io.StrongStrings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public sealed record class GitHubOwnerName : StrongStringAbstract<GitHubOwnerName> { }
public sealed record class GitHubRepoName : StrongStringAbstract<GitHubRepoName> { }
public sealed record class FullyQualifiedGitHubRepoName : StrongStringAbstract<FullyQualifiedGitHubRepoName> { }
public sealed record class GitHubLogin : StrongStringAbstract<GitHubLogin> { }
public sealed record class GitHubPAT : StrongStringAbstract<GitHubPAT> { }

public sealed class GitHubRepository : GitRepository
{
	public GitHubOwnerName OwnerName { get; set; } = new();
	public GitHubRepoName RepoName { get; set; } = new();

	internal static bool IsRemotePathValid(GitRemotePath remotePath)
	{
		ArgumentNullException.ThrowIfNull(remotePath, nameof(remotePath));
		return remotePath.StartsWith("https://github.com/", StringComparison.Ordinal);
	}
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
