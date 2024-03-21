namespace ktsu.io.ProjectDirector;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using AppDataStorage;
using ktsu.io.ImGuiApp;
using ktsu.io.StrongPaths;
using ktsu.io.StrongStrings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public sealed record class GitRemotePath : StrongStringAbstract<GitRemotePath> { }
public sealed record class GitRemotePathPrefix : StrongStringAbstract<GitRemotePathPrefix> { }
public sealed record class FullyQualifiedLocalRepoPath : StrongStringAbstract<FullyQualifiedLocalRepoPath> { }

public sealed class ProjectDirectorOptions : AppData<ProjectDirectorOptions>
{
	public AbsoluteDirectoryPath DevDirectory { get; set; } = (AbsoluteDirectoryPath)@"C:\dev";
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public GitHubLogin GitHubLogin { get; set; } = new();
	public GitHubPAT GitHubPAT { get; set; } = new();
	public Dictionary<string, bool> PanelStates { get; init; } = new();
	public Dictionary<string, List<float>> DividerStates { get; init; } = new();
	public Dictionary<GitHubOwnerName, GitHubPAT> GitHubOwners { get; init; } = new();
	public Dictionary<GitHubOwnerName, Octokit.User> GitHubOwnerInfo { get; init; } = new();
	public Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> ClonedRepos { get; init; } = new();
	public FullyQualifiedGitHubRepoName BaseRepo { get; set; } = new();
	public FullyQualifiedGitHubRepoName CompareRepo { get; set; } = new();
	[JsonIgnore]
	public RelativeFilePath CompareFile { get; set; } = new();
	[JsonIgnore]
	public RelativePath PropagatePath { get; set; } = new();
	[JsonIgnore]
	public RelativeDirectoryPath BrowsePath { get; set; } = new();
	public Dictionary<FullyQualifiedGitHubRepoName, GitRepository> Repos { get; init; } = new();
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
