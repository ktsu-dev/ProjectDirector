// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Text.Json.Serialization;
using DiffPlex.Model;
using Semantics.Paths;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

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

	private Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> similarRepoDiffs = [];
	private int similarRepoRequested;
	private int similarRepoApplied;

	/// <summary>
	/// The last completed comparison of this repository against its siblings.
	/// </summary>
	/// <remarks>
	/// Published as a whole by <see cref="TryApplySimilarRepoDiffs"/> rather than filled in place,
	/// because the comparison runs on a background task while the render thread reads this. A
	/// reader therefore sees either the previous answer or the next one, never a half-built
	/// dictionary. The inner dictionaries stay writable so a single-file re-diff can update one
	/// entry without redoing the whole run.
	/// </remarks>
	[JsonIgnore]
	public IReadOnlyDictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> SimilarRepoDiffs => Volatile.Read(ref similarRepoDiffs);

	/// <summary>
	/// Whether a comparison has been asked for and has not yet published its answer.
	/// </summary>
	[JsonIgnore]
	public bool SimilarReposPending => Volatile.Read(ref similarRepoApplied) != Volatile.Read(ref similarRepoRequested);

	/// <summary>
	/// Discards the current comparison and returns a token identifying the new request.
	/// </summary>
	/// <returns>The token to hand back to <see cref="TryApplySimilarRepoDiffs"/>.</returns>
	/// <remarks>
	/// The old answer is dropped rather than left on screen: it describes whichever repository was
	/// selected before, so showing it while the next one is computed is worse than showing nothing.
	/// </remarks>
	internal int RequestSimilarRepoDiffs()
	{
		Volatile.Write(ref similarRepoDiffs, []);
		return Interlocked.Increment(ref similarRepoRequested);
	}

	/// <summary>
	/// Publishes the result of a comparison, unless a newer one has since been requested.
	/// </summary>
	/// <param name="token">The token from <see cref="RequestSimilarRepoDiffs"/>.</param>
	/// <param name="diffs">The comparison to publish.</param>
	/// <returns><see langword="true"/> when the result was published, <see langword="false"/> when a newer request superseded it.</returns>
	/// <remarks>
	/// Clicking through several repositories leaves that many comparisons in flight, and they do
	/// not finish in the order they started. Only the newest may publish, so a slow earlier run
	/// cannot overwrite the answer for the repository the user is actually looking at.
	/// </remarks>
	internal bool TryApplySimilarRepoDiffs(int token, Dictionary<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> diffs)
	{
		if (Volatile.Read(ref similarRepoRequested) != token)
		{
			return false;
		}

		Volatile.Write(ref similarRepoDiffs, diffs);
		Volatile.Write(ref similarRepoApplied, token);
		return true;
	}

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
		IsDirty = GitCli.HasUncommittedChanges(LocalPath);
		IsOutOfDate = false;
		// work out if the repository is behind the remote
	}
}
