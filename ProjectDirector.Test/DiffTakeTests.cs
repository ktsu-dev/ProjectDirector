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
			DiffTake.WriteLinesPreservingFormat(left, DiffTake.TakeNewIntoOld(diff, diff.DiffBlocks.Single()));

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
			DiffTake.WriteLinesPreservingFormat(right, DiffTake.TakeOldIntoNew(diff, diff.DiffBlocks.Single()));

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
			DiffTake.WriteLinesPreservingFormat(left, DiffTake.TakeNewIntoOld(diff, diff.DiffBlocks.Single()));

			CollectionAssert.AreEqual(Bytes("one\nTWO\nthree\n"), File.ReadAllBytes(left));
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
