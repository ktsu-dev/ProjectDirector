namespace ktsu.io.ProjectDirector;

using System.Collections.Generic;
using AppDataStorage;
using ImGuiApp;
using StrongPaths;
using StrongStrings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public sealed record class GitHubOwnerName : StrongStringAbstract<GitHubOwnerName> { }
public sealed record class GitHubRepoName : StrongStringAbstract<GitHubRepoName> { }

public sealed class ProjectDirectorOptions : AppData<ProjectDirectorOptions>
{
	public AbsoluteDirectoryPath DevDirectory { get; set; } = (AbsoluteDirectoryPath)@"C:\dev";
	public ImGuiAppWindowState WindowState { get; set; } = new();

	public Dictionary<string, bool> PanelStates { get; init; } = new();
	public Dictionary<string, List<float>> DividerStates { get; init; } = new();
	public Dictionary<GitHubOwnerName, Octokit.User> CachedGitHubOwners { get; init; } = new();
	public HashSet<GitHubOwnerName> SelectedGitHubOwners { get; init; } = new();
	public DictionaryOfHashSets<GitHubOwnerName, Octokit.Repository> CachedGitHubRepos { get; init; } = new();
	public HashSet<GitHubRepoName> ClonedGitHubRepos { get; init; } = new();
	public GitHubRepoName SelectedRepo { get; set; } = new();
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
