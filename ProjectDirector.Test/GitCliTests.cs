// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.Collections.ObjectModel;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Guards the reason this application runs the git command line instead of binding libgit2.
/// </summary>
/// <remarks>
/// Git LFS is a pair of filters plus a set of hooks, and all of them belong to the git command. A
/// library reading and writing the object database directly bypasses them, so a clone lands pointer
/// files where the real content should be and a commit stores raw bytes where a pointer should be.
/// ProjectDirector clones, fetches and pulls, which is exactly the half of that the smudge filter
/// covers, so these tests pin the behaviour down rather than trusting it.
/// </remarks>
[TestClass]
public sealed class GitCliTests
{
	private const string LfsPointerPrefix = "version https://git-lfs.github.com/spec/v1";

	private static bool IsLfsAvailable() => GitCli.Run("lfs", "version").Succeeded;

	private static string CreateRepository(bool trackBinariesWithLfs)
	{
		string root = Path.Combine(Path.GetTempPath(), $"ktsu_pd_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(root);

		Assert.IsTrue(GitCli.Run("init", root).Succeeded, "git init failed.");

		// Scope identity to this throwaway repository so the test neither depends on nor disturbs
		// whatever global configuration the machine happens to carry.
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.name", "ProjectDirector").Succeeded);
		Assert.IsTrue(GitCli.RunIn(root, "config", "user.email", "ProjectDirector@ktsu.dev").Succeeded);

		if (trackBinariesWithLfs)
		{
			Assert.IsTrue(GitCli.RunIn(root, "lfs", "install", "--local").Succeeded, "git lfs install failed.");
			File.WriteAllText(Path.Combine(root, ".gitattributes"), "*.bin filter=lfs diff=lfs merge=lfs -text\n");
		}

		return root;
	}

	private static void CommitAll(string root, string message)
	{
		Assert.IsTrue(GitCli.RunIn(root, "add", "--all").Succeeded, "git add failed.");

		GitResult committed = GitCli.RunIn(root, "commit", "-m", message);
		Assert.IsTrue(committed.Succeeded, $"git commit failed: {committed.FailureText}");
	}

	[TestMethod]
	public void CloningAnLfsRepositoryRestoresTheFileContentRatherThanThePointer()
	{
		if (!IsLfsAvailable())
		{
			Assert.Inconclusive("git-lfs is not installed, so the filters cannot run.");
			return;
		}

		string origin = CreateRepository(trackBinariesWithLfs: true);
		string clone = Path.Combine(Path.GetTempPath(), $"ktsu_pd_clone_{Guid.NewGuid():N}");

		try
		{
			// Bytes that are unmistakably not text, so a pointer left in their place is obvious.
			byte[] payload = new byte[2048];
			for (int i = 0; i < payload.Length; i++)
			{
				payload[i] = (byte)(i % 256);
			}

			File.WriteAllBytes(Path.Combine(origin, "asset.bin"), payload);
			CommitAll(origin, "Add asset.bin");

			// The committed object must be a pointer, which is the clean filter having run.
			GitResult blob = GitCli.RunIn(origin, "cat-file", "-p", "HEAD:asset.bin");
			Assert.IsTrue(blob.Succeeded, $"git cat-file failed: {blob.FailureText}");
			Assert.StartsWith(LfsPointerPrefix, blob.OutputText, "The committed blob should be an LFS pointer, not the file's bytes.");

			GitResult cloned = GitCli.Run("clone", origin, clone);
			Assert.IsTrue(cloned.Succeeded, $"git clone failed: {cloned.FailureText}");

			// And the checked-out file must be the content again, which is the smudge filter
			// having run. This is the half libgit2 could not do: a clone through it lands the
			// pointer text on disk in place of the file.
			byte[] checkedOut = File.ReadAllBytes(Path.Combine(clone, "asset.bin"));
			CollectionAssert.AreEqual(payload, checkedOut, "The clone should contain the file, not its LFS pointer.");
		}
		finally
		{
			TryDeleteDirectory(origin);
			TryDeleteDirectory(clone);
		}
	}

	[TestMethod]
	public void AFileOutsideAnyLfsPatternIsStoredVerbatim()
	{
		if (!IsLfsAvailable())
		{
			Assert.Inconclusive("git-lfs is not installed, so the filters cannot run.");
			return;
		}

		string root = CreateRepository(trackBinariesWithLfs: true);

		try
		{
			// The pattern covers *.bin only. Without this half of the pair, a runner that turned
			// everything into a pointer would still pass the test above.
			File.WriteAllText(Path.Combine(root, "notes.txt"), "plain content\n");
			CommitAll(root, "Add notes.txt");

			GitResult blob = GitCli.RunIn(root, "cat-file", "-p", "HEAD:notes.txt");

			Assert.IsTrue(blob.Succeeded, $"git cat-file failed: {blob.FailureText}");
			Assert.AreEqual("plain content", blob.OutputText);
		}
		finally
		{
			TryDeleteDirectory(root);
		}
	}

	[TestMethod]
	public void RepositoryDetectionDistinguishesAWorkingTreeFromAPlainDirectory()
	{
		string root = CreateRepository(trackBinariesWithLfs: false);
		string outside = Path.Combine(Path.GetTempPath(), $"ktsu_pd_norepo_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(outside);

		try
		{
			Assert.IsTrue(GitCli.IsRepository(root));
			Assert.IsFalse(GitCli.IsRepository(outside));
			Assert.IsFalse(GitCli.IsRepository(Path.Combine(outside, "does-not-exist")));
			Assert.IsFalse(GitCli.IsRepository(string.Empty));
		}
		finally
		{
			TryDeleteDirectory(root);
			TryDeleteDirectory(outside);
		}
	}

	[TestMethod]
	public void TrackedFilesAreListedWithForwardSlashesAndSurviveSpacesInPaths()
	{
		string root = CreateRepository(trackBinariesWithLfs: false);

		try
		{
			string nested = Path.Combine(root, "a directory with spaces");
			_ = Directory.CreateDirectory(nested);
			File.WriteAllText(Path.Combine(nested, "a file with spaces.txt"), "content\n");
			File.WriteAllText(Path.Combine(root, "root.txt"), "content\n");
			CommitAll(root, "Add files");

			Collection<string> tracked = GitCli.ListTrackedFiles(root);

			// Paths arrive exactly as git records them, which is what the diff view then joins onto
			// each repository root. Passing arguments as a list is what keeps the spaces intact.
			Assert.Contains("root.txt", tracked);
			Assert.Contains("a directory with spaces/a file with spaces.txt", tracked);
		}
		finally
		{
			TryDeleteDirectory(root);
		}
	}

	[TestMethod]
	public void TrackedFilesAreEmptyOutsideARepository()
	{
		string outside = Path.Combine(Path.GetTempPath(), $"ktsu_pd_norepo_{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(outside);

		try
		{
			Assert.IsEmpty(GitCli.ListTrackedFiles(outside));
		}
		finally
		{
			TryDeleteDirectory(outside);
		}
	}

	[TestMethod]
	public void UncommittedChangesAreDetected()
	{
		string root = CreateRepository(trackBinariesWithLfs: false);

		try
		{
			File.WriteAllText(Path.Combine(root, "notes.txt"), "content\n");
			CommitAll(root, "Add notes.txt");

			Assert.IsFalse(GitCli.HasUncommittedChanges(root), "A freshly committed tree should be clean.");

			File.WriteAllText(Path.Combine(root, "notes.txt"), "changed\n");

			Assert.IsTrue(GitCli.HasUncommittedChanges(root));
		}
		finally
		{
			TryDeleteDirectory(root);
		}
	}

	[TestMethod]
	public void RemoteUrlIsReadBackAndAbsentRemotesReportEmpty()
	{
		string root = CreateRepository(trackBinariesWithLfs: false);

		try
		{
			Assert.IsEmpty(GitCli.GetRemoteUrl(root, "origin"));

			Assert.IsTrue(GitCli.RunIn(root, "remote", "add", "origin", "https://github.com/ktsu-dev/ProjectDirector.git").Succeeded);

			Assert.AreEqual("https://github.com/ktsu-dev/ProjectDirector.git", GitCli.GetRemoteUrl(root, "origin"));
			Assert.IsEmpty(GitCli.GetRemoteUrl(root, "upstream"));
		}
		finally
		{
			TryDeleteDirectory(root);
		}
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
			// Covers a missing directory too. A best-effort cleanup of a temp directory is not
			// worth failing a test over.
		}
		catch (UnauthorizedAccessException)
		{
			// As above.
		}
	}
}
