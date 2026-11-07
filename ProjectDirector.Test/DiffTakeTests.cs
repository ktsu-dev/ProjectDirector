// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.IO;
using System.Linq;
using System.Text;

using DiffPlex;
using DiffPlex.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests that taking one block in the file-diff view changes only that block's bytes.
/// </summary>
/// <remarks>
/// The take arrows used to rebuild the destination with <see cref="Environment.NewLine"/> and write it
/// as UTF-8 without a BOM, so taking one line rewrote every line ending in the file and dropped its
/// byte order mark. These drive <see cref="DiffTake"/> the way the view does: read both files as text,
/// diff them with DiffPlex, take a block, and write the result back over real files on disk.
/// </remarks>
[TestClass]
public sealed class DiffTakeTests
{
	private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

	private static string CreateWorkspace()
	{
		string root = Path.Join(Path.GetTempPath(), $"ktsu_pd_take_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);
		return root;
	}

	private static void Cleanup(string root)
	{
		try
		{
			Directory.Delete(root, recursive: true);
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing an otherwise passing test over.
		}
		catch (UnauthorizedAccessException)
		{
			// Same.
		}
	}

	private static byte[] Bytes(string text, bool bom = false) =>
		[.. bom ? Utf8Bom : [], .. Encoding.UTF8.GetBytes(text)];

	private static DiffResult Diff(string oldPath, string newPath) =>
		Differ.Instance.CreateLineDiffs(File.ReadAllText(oldPath), File.ReadAllText(newPath), ignoreWhitespace: false, ignoreCase: false);

	[TestMethod]
	public void TakingARightBlockKeepsTheLeftFilesCrLfAndBom()
	{
		string root = CreateWorkspace();
		try
		{
			string left = Path.Join(root, "left.txt");
			string right = Path.Join(root, "right.txt");
			File.WriteAllBytes(left, Bytes("one\r\ntwo\r\nthree\r\n", bom: true));
			File.WriteAllBytes(right, Bytes("one\r\nTWO\r\nthree\r\n"));

			DiffResult diff = Diff(left, right);
			Assert.IsNull(DiffTake.WriteLinesPreservingFormat(left, DiffTake.TakeNewIntoOld(diff, diff.DiffBlocks.Single())));

			CollectionAssert.AreEqual(Bytes("one\r\nTWO\r\nthree\r\n", bom: true), File.ReadAllBytes(left));
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void TakingALeftBlockKeepsTheRightFilesCrLfWithoutAddingABom()
	{
		string root = CreateWorkspace();
		try
		{
			string left = Path.Join(root, "left.txt");
			string right = Path.Join(root, "right.txt");
			File.WriteAllBytes(left, Bytes("one\ntwo\nthree\n", bom: true));
			File.WriteAllBytes(right, Bytes("one\r\nTWO\r\nthree\r\n"));

			DiffResult diff = Diff(left, right);
			Assert.IsNull(DiffTake.WriteLinesPreservingFormat(right, DiffTake.TakeOldIntoNew(diff, diff.DiffBlocks.Single())));

			CollectionAssert.AreEqual(Bytes("one\r\ntwo\r\nthree\r\n"), File.ReadAllBytes(right));
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void TakingABlockKeepsAnLfFileLf()
	{
		string root = CreateWorkspace();
		try
		{
			string left = Path.Join(root, "left.txt");
			string right = Path.Join(root, "right.txt");
			File.WriteAllBytes(left, Bytes("one\ntwo\nthree\n"));
			File.WriteAllBytes(right, Bytes("one\r\nTWO\r\nthree\r\n"));

			DiffResult diff = Diff(left, right);
			Assert.IsNull(DiffTake.WriteLinesPreservingFormat(left, DiffTake.TakeNewIntoOld(diff, diff.DiffBlocks.Single())));

			CollectionAssert.AreEqual(Bytes("one\nTWO\nthree\n"), File.ReadAllBytes(left));
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ATakeIntoAPathThatIsAFolderIsDescribedForTheLogRatherThanThrown()
	{
		string root = CreateWorkspace();
		try
		{
			string folder = Path.Join(root, "file.txt");
			_ = Directory.CreateDirectory(folder);
			DateTimeOffset at = new(2026, 11, 7, 2, 30, 0, TimeSpan.Zero);

			string line = DiffTake.Take(folder, ["one"], at);

			StringAssert.StartsWith(line, $"[{at}] Taking a block into {folder} failed: ");
			Assert.IsTrue(Directory.Exists(folder), "The folder in the way should be left alone.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	public void ASuccessfulTakeLogsNothing()
	{
		string root = CreateWorkspace();
		try
		{
			string file = Path.Join(root, "file.txt");
			File.WriteAllText(file, "one\n");

			Assert.AreEqual(string.Empty, DiffTake.Take(file, ["ONE", string.Empty], DateTimeOffset.Now));
			Assert.AreEqual("ONE\n", File.ReadAllText(file));
		}
		finally
		{
			Cleanup(root);
		}
	}

	/// <summary>
	/// A repository that tracks <c>AGENTS.md</c> as a link to <c>CLAUDE.md</c>: taking a block into
	/// <c>AGENTS.md</c> used to write through the link and overwrite <c>CLAUDE.md</c>. It is refused,
	/// and the link and its target are both left as they were.
	/// </summary>
	[TestMethod]
	public void TakingABlockIntoASymlinkedFileIsRefusedAndLeavesTheLinkAndItsTarget()
	{
		string root = CreateWorkspace();
		try
		{
			string left = Path.Join(root, "left.md");
			string target = Path.Join(root, "CLAUDE.md");
			string link = Path.Join(root, "AGENTS.md");
			File.WriteAllText(left, "one\nTWO\nthree\n");
			File.WriteAllText(target, "one\ntwo\nthree\n");
			_ = File.CreateSymbolicLink(link, "CLAUDE.md");

			DiffResult diff = Diff(left, link);
			string? failure = DiffTake.WriteLinesPreservingFormat(link, DiffTake.TakeOldIntoNew(diff, diff.DiffBlocks.Single()));

			Assert.IsNotNull(failure);
			StringAssert.Contains(failure, "symbolic link");
			Assert.AreEqual("one\ntwo\nthree\n", File.ReadAllText(target), "The link's target should not have been written through.");
			Assert.AreEqual("CLAUDE.md", new FileInfo(link).LinkTarget, "The link itself should still be there.");
		}
		finally
		{
			Cleanup(root);
		}
	}

	[TestMethod]
	[DataRow("one\r\ntwo\n", "\r\n")]
	[DataRow("one\ntwo\r\n", "\n")]
	[DataRow("one\rtwo", "\r")]
	[DataRow("one", null)]
	[DataRow("", null)]
	public void DetectNewLineReportsTheFirstLineBreak(string text, string? expected) =>
		Assert.AreEqual(expected, DiffTake.DetectNewLine(text));
}
