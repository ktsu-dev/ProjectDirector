// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using ktsu.Semantics.Paths;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the path handling behind the repo and compare browsers below the top level.
/// </summary>
/// <remarks>
/// Entries used to be combined with the browse path a second time, so opening <c>src</c> looked for
/// <c>src/src/Inner</c>: folders showed as files, navigation stopped at the first level, Give on a
/// file threw on the render thread, and Give on a folder created a stray <c>src/src/...</c> in the
/// other repository. Folders were also recognised by a trailing separator that
/// <see cref="RelativePath"/> strips, so the compare browser called <see cref="File.Copy(string, string)"/>
/// and <see cref="File.Delete(string)"/> on folders.
/// </remarks>
[TestClass]
public sealed class RepoBrowsingTests
{
	private string _workspace = string.Empty;
	private string _repoA = string.Empty;
	private string _repoB = string.Empty;

	[TestInitialize]
	public void CreateRepos()
	{
		_workspace = Path.Join(Path.GetTempPath(), $"ktsu_pd_browse_{Guid.NewGuid():N}");
		_repoA = Path.Join(_workspace, "A");
		_repoB = Path.Join(_workspace, "B");

		_ = Directory.CreateDirectory(Path.Join(_repoA, "src", "Inner"));
		File.WriteAllText(Path.Join(_repoA, "src", "a.cs"), "class A { }\n");
		_ = Directory.CreateDirectory(Path.Join(_repoA, "only"));
		_ = Directory.CreateDirectory(Path.Join(_repoB, "src"));
	}

	[TestCleanup]
	public void DeleteRepos()
	{
		try
		{
			Directory.Delete(_workspace, recursive: true);
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing an otherwise passing test over.
		}
	}

	private static RelativePath Entry(string path) => RelativePath.Create<RelativePath>(path);

	[TestMethod]
	public void List_ANestedFolder_ReturnsEntriesRelativeToTheRepoRoot()
	{
		Collection<RelativePath> entries = RepoBrowsing.List(_repoA, "src");

		CollectionAssert.AreEquivalent(
			new[] { Entry(Path.Join("src", "Inner")), Entry(Path.Join("src", "a.cs")) },
			entries.ToArray());
	}

	[TestMethod]
	public void List_AFolderTheRepoDoesNotHave_ReturnsNothing()
	{
		Assert.IsEmpty(RepoBrowsing.List(_repoB, "only"));
	}

	[TestMethod]
	public void IsDirectory_ANestedFolder_IsAFolder()
	{
		Collection<RelativePath> entries = RepoBrowsing.List(_repoA, "src");

		RelativePath[] directories = [.. entries.Where(entry => RepoBrowsing.IsDirectory(entry, _repoA, _repoB))];

		CollectionAssert.AreEqual(new[] { Entry(Path.Join("src", "Inner")) }, directories);
	}

	[TestMethod]
	public void IsDirectory_AFolderInOnlyOneRepo_IsAFolder()
	{
		Assert.IsTrue(RepoBrowsing.IsDirectory(Entry("only"), _repoB, _repoA));
	}

	[TestMethod]
	public void Navigating_IntoANestedFolder_ListsItsContents()
	{
		File.WriteAllText(Path.Join(_repoA, "src", "Inner", "b.cs"), "class B { }\n");
		RelativePath inner = Entry(Path.Join("src", "Inner"));

		Collection<RelativePath> entries = RepoBrowsing.List(_repoA, inner.WeakString);

		CollectionAssert.AreEqual(new[] { Entry(Path.Join("src", "Inner", "b.cs")) }, entries.ToArray());
	}

	[TestMethod]
	public void Copy_ANestedFile_LandsAtTheSamePathInTheOtherRepo()
	{
		string? failure = RepoBrowsing.Copy(Entry(Path.Join("src", "a.cs")), _repoA, _repoB);

		Assert.IsNull(failure);
		Assert.IsTrue(File.Exists(Path.Join(_repoB, "src", "a.cs")));
		Assert.IsFalse(Directory.Exists(Path.Join(_repoB, "src", "src")));
	}

	[TestMethod]
	public void Copy_ANestedFolder_CreatesItWithoutAStrayParent()
	{
		string? failure = RepoBrowsing.Copy(Entry(Path.Join("src", "Inner")), _repoA, _repoB);

		Assert.IsNull(failure);
		Assert.IsTrue(Directory.Exists(Path.Join(_repoB, "src", "Inner")));
		Assert.IsFalse(Directory.Exists(Path.Join(_repoB, "src", "src")));
	}

	[TestMethod]
	public void Copy_AFolderInOnlyOneRepo_CreatesTheFolderRatherThanThrowing()
	{
		string? failure = RepoBrowsing.Copy(Entry("only"), _repoA, _repoB);

		Assert.IsNull(failure);
		Assert.IsTrue(Directory.Exists(Path.Join(_repoB, "only")));
	}

	[TestMethod]
	public void Copy_ASourceThatIsGone_ReportsTheFailureRatherThanThrowing()
	{
		string? failure = RepoBrowsing.Copy(Entry(Path.Join("src", "missing.cs")), _repoA, _repoB);

		Assert.IsNotNull(failure);
	}

	[TestMethod]
	public void Delete_AFolder_RemovesItRatherThanThrowing()
	{
		string? failure = RepoBrowsing.Delete(Entry("only"), _repoA);

		Assert.IsNull(failure);
		Assert.IsFalse(Directory.Exists(Path.Join(_repoA, "only")));
	}

	[TestMethod]
	public void Delete_ANestedFile_RemovesOnlyThatFile()
	{
		string? failure = RepoBrowsing.Delete(Entry(Path.Join("src", "a.cs")), _repoA);

		Assert.IsNull(failure);
		Assert.IsFalse(File.Exists(Path.Join(_repoA, "src", "a.cs")));
		Assert.IsTrue(Directory.Exists(Path.Join(_repoA, "src", "Inner")));
	}

	[TestMethod]
	public void Delete_AFolderWithContents_ReportsTheFailureAndKeepsIt()
	{
		string? failure = RepoBrowsing.Delete(Entry("src"), _repoA);

		Assert.IsNotNull(failure);
		Assert.IsTrue(File.Exists(Path.Join(_repoA, "src", "a.cs")));
	}
}
