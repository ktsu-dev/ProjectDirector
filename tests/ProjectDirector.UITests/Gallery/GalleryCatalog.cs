// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.UITests.Gallery;

using ktsu.ImGui.App.Testing;

/// <summary>Every picture in the app gallery, in the order the index shows them.</summary>
/// <remarks>
/// Each stage starts from the seeded repositories listed and nothing selected, and drives the
/// application by the probe names its own draw code records. A new view earns a picture by adding an
/// entry here; nothing else needs to change, because the runner, the index and the workflow all read
/// this list.
/// </remarks>
internal static class GalleryCatalog
{
	/// <summary>Gets the entries.</summary>
	internal static IReadOnlyList<GalleryEntry> Entries { get; } =
	[
		new(
			"Selecting a repository",
			"The repositories under each GitHub owner are listed on the left, marked green where a clone exists in the dev directory. Selecting one shows where it lives, its git actions, its files, and its siblings ranked by how many files they share with it.",
			app => app.SelectRepository(GalleryRepositories.Alpha))
		{
			Crop = AboveTheLog,
		},
		new(
			"Comparing two repositories",
			"Picking a similar repository lists every shared file that differs between the two, with how many lines differ, beside a browser over both trees.",
			app =>
			{
				app.SelectRepository(GalleryRepositories.Alpha);
				app.Harness.Click($"similar/{GalleryRepositories.FullName(GalleryRepositories.Beta)}");
				app.Harness.Step(3);
			})
		{
			Crop = AboveTheLog,
		},
		new(
			"Comparing a file",
			"Picking a changed file shows the two copies side by side, and the arrows take a block from one side to the other.",
			app =>
			{
				app.SelectRepository(GalleryRepositories.Alpha);
				app.Harness.Click($"similar/{GalleryRepositories.FullName(GalleryRepositories.Beta)}");
				app.Harness.Step(3);
				app.Harness.Click($"changed/{GalleryRepositories.WidgetSource}");
				app.Harness.Step(3);
			})
		{
			Crop = AboveTheLog,
		},
		new(
			"Propagating a file",
			"Right-clicking a file in the browser offers to copy it into any of the sibling repositories that also have it.",
			app =>
			{
				app.SelectRepository(GalleryRepositories.Alpha);
				app.RightClick("browse/README.md");
				app.Harness.Click("browse-menu/Propagate");
				app.Harness.Step(3);
			})
		{
			Crop = AboveTheLog,
		},
		new(
			"Setting the dev directory",
			"The directory scanned for clones is set from the File menu.",
			app => OpenMenuItem(app, "menu/Set Dev Directory"))
		{
			Crop = AboveTheLog,
		},
		new(
			"Adding a GitHub owner",
			"Repositories are discovered by GitHub owner, a user or an organization, added from the File menu.",
			app => OpenMenuItem(app, "menu/Add New GitHub Owner"))
		{
			Crop = AboveTheLog,
		},
	];

	private static Rectangle? AboveTheLog(GalleryApp app) => app.AboveTheLog();

	/// <summary>Opens a popup from the File menu. The popup is only ever shown, never confirmed.</summary>
	private static void OpenMenuItem(GalleryApp app, string item)
	{
		app.Harness.Click("menu/File");
		app.Harness.Step(2);
		app.Harness.Click(item);
		app.Harness.Step(3);
	}
}
