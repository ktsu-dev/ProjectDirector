// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.UITests;

using ktsu.ProjectDirector.UITests.Gallery;
using ktsu.Semantics.Paths;

/// <summary>
/// Drives the left panel's repository list, where every owner's repositories are drawn as rows of
/// the same kind of widget.
/// </summary>
[TestClass]
public sealed class RepoPanelTests
{
	private const string SharedName = ".github";

	private static readonly string[] Owners = ["ktsu-dev", "matt-edmondson"];

	[TestMethod]
	public void ClickingASharedRepoName_SelectsThatOwnersRepository()
	{
		using GalleryApp app = GalleryApp.Start(AppGallery.Display, CreateOptions);
		app.Harness.Step(2);

		// Click the later owner first: it is the row an ID clash with the earlier owner's row swallows.
		foreach (string owner in Owners.Reverse())
		{
			FullyQualifiedGitHubRepoName expected = FullName(owner);
			app.Harness.Click($"repo/{expected}");
			app.Harness.Step(2);

			Assert.AreEqual(expected, app.Options.BaseRepo, $"Clicking {owner}'s {SharedName} did not select it.");
		}
	}

	private static FullyQualifiedGitHubRepoName FullName(string owner) =>
		FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>($"{owner}.{SharedName}");

	private static ProjectDirectorOptions CreateOptions()
	{
		// Nothing is cloned, so the paths are never created; they only have to differ per owner.
		string root = Path.Combine(Path.GetTempPath(), "projectdirector-repo-panel");
		ProjectDirectorOptions options = new()
		{
			DevDirectory = AbsoluteDirectoryPath.Create<AbsoluteDirectoryPath>(root),
		};

		foreach (string ownerName in Owners)
		{
			GitHubOwnerName owner = GitHubOwnerName.Create<GitHubOwnerName>(ownerName);
			_ = options.GitHubOwners.Add(owner);
			options.Repos[FullName(ownerName)] = new GitHubRepository()
			{
				OwnerName = owner,
				RepoName = GitHubRepoName.Create<GitHubRepoName>(SharedName),
				LocalPath = FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(Path.Combine(root, ownerName, SharedName)),
				RemotePath = GitRemotePath.Create<GitRemotePath>($"https://github.com/{ownerName}/{SharedName}"),
				MinFetchIntervalSeconds = 0,
			};
		}

		return options;
	}
}
