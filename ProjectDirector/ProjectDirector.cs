namespace ktsu.io.ProjectDirector;

using System.Numerics;
using System.Runtime.Versioning;
using ImGuiApp;
using ImGuiNET;
using StrongPaths;
using TrivialWinForms;

[SupportedOSPlatform("windows")]
internal sealed class ProjectDirector
{
	internal ProjectDirectorOptions Options { get; } = new();
	private static float FieldWidth => ImGui.GetIO().DisplaySize.X * 0.15f;
	private DateTime LastSaveOptionsTime { get; set; } = DateTime.MinValue;
	private DateTime SaveOptionsQueuedTime { get; set; } = DateTime.MinValue;
	private TimeSpan SaveOptionsDebounceTime { get; } = TimeSpan.FromSeconds(3);
	private DividerContainer DividerContainerCols { get; }
	private Octokit.GitHubClient GitHubClient { get; } = new(new Octokit.ProductHeaderValue("ktsu.io.ProjectDirector"));
	private static void Main(string[] _)
	{
		ProjectDirector projectDirector = new();
		ImGuiApp.Start(nameof(ProjectDirector), new ImGuiAppWindowState(), projectDirector.Tick, projectDirector.ShowMenu, projectDirector.WindowResized);
	}

	public ProjectDirector()
	{
		DividerContainerCols = new("RootDivider", DividerResized);
		Options = ProjectDirectorOptions.LoadOrCreate();
		RestoreOptions();
		RefreshOwners();
		DividerContainerCols.Add("Left", 0.25f, ShowLeftPanel);
		DividerContainerCols.Add("Right", 0.75f, ShowRightPanel);
	}

	private void RestoreOptions() => RestoreDividerStates();

	private void WindowResized() => QueueSaveOptions();

	private void DividerResized(DividerContainer container)
	{
		Options.DividerStates[container.Id] = container.GetSizes();
		QueueSaveOptions();
	}

	private void RestoreDividerStates()
	{
		if (Options.DividerStates.TryGetValue(DividerContainerCols.Id, out var sizes))
		{
			DividerContainerCols.SetSizesFromList(sizes);
		}
	}

	// Dont call this directly, call QueueSaveOptions instead so that we can debounce the saves and avoid saving multiple times per frame or multiple frames in a row
	private void SaveOptionsInternal() => Options.Save();

	private void QueueSaveOptions() => SaveOptionsQueuedTime = DateTime.Now;

	private void SaveOptionsIfRequired()
	{
		//debounce the save requests and avoid saving multiple times per frame or multiple frames in a row
		if ((SaveOptionsQueuedTime > LastSaveOptionsTime) && ((DateTime.Now - SaveOptionsQueuedTime) > SaveOptionsDebounceTime))
		{
			SaveOptionsInternal();
			LastSaveOptionsTime = DateTime.Now;
		}
	}

	private void Tick(float dt)
	{
		DividerContainerCols.Tick(dt);

		SaveOptionsIfRequired();
	}

	private void ShowLeftPanel(float dt)
	{
		if (ImGui.Button($"Dev Dir: {Options.DevDirectory}", new Vector2(FieldWidth, 0)))
		{
			string devDirectory = TextPrompt.Show("New dev directory?");
			if (!string.IsNullOrEmpty(devDirectory))
			{
				Options.DevDirectory = (AbsoluteDirectoryPath)devDirectory;
				QueueSaveOptions();
			}
		}

		if (ImGui.Button("Add Owner", new Vector2(FieldWidth, 0)))
		{
			var newName = (GitHubOwnerName)TextPrompt.Show("New Owner Name?");
			Options.SelectedGitHubOwners.Add(newName);
			RefreshOwner(newName);
		}

		ShowOwners();
	}

	private void ShowRightPanel(float dt)
	{
		ImGui.TextUnformatted($"Selected Repo: {Options.SelectedRepo}");

		ShowCollapsiblePanel($"Git Actions", () =>
		{
			if (!Options.ClonedGitHubRepos.Contains(Options.SelectedRepo))
			{
				if (ImGui.Button("Clone", new Vector2(FieldWidth, 0)))
				{
				}
			}
			else
			{
				if (ImGui.Button("Pull", new Vector2(FieldWidth, 0)))
				{
				}
				ImGui.SameLine();
				ImGui.Button("Commit", new Vector2(FieldWidth, 0));
				ImGui.SameLine();
				ImGui.Button("Push", new Vector2(FieldWidth, 0));
			}
		});
		ShowCollapsiblePanel($"4", () => { });
	}

	private void ShowCollapsiblePanel(string name, Action contentDelegate)
	{
		if (!Options.PanelStates.TryGetValue(name, out bool open))
		{
			open = true;
			Options.PanelStates[name] = open;
			QueueSaveOptions();
		}

		var flags = ImGuiTreeNodeFlags.None;
		if (open)
		{
			flags |= ImGuiTreeNodeFlags.DefaultOpen;
		}

		bool wasOpen = open;
		if (ImGui.CollapsingHeader(name, flags))
		{
			contentDelegate?.Invoke();
			open = true;
		}
		else
		{
			open = false;
		}

		if (open != wasOpen)
		{
			Options.PanelStates[name] = open;
			QueueSaveOptions();
		}
	}


	private void ShowMenu()
	{
		if (ImGui.BeginMenu("File"))
		{
			//if (ImGui.MenuItem("New"))
			//{
			//	New();
			//}

			//if (ImGui.MenuItem("Open"))
			//{
			//	Open();
			//}

			//if (ImGui.MenuItem("Save"))
			//{
			//	Save();
			//}

			//ImGui.Separator();

			//string schemaFilePath = CurrentSchema?.FilePath ?? "";
			//if (ImGui.MenuItem("Open Externally", !string.IsNullOrEmpty(schemaFilePath)))
			//{
			//	var p = new Process();
			//	p.StartInfo.FileName = $"explorer.exe";
			//	p.StartInfo.Arguments = schemaFilePath;
			//	p.Start();
			//}

			if (ImGui.MenuItem("Exit"))
			{

			}

			ImGui.EndMenu();
		}
	}

	private void ShowOwners()
	{
		foreach (var owner in Options.SelectedGitHubOwners)
		{
			ShowCollapsiblePanel(owner, () => { ShowRepos(owner); });
		}
	}

	private void ShowRepos(GitHubOwnerName owner)
	{
		if (Options.CachedGitHubRepos.TryGetValue(owner, out var repos))
		{
			foreach (var repo in repos)
			{
				var repoName = (GitHubRepoName)repo.FullName;
				bool isChecked = Options.ClonedGitHubRepos.Contains(repoName);
				ImGui.Checkbox($"##cb{repoName}", ref isChecked);
				ImGui.SameLine();
				bool isSelected = Options.SelectedRepo == repoName;
				if (ImGui.Selectable(repo.Name, ref isSelected))
				{
					Options.SelectedRepo = repoName;
					QueueSaveOptions();
				}
			}
		}
	}

	private void RefreshOwners()
	{
		var knownOwners = Options.CachedGitHubOwners.ToList();
		foreach (var (owner, _) in knownOwners)
		{
			RefreshOwner(owner);
		}
	}

	private void RefreshOwner(GitHubOwnerName owner)
	{
		var newUser = GitHubClient.User.Get(owner).GetAwaiter().GetResult();
		Options.CachedGitHubOwners[owner] = newUser;
		RefreshRepos(owner);
		QueueSaveOptions();
	}

	private void RefreshRepos(GitHubOwnerName owner)
	{
		var repos = GitHubClient.Repository.GetAllForUser(owner).GetAwaiter().GetResult();
		if (Options.CachedGitHubRepos.TryGetValue(owner, out var oldRepos))
		{
			oldRepos.Clear();
		}

		foreach (var repo in repos)
		{
			Options.CachedGitHubRepos.Add(owner, repo);

			var repoName = (GitHubRepoName)repo.FullName;
			var repoPath = Options.DevDirectory / (RelativeDirectoryPath)repo.FullName;

			if (Directory.Exists(repoPath))
			{
				Options.ClonedGitHubRepos.Add(repoName);
			}
			else
			{
				Options.ClonedGitHubRepos.Remove(repoName);
			}
		}

		QueueSaveOptions();
	}
}
