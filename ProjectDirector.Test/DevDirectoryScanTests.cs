// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the walk that finds working trees under the configured dev directory.
/// </summary>
/// <remarks>
/// The scan used to hand the whole tree to the recursive form of
/// <see cref="Directory.EnumerateDirectories(string, string, SearchOption)"/>, which leaves
/// <see cref="EnumerationOptions.IgnoreInaccessible"/> off and enumerates lazily. One unreadable
/// folder therefore threw part way through, on the render thread, and took the whole application
/// with it -- and a dev directory holds exactly the package caches, build output and IDE metadata
/// that produce such a folder. <see cref="ProjectDirector.EnumerateGitDirectories"/> exists
/// separately so that rule can be driven without a live ImGui context, the way
/// <see cref="PullDecisionTests"/> drives the pull rule.
/// </remarks>
[TestClass]
public sealed class DevDirectoryScanTests
{
	private static string CreateTree()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_scan_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(Path.Join(root, "alpha", ".git"));
		_ = Directory.CreateDirectory(Path.Join(root, "nested", "beta", ".git"));
		_ = Directory.CreateDirectory(Path.Join(root, "nested", "beta", "src"));
		_ = Directory.CreateDirectory(Path.Join(root, "notarepo"));
		return root;
	}

	private static string[] Walk(string root, Func<string, string[]>? listDirectories = null) =>
		[.. ProjectDirector.EnumerateGitDirectories(root, listDirectories)
			.Select(Path.GetFullPath)
			.Order(StringComparer.Ordinal)];

	[TestMethod]
	public void EveryWorkingTreeUnderTheRootIsFound()
	{
		string root = CreateTree();
		try
		{
			string[] found = Walk(root);

			CollectionAssert.AreEqual(
				new[]
				{
					Path.GetFullPath(Path.Join(root, "alpha", ".git")),
					Path.GetFullPath(Path.Join(root, "nested", "beta", ".git")),
				}.Order(StringComparer.Ordinal).ToArray(),
				found,
				"The walk should report every .git directory at any depth and nothing else.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// The regression this file exists for: a directory that refuses to be listed must cost only
	/// itself, not the repositories that sort after it.
	/// </summary>
	/// <remarks>
	/// The refusal is injected rather than arranged with file permissions because a test process
	/// running as root -- which CI containers routinely do -- bypasses the permission bits
	/// entirely, so <c>chmod</c> would quietly produce a readable directory and the test would pass
	/// against the unfixed code. <see cref="ADeniedDirectoryOnDiskIsSkipped"/> covers the real
	/// file system wherever the host can actually deny a read.
	/// </remarks>
	[TestMethod]
	public void ADeniedDirectoryCostsOnlyItsOwnSubtree()
	{
		string root = CreateTree();
		try
		{
			string denied = Path.Join(root, "nested");
			List<string> refused = [];

			string[] found = Walk(root, directory =>
			{
				if (string.Equals(directory, denied, StringComparison.Ordinal))
				{
					refused.Add(directory);
					throw new UnauthorizedAccessException($"Access to the path '{directory}' is denied.");
				}

				return Directory.GetDirectories(directory);
			});

			Assert.AreEqual(1, refused.Count, "The walk should have reached the denied directory exactly once.");
			CollectionAssert.AreEqual(
				new[] { Path.GetFullPath(Path.Join(root, "alpha", ".git")) },
				found,
				"The readable half of the tree should survive a directory that refuses to be listed.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void ADirectoryThatVanishesMidWalkIsSkipped()
	{
		string root = CreateTree();
		try
		{
			string vanished = Path.Join(root, "nested");

			string[] found = Walk(root, directory => string.Equals(directory, vanished, StringComparison.Ordinal)
				? throw new DirectoryNotFoundException($"Could not find a part of the path '{directory}'.")
				: Directory.GetDirectories(directory));

			CollectionAssert.AreEqual(
				new[] { Path.GetFullPath(Path.Join(root, "alpha", ".git")) },
				found,
				"A directory removed while the scan is running should not end the scan.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void AMissingRootYieldsNothingRatherThanThrowing()
	{
		string missing = Path.Join(Path.GetTempPath(), $"ktsu_pd_scan_{Guid.NewGuid():N}");

		CollectionAssert.AreEqual(Array.Empty<string>(), Walk(missing), "A dev directory that does not exist is not a crash.");
	}

	[TestMethod]
	public void TheContentsOfAGitDirectoryAreNotWalked()
	{
		string root = CreateTree();
		try
		{
			// A working tree checked out inside another repository's storage is not a second
			// repository, and git's own object store is large enough to be worth not descending into.
			_ = Directory.CreateDirectory(Path.Join(root, "alpha", ".git", "modules", "sub", ".git"));

			string[] found = Walk(root);

			CollectionAssert.AreEqual(
				new[]
				{
					Path.GetFullPath(Path.Join(root, "alpha", ".git")),
					Path.GetFullPath(Path.Join(root, "nested", "beta", ".git")),
				}.Order(StringComparer.Ordinal).ToArray(),
				found,
				"The walk should stop at a .git directory rather than descend into it.");
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	/// <summary>
	/// The same regression against the real file system, for hosts where a read can actually be
	/// denied. Goes inconclusive rather than passing vacuously where it cannot be.
	/// </summary>
	[TestMethod]
	public void ADeniedDirectoryOnDiskIsSkipped()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("Denying a directory read on Windows needs an ACL edit rather than a mode change.");
			return;
		}

		string root = CreateTree();
		string denied = Path.Join(root, "nested");
		try
		{
			File.SetUnixFileMode(denied, UnixFileMode.None);

			try
			{
				_ = Directory.GetDirectories(denied);
				Assert.Inconclusive("This process reads a mode-000 directory anyway, most likely because it is root.");
			}
			catch (UnauthorizedAccessException)
			{
				// The mode took effect, so the walk is about to meet a genuinely unreadable directory.
			}

			CollectionAssert.AreEqual(
				new[] { Path.GetFullPath(Path.Join(root, "alpha", ".git")) },
				Walk(root),
				"The readable half of the tree should survive an unreadable directory on disk.");
		}
		finally
		{
			File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			Directory.Delete(root, recursive: true);
		}
	}
}
