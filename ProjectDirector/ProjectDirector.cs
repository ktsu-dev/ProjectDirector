namespace ktsu.io.ProjectDirector;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;
using System.Text;
using DiffPlex;
using ImGuiNET;
using ktsu.io.Extensions;
using ktsu.io.ImGuiApp;
using ktsu.io.ImGuiWidgets;
using ktsu.io.StrongPaths;
using ktsu.io.TrivialWinForms;

[SupportedOSPlatform("windows")]
internal sealed class ProjectDirector
{
	internal ProjectDirectorOptions Options { get; } = new();
	private static float FieldWidth => ImGui.GetIO().DisplaySize.X * 0.15f;
	private DateTime LastSaveOptionsTime { get; set; } = DateTime.MinValue;
	private DateTime SaveOptionsQueuedTime { get; set; } = DateTime.MinValue;
	private TimeSpan SaveOptionsDebounceTime { get; } = TimeSpan.FromSeconds(3);
	private DividerContainer DividerContainerCols { get; }
	private DividerContainer DividerContainerRows { get; }
	private Octokit.GitHubClient GitHubClient { get; init; }
	private StringBuilder LogBuilder { get; } = new();

	private static void Main(string[] _)
	{
		ProjectDirector projectDirector = new();
		ImGuiApp.Start(nameof(ProjectDirector), new ImGuiAppWindowState(), projectDirector.Tick, projectDirector.ShowMenu, projectDirector.WindowResized);
	}

	public ProjectDirector()
	{
		Options = ProjectDirectorOptions.LoadOrCreate();

		DividerContainerCols = new("VerticalDivider", DividerResized, DividerLayout.Columns);
		DividerContainerRows = new("HorizontalDivider", DividerResized, DividerLayout.Rows);
		DividerContainerCols.Add("Left", 0.25f, ShowLeftPanel);
		DividerContainerCols.Add("Right", 0.75f, ShowRightPanel);
		DividerContainerRows.Add("Top", 0.80f, ShowTopPanel);
		DividerContainerRows.Add("Bottom", 0.20f, ShowBottomPanel);

		RestoreDividerStates();

		LibGit2Sharp.GlobalSettings.LogConfiguration = new(LibGit2Sharp.LogLevel.Debug, new((level, message) =>
		{
			string logMessage = $"[{level}] {message}";
			LogBuilder.AppendLine(logMessage);
		}));

		GitHubClient = new(new Octokit.ProductHeaderValue("ktsu.io.ProjectDirector"));

		if (!string.IsNullOrEmpty(Options.GitHubLogin) && !string.IsNullOrEmpty(Options.GitHubPAT))
		{
			GitHubClient.Credentials = new(Options.GitHubLogin, Options.GitHubPAT);
		}

		QueueSaveOptions();
	}

	private void WindowResized() => QueueSaveOptions();

	private void DividerResized(DividerContainer container)
	{
		Options.DividerStates[container.Id] = new(container.GetSizes());
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

	private void FetchReposIfRequired()
	{
		foreach (var (repoPath, repoName) in Options.ClonedRepos)
		{
			if (Options.Repos.TryGetValue(repoName, out var repo))
			{
				if (repo.MinFetchIntervalSeconds > 0 && (DateTime.Now - repo.LastFetchTime) > TimeSpan.FromSeconds(repo.MinFetchIntervalSeconds))
				{
					repo.LastFetchTime = DateTime.Now;
					QueueSaveOptions();

					var task = new Task(() =>
					{
						var localRepo = new LibGit2Sharp.Repository(repoPath);
						var fetchOptions = new LibGit2Sharp.FetchOptions();
						var origin = localRepo.Network.Remotes["origin"];
						var refSpecs = origin.FetchRefSpecs.Select(x => x.Specification);
						LibGit2Sharp.Commands.Fetch(localRepo, "origin", refSpecs, fetchOptions, $"Fetching {repoName}");
					});

					task.Start();
				}
			}
		}
	}

	private void Tick(float dt)
	{
		DividerContainerCols.Tick(dt);

		FetchReposIfRequired();
		SaveOptionsIfRequired();
	}

	private void ShowTopPanel(float dt)
	{
		if (Options.Repos.TryGetValue(Options.SelectedRepo, out var repo))
		{
			ImGui.TextUnformatted($"Selected Repo: {Options.SelectedRepo}");
			ImGui.TextUnformatted($"Local Path: {repo.LocalPath}");
			ImGui.TextUnformatted($"Remote Path: {repo.RemotePath}");

			ShowCollapsiblePanel($"Git Actions", () =>
			{
				if (!Options.ClonedRepos.ContainsValue(Options.SelectedRepo))
				{
					if (ImGui.Button("Clone", new Vector2(FieldWidth, 0)))
					{
						var task = new Task(() =>
						{
							LibGit2Sharp.Repository.Clone(repo.RemotePath, repo.LocalPath);
						});

						task.ContinueWith((t) =>
						{
							Options.ClonedRepos.Add(repo.LocalPath, Options.SelectedRepo);
							QueueSaveOptions();
						},
						new CancellationToken(),
						TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Current);

						task.Start();
					}
				}
				else
				{
					int fetchInterval = repo.MinFetchIntervalSeconds;
					if (Knob.Draw("Min Fetch Interval", ref fetchInterval, 0, 300, 150))
					{
						repo.MinFetchIntervalSeconds = fetchInterval;
					}

					ImGui.SameLine();

					int timeUntilFetch = fetchInterval - (int)(DateTime.Now - repo.LastFetchTime).TotalSeconds;
					Knob.Draw("Fetch In", ref timeUntilFetch, 0, 300, 150);

					if (ImGui.Button("Pull", new Vector2(FieldWidth, 0)))
					{
						// TODO: check if there are any uncommitted changes and warn the user
						//var task = new Task(() =>
						//{
						//	var localRepo = new LibGit2Sharp.Repository(repoPath);
						//	var fetchOptions = new LibGit2Sharp.FetchOptions();
						//	var origin = localRepo.Network.Remotes["origin"];
						//	var refSpecs = origin.FetchRefSpecs.Select(x => x.Specification);
						//	LibGit2Sharp.Commands.Pull(localRepo
						//});

						//task.Start();
					}
					ImGui.SameLine();
					ImGui.Button("Commit", new Vector2(FieldWidth, 0));
					ImGui.SameLine();
					ImGui.Button("Push", new Vector2(FieldWidth, 0));
				}
			});
		}
	}

	private void ShowBottomPanel(float dt)
	{
		ImGui.BeginChild("Log", new Vector2(0, 0), true, ImGuiWindowFlags.HorizontalScrollbar);
		ImGui.TextUnformatted(LogBuilder.ToString());
		ImGui.SetScrollHereY(1);
		ImGui.EndChild();
	}

	private void ShowLeftPanel(float dt)
	{
		ImGui.TextUnformatted($"Dev Dir: {Options.DevDirectory}");

		ShowOwners();
	}

	private void ShowRightPanel(float dt) => DividerContainerRows.Tick(dt);

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
			if (ImGui.MenuItem("Set Dev Directory"))
			{
				string devDirectory = TextPrompt.Show("Set Dev Directory?");
				if (!string.IsNullOrEmpty(devDirectory))
				{
					Options.DevDirectory = (AbsoluteDirectoryPath)devDirectory;
					QueueSaveOptions();
				}
			}

			if (ImGui.MenuItem("Explore Dev Directory", !string.IsNullOrEmpty(Options.DevDirectory)))
			{
				using Process p = new()
				{
					StartInfo = new()
					{
						FileName = "explorer.exe",
						Arguments = Options.DevDirectory,
					}
				};
				p.Start();
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Add New GitHub Owner"))
			{
				var ownerName = (GitHubOwnerName)TextPrompt.Show("New Owner Name?");
				if (!string.IsNullOrEmpty(ownerName))
				{
					Options.GitHubOwners.TryAdd(ownerName, (GitHubPAT)string.Empty);
					SyncGitHubOwnerInfo(ownerName);
				}
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Exit"))
			{
				ImGuiApp.Stop();
			}

			ImGui.EndMenu();
		}

		if (ImGui.BeginMenu("Scan"))
		{
			if (ImGui.MenuItem("Dev Dir"))
			{
				ScanDevDirectoryForOwnersAndRepos();
			}

			if (ImGui.MenuItem("GitHub Owners"))
			{
				ScanRemoteAccountsForRepos();
			}

			ImGui.EndMenu();
		}
	}

	private void ShowOwners()
	{
		foreach (var (owner, pat) in Options.GitHubOwners)
		{
			ShowCollapsiblePanel(owner, () => { ShowRepos(owner); });
		}
	}

	private void ShowRepos(GitHubOwnerName owner)
	{
		foreach (var (repoName, repo) in Options.Repos)
		{
			if (repo is GitHubRepository gitHubRepo && gitHubRepo.OwnerName == owner)
			{
				bool isChecked = Options.ClonedRepos.ContainsKey(repo.LocalPath);
				ImGui.Checkbox($"##cb{repoName}", ref isChecked);
				ImGui.SameLine();
				bool isSelected = Options.SelectedRepo == repoName;
				if (ImGui.Selectable(gitHubRepo.RepoName, ref isSelected))
				{
					Options.SelectedRepo = repoName;
					UpdateSimalarRepos(repo);
					QueueSaveOptions();
				}
			}
		}
	}

	private void SyncGitHubOwnerInfo(GitHubOwnerName owner)
	{
		var newOwner = GitHubClient.User.Get(owner).GetAwaiter().GetResult();
		Options.GitHubOwnerInfo[owner] = newOwner;
		SyncGitHubRepoInfoForOwner(owner);
		QueueSaveOptions();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
	private void SyncGitHubRepoInfoForOwner(GitHubOwnerName owner)
	{
		if (!Options.GitHubOwnerInfo.TryGetValue(owner, out var ownerInfo))
		{
			return;
		}

		try
		{
			IEnumerable<Octokit.Repository> remoteRepos = GitHubClient.Repository.GetAllForUser(owner).Result;

			if (ownerInfo.Type == Octokit.AccountType.Organization)
			{
				remoteRepos = remoteRepos.Concat(GitHubClient.Repository.GetAllForOrg(owner).Result);
			}

			foreach (var remoteRepo in remoteRepos)
			{
				var localPath = MakeFullyQualifyLocalRepoPath(Options.DevDirectory / (RelativeDirectoryPath)remoteRepo.FullName);
				var repoName = GetFullyQualifiedRepoName(remoteRepo);
				var repo = GitRepository.Create((GitRemotePath)remoteRepo.CloneUrl, localPath);
				if (repo is not null)
				{
					Options.Repos[repoName] = repo;
					if (repo is GitHubRepository gitHubRepo)
					{
						gitHubRepo.OwnerName = owner;
						gitHubRepo.RepoName = (GitHubRepoName)remoteRepo.Name;
					}
				}
			}

			QueueSaveOptions();
		}
		catch (Exception)
		{
		}
	}

	private void UpdateClonedStatus()
	{
		foreach (var (repoName, repoOptions) in Options.Repos)
		{
			var repoPath = repoOptions.LocalPath;
			if (Directory.Exists(repoPath))
			{
				Options.ClonedRepos[repoPath] = repoName;
			}
			else
			{
				Options.ClonedRepos.Remove(repoPath);
			}
		}

		QueueSaveOptions();
	}

	private static FullyQualifiedGitHubRepoName GetFullyQualifiedRepoName(Octokit.Repository repo) => (FullyQualifiedGitHubRepoName)repo.FullName.Replace('/', '.');
	private static FullyQualifiedGitHubRepoName GetFullyQualifiedRepoName(GitHubOwnerName ownerName, GitHubRepoName repoName) => (FullyQualifiedGitHubRepoName)$"{ownerName}.{repoName}";

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "<Pending>")]
	private void ScanDevDirectoryForOwnersAndRepos()
	{
		// scan the dev directory for git repos and when we find one we add the owner to the list of owners and the repo to the list of repos
		var gitDirs = Directory.EnumerateDirectories(Options.DevDirectory, ".git", SearchOption.AllDirectories);
		foreach (string gitDir in gitDirs)
		{
			using var localRepo = new LibGit2Sharp.Repository(gitDir);
			var localPath = MakeFullyQualifyLocalRepoPath((AbsoluteDirectoryPath)localRepo.Info.WorkingDirectory);
			var remoteUrl = (GitRemotePath)localRepo.Network.Remotes["origin"].Url;

			try
			{
				var repo = GitRepository.Create(remoteUrl, localPath);
				if (repo is GitHubRepository gitHubRepo)
				{
					string[] remoteUrlParts = remoteUrl.Split('/').Reverse().ToArray();
					if (remoteUrlParts.Length >= 2)
					{
						var repoName = (GitHubRepoName)remoteUrlParts[0].RemoveSuffix(".git");
						var ownerName = (GitHubOwnerName)remoteUrlParts[1];
						var repoFullName = GetFullyQualifiedRepoName(ownerName, repoName);
						Options.Repos[repoFullName] = gitHubRepo;
						gitHubRepo.OwnerName = ownerName;
						gitHubRepo.RepoName = repoName;
						Options.GitHubOwners.TryAdd(gitHubRepo.OwnerName, (GitHubPAT)string.Empty);
					}
				}
			}
			catch (NotSupportedException)
			{
				continue;
			}
		}

		UpdateClonedStatus();
	}

	private void ScanRemoteAccountsForRepos()
	{
		var knownOwners = Options.GitHubOwners;
		foreach (var (owner, pat) in knownOwners)
		{
			GitHubClient.Credentials = !string.IsNullOrEmpty(pat) ? new(owner, pat) : new(Options.GitHubLogin, Options.GitHubPAT);

			SyncGitHubOwnerInfo(owner);
		}

		UpdateClonedStatus();
	}

	private static FullyQualifiedLocalRepoPath MakeFullyQualifyLocalRepoPath(AbsoluteDirectoryPath localPath) => (FullyQualifiedLocalRepoPath)Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private Dictionary<FullyQualifiedLocalRepoPath, Dictionary<string, int>> FindSimilarRepos(GitRepository repo)
	{
		var similarRepos = new Dictionary<FullyQualifiedLocalRepoPath, Dictionary<string, int>>();

		try
		{
			using var gitRepo = new LibGit2Sharp.Repository(repo.LocalPath);
			var fileList = gitRepo.Index.Select(x => x.Path);

			foreach (var (_, otherRepo) in Options.Repos)
			{
				if (repo != otherRepo)
				{
					try
					{
						using var otherGitRepo = new LibGit2Sharp.Repository(otherRepo.LocalPath);
						var otherFileList = otherGitRepo.Index.Select(x => x.Path);
						var matches = fileList.Intersect(otherFileList).ToCollection();
						var fileContents = matches.ToDictionary(x => x, x => File.ReadAllText(Path.Combine(repo.LocalPath, x)));
						var otherFileContents = matches.ToDictionary(x => x, x => File.ReadAllText(Path.Combine(otherRepo.LocalPath, x)));
						similarRepos.Remove(otherRepo.LocalPath);
						foreach (string match in matches)
						{
							var diff = Differ.Instance.CreateLineDiffs(fileContents[match], otherFileContents[match], ignoreWhitespace: false, ignoreCase: false);
							int linesDifferent = diff.DiffBlocks.Sum(x => x.DeleteCountA + x.InsertCountB);
							similarRepos[otherRepo.LocalPath][match] = linesDifferent;
						}
					}
					catch (LibGit2Sharp.RepositoryNotFoundException)
					{
					}
				}
			}
		}
		catch (LibGit2Sharp.RepositoryNotFoundException)
		{
		}

		return similarRepos;
	}

	private void UpdateSimalarRepos(GitRepository repo) => repo.SimilarRepos = FindSimilarRepos(repo);

	//private void ShowSimilarRepos(GitRepository repo)
	//{
	//	if (Options.Repos.TryGetValue(repo.LocalPath, out var otherRepo))
	//	{
	//		var similarRepos = FindSimilarRepos(repo);
	//		foreach (var (otherRepoPath, matches) in similarRepos)
	//		{
	//			ImGui.TextUnformatted($"Similar Files in {otherRepoPath}");
	//			foreach (var match in matches)
	//			{
	//				ImGui.TextUnformatted(match);
	//			}
	//		}
	//	}
	//}
}
