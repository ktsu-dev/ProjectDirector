namespace ktsu.io.ProjectDirector;

using ktsu.io.StrongStrings;

public sealed record class GitHubOwnerName : StrongStringAbstract<GitHubOwnerName> { }
public sealed record class GitHubRepoName : StrongStringAbstract<GitHubRepoName> { }
public sealed record class FullyQualifiedGitHubRepoName : StrongStringAbstract<FullyQualifiedGitHubRepoName> { }
public sealed record class GitHubLogin : StrongStringAbstract<GitHubLogin> { }
public sealed record class GitHubToken : StrongStringAbstract<GitHubToken> { }

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
