// Ignore Spelling: Diffs

namespace ktsu.ProjectDirector;

using System.Text.Json.Serialization;
using DiffPlex.Model;
using ktsu.StrongPaths;
using LibGit2Sharp;

[JsonDerivedType(typeof(GitHubRepository), nameof(GitHubRepository))]
[JsonDerivedType(typeof(AzureDevOpsRepository), nameof(AzureDevOpsRepository))]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "TypeName")]
public abstract class GitRepository
{
	public int MinFetchIntervalSeconds { get; set; } = 60;
	public DateTime LastFetchTime { get; set; } = DateTime.MinValue;

	public GitRemotePath RemotePath { get; set; } = new();
	public FullyQualifiedLocalRepoPath LocalPath { get; set; } = new();

	public bool IsCloned => Directory.Exists(LocalPath);

	[JsonIgnore]
	public Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> SimilarRepoDiffs { get; } = [];

	public static GitRepository? Create(GitRemotePath remotePath, FullyQualifiedLocalRepoPath localPath)
	{
		return GitHubRepository.IsRemotePathValid(remotePath)
			? (GitRepository)new GitHubRepository
			{
				RemotePath = remotePath,
				LocalPath = localPath
			}
			: null;
	}

	public bool IsDirty { get; private set; }
	public bool IsOutOfDate { get; private set; }

	internal void UpdateStatus()
	{
		IsDirty = false;
		IsOutOfDate = false;

		using var repo = new Repository(LocalPath);
		var status = repo.RetrieveStatus();
		IsDirty = status.IsDirty;
		// work out if the repository is behind the remote
	}
}
