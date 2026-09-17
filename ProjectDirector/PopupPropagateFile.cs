// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

using System.Collections.ObjectModel;
using DiffPlex.Model;
using Hexa.NET.ImGui;
using ktsu.Extensions;
using ktsu.ImGui.Popups;
using Semantics.Paths;

internal sealed class PopupPropagateFile
{
	private ImGuiPopups.Modal Modal { get; } = new();
	private ProjectDirectorOptions Options { get; set; } = new();
	private Dictionary<FullyQualifiedGitHubRepoName, bool> Propagation { get; } = [];
	private ImGuiPopups.Prompt Prompt { get; } = new();
	private bool ShouldClose { get; set; }

	/// <summary>
	/// Where this popup reports what propagating did, so a batch copy is accounted for in the log
	/// panel the same way every git action already is.
	/// </summary>
	private Action<string> Log { get; set; } = _ => { };

	public void Open(ProjectDirectorOptions options, Action<string> log)
	{
		ShouldClose = false;
		Options = options;
		Log = log;
		Propagation.Clear();
		Modal.Open("Propagate File", ShowContent);
	}

	private void ShowContent()
	{
		string normalizePath(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		bool hasSimilarFile(KeyValuePair<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> kvp) => kvp.Value.Any(x => normalizePath(x.Key) == normalizePath(Options.PropagatePath));

		GitRepository repo = Options.Repos[Options.BaseRepo];

		IOrderedEnumerable<KeyValuePair<FullyQualifiedGitHubRepoName, bool>> sortedRepos = repo.SimilarRepoDiffs
			.ToDictionary(kvp => kvp.Key, hasSimilarFile)
			.OrderByDescending(kvp => kvp.Value)
			.ThenBy(kvp => kvp.Key);

		ImGui.TextUnformatted("Propagate file to other repos");
		ImGui.Separator();
		ImGui.TextUnformatted($"From: {Options.BaseRepo}");
		ImGui.TextUnformatted($"Path: {Options.PropagatePath}");
		ImGui.Separator();
		ImGui.TextUnformatted("Repos to propagate to:");
		foreach ((FullyQualifiedGitHubRepoName name, bool similar) in sortedRepos)
		{
			bool shouldPropagate = Propagation.GetOrCreate(name, similar);
			_ = ImGui.Checkbox($"{name}{(similar ? "*" : string.Empty)}", ref shouldPropagate);
			Propagation[name] = shouldPropagate;
		}

		ImGui.Separator();
		if (ImGui.Button("Propagate"))
		{
			int propagationCount = Propagation.Count(kvp => kvp.Value);
			Prompt.Open("Propagation", $"Are you sure you want to propagate {Options.PropagatePath} to {propagationCount} repos?", new()
			{
				{ "Yes", Propagate },
				{ "NO", null }
			});
		}

		if (ShouldClose)
		{
			ImGui.CloseCurrentPopup();
		}

		_ = Prompt.ShowIfOpen();
	}

	private void Propagate()
	{
		GitRepository repo = Options.Repos[Options.BaseRepo];
		string from = Path.Combine(repo.LocalPath, Options.PropagatePath);

		Dictionary<FullyQualifiedGitHubRepoName, string> destinations = Propagation
			.Where(kvp => kvp.Value)
			.ToDictionary(kvp => kvp.Key, kvp => Path.Combine(Options.Repos[kvp.Key].LocalPath, Options.PropagatePath));

		Collection<string> lines = FilePropagation.Propagate(from, destinations).Summarize();

		// Timestamp the summary and leave the per-repository detail indented under it, which is the
		// shape QueueGitLog already gives the log panel for a git command and its output.
		Log($"[{DateTimeOffset.Now}] {lines[0]}");
		foreach (string line in lines.Skip(1))
		{
			Log(line);
		}

		ShouldClose = true;
	}

	/// <summary>
	/// Show the modal if it is open.
	/// </summary>
	/// <returns>True if the modal is open.</returns>
	public bool ShowIfOpen() => Modal.ShowIfOpen();
}
