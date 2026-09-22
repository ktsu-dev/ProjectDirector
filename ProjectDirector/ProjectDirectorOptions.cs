// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using ktsu.AppDataStorage;
using ktsu.ImGui.App;
using Semantics.Paths;
using Semantics.Strings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public sealed record class OpenAIToken : SemanticString<OpenAIToken> { }
public sealed record class GitRemotePath : SemanticString<GitRemotePath> { }
public sealed record class GitRemotePathPrefix : SemanticString<GitRemotePathPrefix> { }
public sealed record class FullyQualifiedLocalRepoPath : SemanticString<FullyQualifiedLocalRepoPath> { }

public sealed class ProjectDirectorOptions : AppData<ProjectDirectorOptions>
{
	public AbsoluteDirectoryPath DevDirectory { get; set; } = DefaultDevDirectory();

	/// <summary>
	/// The dev directory a fresh install starts with.
	/// </summary>
	/// <remarks>
	/// <c>C:\dev</c> is not an absolute path anywhere but Windows, so
	/// <see cref="AbsoluteDirectoryPath"/> rejected it and this type could not be constructed at all
	/// off Windows -- not by the application, and not by a test. Windows keeps the path it has
	/// always had; everywhere else falls back to <c>~/dev</c>. Only a fresh install reads this, so a
	/// saved options file keeps whatever the user chose.
	/// </remarks>
	private static AbsoluteDirectoryPath DefaultDevDirectory() =>
		AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(OperatingSystem.IsWindows()
			? @"C:\dev"
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev"));
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
