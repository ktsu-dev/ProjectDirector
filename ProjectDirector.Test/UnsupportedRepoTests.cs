// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the rule that refuses a saved repository this application cannot act on.
/// </summary>
/// <remarks>
/// <see cref="GitRepository"/> registers <see cref="AzureDevOpsRepository"/> as a derived type for
/// polymorphic JSON, so a saved options file carrying one deserializes without complaint. Nothing
/// then acts on it: every site that pattern-matches a repository handles
/// <see cref="GitHubRepository"/> and throws otherwise. <c>UpdateClonedStatus</c> runs from the
/// constructor's <c>RefreshPage</c>, so that throw landed on the next launch, before the user could
/// open the UI to delete the entry causing it -- unrecoverable without hand-editing the file.
///
/// <c>RejectUnsupportedRepos</c> and <c>MakeLoadedOptionsSafe</c> exist so the one boundary such an
/// entry can arrive through can be driven without a live ImGui context, the way
/// <see cref="PullDecisionTests"/> drives the pull rule. What it guarantees is the invariant those six throw sites rest on: after it
/// runs, every repository left in the options is a <see cref="GitHubRepository"/>.
/// </remarks>
[TestClass]
public sealed class UnsupportedRepoTests
{
	private static FullyQualifiedGitHubRepoName RepoName(string value) => FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>(value);
	private static FullyQualifiedLocalRepoPath LocalPath(string value) => FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(value);

	/// <summary>
	/// The premise: an Azure DevOps repository really does come back from JSON as a live object,
	/// rather than being rejected by the serializer.
	/// </summary>
	/// <remarks>
	/// The discriminator alone is enough, which is the point -- this is what a hand-edited options
	/// file, or one left over from a partially built feature, looks like.
	/// </remarks>
	[TestMethod]
	public void AnAzureDevOpsRepositoryDeserializesIntoALiveObject()
	{
		GitRepository? fromSavedOptions = JsonSerializer.Deserialize<GitRepository>("""{"TypeName":"AzureDevOpsRepository"}""");

		Assert.IsInstanceOfType<AzureDevOpsRepository>(fromSavedOptions,
			"If this stops holding, the crash this rule guards against is no longer reachable and the rule can go.");
	}

	/// <summary>
	/// The regression this file exists for.
	/// </summary>
	[TestMethod]
	public void AnUnsupportedRepoIsDroppedAndTheSupportedOnesAreKept()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[RepoName("ktsu-dev.ProjectDirector")] = new GitHubRepository(),
			[RepoName("contoso.internal")] = new AzureDevOpsRepository(),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> clonedRepos = [];

		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = ProjectDirector.RejectUnsupportedRepos(repos, clonedRepos);

		CollectionAssert.AreEqual(new[] { RepoName("contoso.internal") }, rejected.ToArray());
		CollectionAssert.AreEqual(
			new[] { RepoName("ktsu-dev.ProjectDirector") },
			repos.Keys.ToArray(),
			"The supported repositories must survive.");

		Assert.IsTrue(repos.Values.All(repo => repo is GitHubRepository),
			"Every site that pattern-matches a repository rests on this invariant.");
	}

	[TestMethod]
	public void NothingUnsupportedMeansNothingIsTouched()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[RepoName("ktsu-dev.ProjectDirector")] = new GitHubRepository(),
			[RepoName("ktsu-dev.ImGuiApp")] = new GitHubRepository(),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> clonedRepos = new()
		{
			[LocalPath("/dev/ProjectDirector")] = RepoName("ktsu-dev.ProjectDirector"),
		};

		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = ProjectDirector.RejectUnsupportedRepos(repos, clonedRepos);

		Assert.AreEqual(0, rejected.Count);
		Assert.AreEqual(2, repos.Count);
		Assert.AreEqual(1, clonedRepos.Count);
		Assert.AreEqual(
			RepoName("ktsu-dev.ProjectDirector"),
			ProjectDirector.ClearSelectionIfRejected(RepoName("ktsu-dev.ProjectDirector"), rejected),
			"An ordinary selection must not be disturbed.");
	}

	[TestMethod]
	public void ACloneRecordedAgainstADroppedRepoIsCleared()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[RepoName("contoso.internal")] = new AzureDevOpsRepository(),
			[RepoName("ktsu-dev.ProjectDirector")] = new GitHubRepository(),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> clonedRepos = new()
		{
			[LocalPath("/dev/internal")] = RepoName("contoso.internal"),
			[LocalPath("/dev/ProjectDirector")] = RepoName("ktsu-dev.ProjectDirector"),
		};

		_ = ProjectDirector.RejectUnsupportedRepos(repos, clonedRepos);

		CollectionAssert.AreEqual(
			new[] { LocalPath("/dev/ProjectDirector") },
			clonedRepos.Keys.ToArray(),
			"A clone must not keep naming a repository that is no longer in the options.");
	}

	[TestMethod]
	public void EveryRepoBeingUnsupportedLeavesAnEmptyButUsableState()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[RepoName("contoso.internal")] = new AzureDevOpsRepository(),
			[RepoName("contoso.other")] = new AzureDevOpsRepository(),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> clonedRepos = new()
		{
			[LocalPath("/dev/internal")] = RepoName("contoso.internal"),
		};

		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = ProjectDirector.RejectUnsupportedRepos(repos, clonedRepos);

		Assert.AreEqual(2, rejected.Count);
		Assert.AreEqual(0, repos.Count);
		Assert.AreEqual(0, clonedRepos.Count);
	}

	/// <summary>
	/// The whole of what the constructor does after loading, against real options: the crashing
	/// entry goes, the selection pointing at it goes with it, the supported repository stays, and
	/// the user is told why.
	/// </summary>
	[TestMethod]
	public void LoadedOptionsCarryingAnUnsupportedRepoAreMadeSafe()
	{
		using ProjectDirectorOptions options = new()
		{
			BaseRepo = RepoName("contoso.internal"),
			CompareRepo = RepoName("ktsu-dev.ProjectDirector"),
		};
		options.Repos[RepoName("contoso.internal")] = new AzureDevOpsRepository();
		options.Repos[RepoName("ktsu-dev.ProjectDirector")] = new GitHubRepository();
		options.ClonedRepos[LocalPath("/dev/internal")] = RepoName("contoso.internal");

		List<string> logged = [];
		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = ProjectDirector.MakeLoadedOptionsSafe(options, logged.Add);

		CollectionAssert.AreEqual(new[] { RepoName("contoso.internal") }, rejected.ToArray());
		CollectionAssert.AreEqual(new[] { RepoName("ktsu-dev.ProjectDirector") }, options.Repos.Keys.ToArray());
		Assert.AreEqual(0, options.ClonedRepos.Count);

		Assert.AreEqual(new FullyQualifiedGitHubRepoName(), options.BaseRepo, "The base selection named the rejected repository.");
		Assert.AreEqual(RepoName("ktsu-dev.ProjectDirector"), options.CompareRepo, "The compare selection named a surviving one and must be left alone.");

		Assert.AreEqual(1, logged.Count, "Each rejection should be reported once.");
		StringAssert.Contains(logged[0], "contoso.internal", StringComparison.Ordinal);
	}

	[TestMethod]
	public void LoadedOptionsWithNothingUnsupportedAreLeftAlone()
	{
		using ProjectDirectorOptions options = new()
		{
			BaseRepo = RepoName("ktsu-dev.ProjectDirector"),
		};
		options.Repos[RepoName("ktsu-dev.ProjectDirector")] = new GitHubRepository();

		List<string> logged = [];
		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = ProjectDirector.MakeLoadedOptionsSafe(options, logged.Add);

		Assert.AreEqual(0, rejected.Count);
		Assert.AreEqual(1, options.Repos.Count);
		Assert.AreEqual(RepoName("ktsu-dev.ProjectDirector"), options.BaseRepo);
		Assert.AreEqual(0, logged.Count, "Nothing to reject means nothing to report.");
	}

	/// <summary>
	/// Fresh options have to be constructible on every platform this repository tests on, which is
	/// what a hardcoded <c>C:\dev</c> default prevented.
	/// </summary>
	[TestMethod]
	public void FreshOptionsCanBeConstructed()
	{
		using ProjectDirectorOptions options = new();

		Assert.IsFalse(string.IsNullOrEmpty(options.DevDirectory), "A fresh install needs a usable dev directory default.");
	}

	[TestMethod]
	public void EmptyOptionsAreHandled()
	{
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = [];
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> clonedRepos = [];

		Assert.AreEqual(0, ProjectDirector.RejectUnsupportedRepos(repos, clonedRepos).Count);
		Assert.AreEqual(0, repos.Count);
	}

	/// <summary>
	/// A selection left pointing at a dropped repository would send the panels straight back into
	/// <c>Options.Repos[Options.BaseRepo]</c>, turning one crash into another.
	/// </summary>
	[TestMethod]
	public void ASelectionPointingAtADroppedRepoIsCleared()
	{
		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = [RepoName("contoso.internal")];

		Assert.AreEqual(
			new FullyQualifiedGitHubRepoName(),
			ProjectDirector.ClearSelectionIfRejected(RepoName("contoso.internal"), rejected),
			"The selection must not outlive the repository it names.");
	}

	[TestMethod]
	public void ASelectionNamingASurvivingRepoIsKept()
	{
		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = [RepoName("contoso.internal")];

		Assert.AreEqual(
			RepoName("ktsu-dev.ProjectDirector"),
			ProjectDirector.ClearSelectionIfRejected(RepoName("ktsu-dev.ProjectDirector"), rejected));
	}

	[TestMethod]
	public void AnEmptySelectionSurvivesAnEmptyRejectionList()
	{
		IReadOnlyList<FullyQualifiedGitHubRepoName> rejected = [];

		Assert.AreEqual(
			new FullyQualifiedGitHubRepoName(),
			ProjectDirector.ClearSelectionIfRejected(new FullyQualifiedGitHubRepoName(), rejected),
			"A fresh install selects nothing, and that must not be mistaken for a rejection.");
	}
}
