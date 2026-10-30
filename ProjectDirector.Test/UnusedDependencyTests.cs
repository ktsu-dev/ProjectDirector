// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector.Test;

using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Pins the removal of the abandoned OpenAI feature and the second ImGui binding. Neither package was
/// used, but both were shipped, and the OpenAI token sat in the plain-text settings file.
/// </summary>
[TestClass]
public sealed class UnusedDependencyTests
{
	/// <summary>
	/// The settings file has no field for an OpenAI token, so no secret can be persisted beside UI state.
	/// </summary>
	[TestMethod]
	public void OptionsHaveNoOpenAITokenField() =>
		Assert.IsNull(typeof(ProjectDirectorOptions).GetProperty("OpenAIToken"));

	/// <summary>
	/// The output no longer carries the OpenAI SDK or ImGui.NET, whose native cimgui duplicates Hexa's.
	/// </summary>
	/// <param name="assembly">An assembly file that must not be shipped.</param>
	[TestMethod]
	[DataRow("OpenAI.dll")]
	[DataRow("ImGui.NET.dll")]
	public void UnusedPackageIsNotShipped(string assembly) =>
		Assert.IsFalse(File.Exists(Path.Combine(AppContext.BaseDirectory, assembly)), $"{assembly} is still in the output");
}
