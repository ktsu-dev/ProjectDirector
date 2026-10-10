// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Semantics.Paths;

/// <summary>
/// Covers how the repository browsers tell a directory from a file.
/// </summary>
/// <remarks>
/// The compare browser used to look for a trailing separator on each entry. The entries are
/// <see cref="RelativePath"/>s, which do not keep one, so it found no directories at all and listed
/// every directory among the files, where clicking it could not open it.
/// </remarks>
[TestClass]
public sealed class BrowserDirectoryTests
{
	private static readonly string[] ExpectedDirectories = ["docs", "src"];

	private string root = string.Empty;

	[TestInitialize]
	public void CreateRepositories()
	{
		root = Path.Combine(Path.GetTempPath(), $"browser-dirs-{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(Path.Combine(root, "A", "src"));
		_ = Directory.CreateDirectory(Path.Combine(root, "B", "docs"));
		File.WriteAllText(Path.Combine(root, "A", "README.md"), "a");
		File.WriteAllText(Path.Combine(root, "B", "README.md"), "b");
	}

	[TestCleanup]
	public void DeleteRepositories() => Directory.Delete(root, recursive: true);

	[TestMethod]
	public void ARelativePathDoesNotKeepItsTrailingSeparator()
	{
		RelativePath entry = RelativePath.Create<RelativePath>("src" + Path.DirectorySeparatorChar);

		Assert.IsFalse(entry.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal), "If this starts passing, the spelling of an entry says whether it is a directory again.");
	}

	[TestMethod]
	public void DirectoriesAreFoundInWhicheverRepositoryHasThem()
	{
		RelativePath[] entries = [Entry("docs"), Entry("README.md"), Entry("src")];

		Collection<RelativePath> directories = ProjectDirector.ListBrowserDirectories(entries, string.Empty, Path.Combine(root, "A"), Path.Combine(root, "B"));

		Assert.AreSequenceEqual(ExpectedDirectories, directories.Select(x => x.ToString()));
	}

	[TestMethod]
	public void AFileIsNeverADirectory()
	{
		RelativePath[] entries = [Entry("README.md")];

		Assert.IsEmpty(ProjectDirector.ListBrowserDirectories(entries, string.Empty, Path.Combine(root, "A")));
	}

	private static RelativePath Entry(string name) => RelativePath.Create<RelativePath>(name + (name.Contains('.', StringComparison.Ordinal) ? string.Empty : Path.DirectorySeparatorChar.ToString()));
}
