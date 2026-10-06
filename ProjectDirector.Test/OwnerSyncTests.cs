// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers an owner scan meeting a repository that Scan Dev Dir already found cloned elsewhere.
/// </summary>
/// <remarks>
/// Scan Dev Dir adds each clone's owner to the owners it scans, so the documented flow is Scan Dev
/// Dir followed by Scan GitHub Owners. An owner scan that moved such a repository to
/// <c>&lt;dev&gt;/&lt;owner&gt;/&lt;repo&gt;</c> pointed it at a folder that does not exist and left
/// the old clone recorded, so the repository fetched nothing and offered git actions that all failed.
/// </remarks>
[TestClass]
public sealed class OwnerSyncTests
{
	private static FullyQualifiedGitHubRepoName Name(string repoName) =>
		FullyQualifiedGitHubRepoName.Create<FullyQualifiedGitHubRepoName>($"ktsu-dev.{repoName}");

	private static FullyQualifiedLocalRepoPath LocalPath(string path) =>
		FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>(path);

	private static GitHubRepository Repository(string localPath, string repoName) => new()
	{
		OwnerName = GitHubOwnerName.Create<GitHubOwnerName>("ktsu-dev"),
		RepoName = GitHubRepoName.Create<GitHubRepoName>(repoName),
		LocalPath = LocalPath(localPath),
	};

	private static string CreateDevDirectory() =>
		Directory.CreateDirectory(Path.Join(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}")).FullName;

	[TestMethod]
	public void AnOwnerScanKeepsTheFolderARepositoryIsAlreadyClonedIn()
	{
		string dev = CreateDevDirectory();
		try
		{
			string clone = Path.Join(dev, "ProjectDirector");
			Assert.IsTrue(GitCli.Run("init", clone).Succeeded, "git init failed.");
			Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
			{
				[Name("ProjectDirector")] = Repository(clone, "ProjectDirector"),
			};

			FullyQualifiedLocalRepoPath chosen = ProjectDirector.ChooseSyncedLocalPath(
				repos, Name("ProjectDirector"), LocalPath(Path.Join(dev, "ktsu-dev", "ProjectDirector")));

			Assert.AreEqual(LocalPath(clone), chosen);
		}
		finally
		{
			TryDeleteDirectory(dev);
		}
	}

	[TestMethod]
	public void AnOwnerScanUsesTheConventionalFolderForAnUnknownRepository()
	{
		FullyQualifiedLocalRepoPath conventional = LocalPath(Path.Join(Path.GetTempPath(), "ktsu-dev", "New"));

		FullyQualifiedLocalRepoPath chosen = ProjectDirector.ChooseSyncedLocalPath(new Dictionary<FullyQualifiedGitHubRepoName, GitRepository>(), Name("New"), conventional);

		Assert.AreEqual(conventional, chosen);
	}

	[TestMethod]
	public void AnOwnerScanUsesTheConventionalFolderWhenTheKnownOneIsNotAClone()
	{
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}", "Gone");
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[Name("Gone")] = Repository(missing, "Gone"),
		};
		FullyQualifiedLocalRepoPath conventional = LocalPath(Path.Join(Path.GetTempPath(), "ktsu-dev", "Gone"));

		FullyQualifiedLocalRepoPath chosen = ProjectDirector.ChooseSyncedLocalPath(repos, Name("Gone"), conventional);

		Assert.AreEqual(conventional, chosen);
	}

	[TestMethod]
	public void ACloneRecordedAtAPathItsRepositoryNoLongerUsesIsPruned()
	{
		string oldPath = Path.Join(Path.GetTempPath(), "dev", "ProjectDirector");
		string newPath = Path.Join(Path.GetTempPath(), "dev", "ktsu-dev", "ProjectDirector");
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[Name("ProjectDirector")] = Repository(newPath, "ProjectDirector"),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> cloned = new()
		{
			[LocalPath(oldPath)] = Name("ProjectDirector"),
		};

		Assert.IsTrue(ProjectDirector.PruneStaleClonedRepos(cloned, repos));
		Assert.IsEmpty(cloned);
	}

	[TestMethod]
	public void ACloneOfAnUnknownRepositoryIsPruned()
	{
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> cloned = new()
		{
			[LocalPath(Path.Join(Path.GetTempPath(), "dev", "Orphan"))] = Name("Orphan"),
		};

		Assert.IsTrue(ProjectDirector.PruneStaleClonedRepos(cloned, new Dictionary<FullyQualifiedGitHubRepoName, GitRepository>()));
		Assert.IsEmpty(cloned);
	}

	[TestMethod]
	public void ACloneRecordedAtItsRepositorysPathIsKept()
	{
		string path = Path.Join(Path.GetTempPath(), "dev", "ProjectDirector");
		Dictionary<FullyQualifiedGitHubRepoName, GitRepository> repos = new()
		{
			[Name("ProjectDirector")] = Repository(path, "ProjectDirector"),
		};
		Dictionary<FullyQualifiedLocalRepoPath, FullyQualifiedGitHubRepoName> cloned = new()
		{
			[LocalPath(path)] = Name("ProjectDirector"),
		};

		Assert.IsFalse(ProjectDirector.PruneStaleClonedRepos(cloned, repos));
		Assert.HasCount(1, cloned);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			// Git marks objects read-only, which blocks a plain recursive delete on Windows.
			foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// A best-effort cleanup of a temp directory is not worth failing a test over.
		}
		catch (UnauthorizedAccessException)
		{
			// As above.
		}
	}
}
