// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.UITests;

using System.IO.Abstractions.TestingHelpers;
using System.Numerics;

using ktsu.AppDataStorage;
using ktsu.ImGui.App;

using Silk.NET.Windowing;

/// <summary>
/// The window's size, position and layout, which the application saves whenever the window moves
/// or resizes, and starts the next run with.
/// </summary>
[TestClass]
public sealed class WindowStateTests
{
	[TestMethod]
	public void BuildConfig_StartsTheWindowWhereTheSavedOptionsLeftIt()
	{
		AppData.ConfigureForTesting(() => new MockFileSystem());
		try
		{
			ImGuiAppWindowState saved = new()
			{
				Size = new Vector2(1600, 1000),
				Pos = new Vector2(2000, 150),
				LayoutState = WindowState.Maximized,
			};
			Assert.AreNotEqual(saved.LayoutState, new ImGuiAppWindowState().LayoutState, "The default layout is already maximized, so this would prove nothing.");

			using ProjectDirectorOptions options = new() { WindowState = saved };
			ImGuiAppConfig config = new ProjectDirector(options).BuildConfig();

			Assert.AreEqual(saved.Size, config.InitialWindowState.Size);
			Assert.AreEqual(saved.Pos, config.InitialWindowState.Pos);
			Assert.AreEqual(saved.LayoutState, config.InitialWindowState.LayoutState);
		}
		finally
		{
			AppData.ResetFileSystem();
		}
	}
}
