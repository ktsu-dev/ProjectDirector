namespace ktsu.ProjectDirector;

using DiffPlex.Model;
using ImGuiNET;
using ktsu.Extensions;
using ktsu.ImGuiPopups;
using ktsu.StrongPaths;

internal class PopupPropagateFile
{
	private ImGuiPopups.Modal Modal { get; } = new();
	private ProjectDirectorOptions Options { get; set; } = new();
	private Dictionary<FullyQualifiedGitHubRepoName, bool> Propagation { get; } = [];
	private ImGuiPopups.Prompt Prompt { get; } = new();
	private bool ShouldClose { get; set; }

	public void Open(ProjectDirectorOptions options)
	{
		ShouldClose = false;
		Options = options;
		Propagation.Clear();
		Modal.Open("Propagate File", ShowContent);
	}

	protected void ShowContent()
	{
		string normalizePath(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		bool hasSimilarFile(KeyValuePair<FullyQualifiedGitHubRepoName, Dictionary<RelativeFilePath, DiffResult>> kvp) => kvp.Value.Any(x => normalizePath(x.Key) == normalizePath(Options.PropagatePath));

		var repo = Options.Repos[Options.BaseRepo];

		var sortedRepos = repo.SimilarRepoDiffs
			.ToDictionary(kvp => kvp.Key, hasSimilarFile)
			.OrderByDescending(kvp => kvp.Value)
			.ThenBy(kvp => kvp.Key);

		ImGui.TextUnformatted("Propagate file to other repos");
		ImGui.Separator();
		ImGui.TextUnformatted($"From: {Options.BaseRepo}");
		ImGui.TextUnformatted($"Path: {Options.PropagatePath}");
		ImGui.Separator();
		ImGui.TextUnformatted("Repos to propagate to:");
		foreach (var (name, similar) in sortedRepos)
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
		var repo = Options.Repos[Options.BaseRepo];
		string from = Path.Combine(repo.LocalPath, Options.PropagatePath);
		foreach (var (name, shouldPropagate) in Propagation)
		{
			if (shouldPropagate)
			{
				var otherRepo = Options.Repos[name];
				string to = Path.Combine(otherRepo.LocalPath, Options.PropagatePath);
				string? directory = Path.GetDirectoryName(to);
				if (!string.IsNullOrEmpty(directory))
				{
					_ = Directory.CreateDirectory(directory);
					File.Copy(from, to, overwrite: true);
				}
			}
		}

		ShouldClose = true;
	}

	/// <summary>
	/// Show the modal if it is open.
	/// </summary>
	/// <returns>True if the modal is open.</returns>
	public bool ShowIfOpen() => Modal.ShowIfOpen();
}
