// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.ProjectDirector;

using System.Collections.Generic;
using System.Text.Json.Serialization;
using ktsu.AppDataStorage;
using ktsu.ImGuiApp;
using Semantics.Paths;
using Semantics.Strings;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public sealed record class OpenAIToken : SemanticString<OpenAIToken> { }
public sealed record class GitRemotePath : SemanticString<GitRemotePath> { }
public sealed record class GitRemotePathPrefix : SemanticString<GitRemotePathPrefix> { }
public sealed record class FullyQualifiedLocalRepoPath : SemanticString<FullyQualifiedLocalRepoPath> { }

public sealed class ProjectDirectorOptions : AppData<ProjectDirectorOptions>
{
	public AbsoluteDirectoryPath DevDirectory { get; set; } = @"C:\dev".As<AbsoluteDirectoryPath>();
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
