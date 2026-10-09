// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.UITests.Gallery;

using System.IO.Abstractions.TestingHelpers;

using ktsu.AppDataStorage;
using ktsu.CredentialCache.Storage;
using ktsu.ImGui.App.Testing;

using CredentialCache = ktsu.CredentialCache.CredentialCache;

/// <summary>
/// The real application, started headlessly over the seeded repositories with its settings file
/// and secret store replaced by in-memory ones.
/// </summary>
/// <remarks>
/// The application saves its settings as it starts and reads its GitHub token from the OS secret
/// store. Both are redirected for the lifetime of this object, so a gallery run never reads or
/// writes the settings or credentials of whoever runs it.
/// </remarks>
internal sealed class GalleryApp : IDisposable
{
	private readonly CredentialCache credentials;

	private GalleryApp(ImGuiAppHarness harness, ProjectDirectorOptions options, CredentialCache credentials)
	{
		Harness = harness;
		Options = options;
		this.credentials = credentials;
	}

	/// <summary>Gets the harness driving the application.</summary>
	internal ImGuiAppHarness Harness { get; }

	/// <summary>Gets the settings the application is running on.</summary>
	internal ProjectDirectorOptions Options { get; }

	/// <summary>Starts the application at the given display size.</summary>
	/// <param name="display">The size of the window the application draws into.</param>
	/// <returns>The running application.</returns>
	internal static GalleryApp Start((int Width, int Height) display)
	{
		AppData.ConfigureForTesting(() => new MockFileSystem());
		CredentialCache credentials = new(new InMemoryCredentialStore());
		TokenStorage.UseCache(credentials);

		try
		{
			ProjectDirectorOptions options = GalleryRepositories.CreateOptions();
			ProjectDirector director = new(options);
			ImGuiAppHarness harness = ImGuiAppHarness.Start(director.BuildConfig(), new HarnessOptions
			{
				Width = display.Width,
				Height = display.Height,
			});

			return new(harness, options, credentials);
		}
		catch
		{
			Restore(credentials);
			throw;
		}
	}

	/// <summary>Selects a repository in the left panel and waits for its comparison to finish.</summary>
	/// <param name="repo">The repository's own name.</param>
	internal void SelectRepository(string repo)
	{
		Harness.Click($"repo/{repo}");
		GitRepository selected = Options.Repos[GalleryRepositories.FullName(repo)];

		// The comparison runs git on a background task, which publishes whenever it finishes rather
		// than after some number of frames, so the wait is on the task's own answer.
		Assert.IsTrue(
			SpinWait.SpinUntil(() => !selected.SimilarReposPending, TimeSpan.FromSeconds(60)),
			$"Comparing {repo} against its siblings did not finish.");
		Harness.Step(2);
	}

	/// <summary>Right-clicks a probe-marked item, which is what opens a context menu.</summary>
	/// <param name="name">The item's probe name.</param>
	internal void RightClick(string name)
	{
		Rectangle item = Harness.Probe.Rect(name) ?? throw new InvalidOperationException($"Nothing called {name} has been drawn.");
		Harness.Mouse.Click(item.MinX + (item.Width / 2f), item.MinY + (item.Height / 2f), 1);
		Harness.Step(2);
	}

	/// <summary>
	/// Gets everything above the log panel. The log prefixes each line with the wall-clock time it
	/// was written, so it can never be photographed the same way twice.
	/// </summary>
	/// <returns>The part of the window worth keeping.</returns>
	internal Rectangle AboveTheLog()
	{
		Rectangle log = Harness.Probe.Rect("Log") ?? throw new InvalidOperationException("The log panel has not been drawn.");
		return new Rectangle(0, 0, Harness.Options.Width, log.MinY - 1);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Harness.Dispose();
		Restore(credentials);
	}

	private static void Restore(CredentialCache credentials)
	{
		TokenStorage.UseCache(null);
		credentials.Dispose();
		AppData.ResetFileSystem();
	}
}
