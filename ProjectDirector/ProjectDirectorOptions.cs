namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using ktsu.AppDataStorage;
using ktsu.ImGuiApp;
using ktsu.StrongPaths;
using ktsu.StrongStrings;

public sealed record class OpenAIToken : StrongStringAbstract<OpenAIToken> { }
public sealed record class GitRemotePath : StrongStringAbstract<GitRemotePath> { }
public sealed record class GitRemotePathPrefix : StrongStringAbstract<GitRemotePathPrefix> { }
public sealed record class FullyQualifiedLocalRepoPath : StrongStringAbstract<FullyQualifiedLocalRepoPath> { }

public sealed class ProjectDirectorOptions : AppData<ProjectDirectorOptions>
{
	public AbsoluteDirectoryPath DevDirectory { get; set; } = (AbsoluteDirectoryPath)@"C:\dev";
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public GitHubLogin GitHubLogin { get; set; } = new();
	public GitHubToken GitHubToken { get; set; } = new();
	public OpenAIToken OpenAIToken { get; set; } = new();
	public Dictionary<string, bool> PanelStates { get; init; } = [];
	public Dictionary<string, List<float>> DividerStates { get; init; } = [];
	public Dictionary<GitHubOwnerName, GitHubToken> GitHubOwners { get; init; } = [];
	public Dictionary<GitHubOwnerName, Octokit.User> GitHubOwnerInfo { get; init; } = [];
	public Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> ClonedRepos { get; init; } = [];
	public FullyQualifiedGitHubRepoName BaseRepo { get; set; } = new();
	public FullyQualifiedGitHubRepoName CompareRepo { get; set; } = new();
	[JsonIgnore]
	public RelativeFilePath CompareFile { get; set; } = new();
	[JsonIgnore]
	public RelativePath PropagatePath { get; set; } = new();
	[JsonIgnore]
	public RelativeDirectoryPath BrowsePath { get; set; } = new();
	public Dictionary<FullyQualifiedGitHubRepoName, GitRepository> Repos { get; init; } = [];
}
