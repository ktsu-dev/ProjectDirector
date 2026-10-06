// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the parsing of a GitHub remote into its owner and repository name.
/// </summary>
/// <remarks>
/// "Scan Dev Dir" keeps a working tree only when its origin parses here, so a form left out drops
/// every repository cloned that way without a word. SSH clones were exactly that case.
/// </remarks>
[TestClass]
public sealed class GitHubRemoteTests
{
	[TestMethod]
	[DataRow("https://github.com/ktsu-dev/ProjectDirector")]
	[DataRow("https://github.com/ktsu-dev/ProjectDirector.git")]
	[DataRow("https://github.com/ktsu-dev/ProjectDirector/")]
	[DataRow("https://github.com/ktsu-dev/ProjectDirector.git/")]
	[DataRow("HTTPS://GitHub.com/ktsu-dev/ProjectDirector.git")]
	[DataRow("git@github.com:ktsu-dev/ProjectDirector")]
	[DataRow("git@github.com:ktsu-dev/ProjectDirector.git")]
	[DataRow("git@github.com:ktsu-dev/ProjectDirector/")]
	[DataRow("git@GitHub.com:ktsu-dev/ProjectDirector.git")]
	[DataRow("ssh://git@github.com/ktsu-dev/ProjectDirector")]
	[DataRow("ssh://git@github.com/ktsu-dev/ProjectDirector.git")]
	[DataRow("ssh://git@github.com/ktsu-dev/ProjectDirector.git/")]
	[DataRow("SSH://git@GITHUB.COM/ktsu-dev/ProjectDirector.git")]
	public void EveryGitHubRemoteFormYieldsItsOwnerAndRepository(string remote)
	{
		// Act
		bool parsed = GitHubRepository.TryParseRemote(remote, out string owner, out string repo);

		// Assert
		Assert.IsTrue(parsed, $"{remote} should be recognised as a GitHub remote.");
		Assert.AreEqual("ktsu-dev", owner);
		Assert.AreEqual("ProjectDirector", repo);
		Assert.IsTrue(GitHubRepository.IsRemotePathValid(GitRemotePath.Create<GitRemotePath>(remote)));
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("https://gitlab.com/ktsu-dev/ProjectDirector.git")]
	[DataRow("https://github.com.evil.example/ktsu-dev/ProjectDirector.git")]
	[DataRow("git@bitbucket.org:ktsu-dev/ProjectDirector.git")]
	[DataRow("https://github.com/ktsu-dev")]
	[DataRow("https://github.com/ktsu-dev/")]
	[DataRow("https://github.com//ProjectDirector")]
	[DataRow("https://github.com/ktsu-dev/ProjectDirector/tree/main")]
	[DataRow("C:/dev/ProjectDirector")]
	public void AnythingElseIsNotAGitHubRemote(string remote)
	{
		// Act
		bool parsed = GitHubRepository.TryParseRemote(remote, out string owner, out string repo);

		// Assert
		Assert.IsFalse(parsed, $"{remote} should not be recognised as a GitHub remote.");
		Assert.AreEqual(string.Empty, owner);
		Assert.AreEqual(string.Empty, repo);
	}

	[TestMethod]
	public void AnSshCloneCreatesAGitHubRepository()
	{
		// Act
		GitRepository? repo = GitRepository.Create(
			GitRemotePath.Create<GitRemotePath>("git@github.com:ktsu-dev/ProjectDirector.git"),
			FullyQualifiedLocalRepoPath.Create<FullyQualifiedLocalRepoPath>("/dev/ktsu-dev/ProjectDirector"));

		// Assert
		Assert.IsInstanceOfType<GitHubRepository>(repo);
	}
}
