namespace ktsu.io.ProjectDirector;

using System.ClientModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using DiffPlex;
using DiffPlex.Model;
using ImGuiNET;
using ktsu.io.Extensions;
using ktsu.io.ImGuiApp;
using ktsu.io.ImGuiWidgets;
using ktsu.io.StrongPaths;
using OpenAI.Chat;

internal sealed class ProjectDirector
{
	internal ProjectDirectorOptions Options { get; }
	private static float FieldWidth => ImGui.GetIO().DisplaySize.X * 0.15f;
	private DateTime LastSaveOptionsTime { get; set; } = DateTime.MinValue;
	private DateTime SaveOptionsQueuedTime { get; set; } = DateTime.MinValue;
	private TimeSpan SaveOptionsDebounceTime { get; } = TimeSpan.FromSeconds(3);
	private DividerContainer DividerContainerCols { get; }
	private DividerContainer DividerContainerRows { get; }
	private DividerContainer DividerDiff { get; }
	private Octokit.GitHubClient GitHubClient { get; init; }
	private StringBuilder LogBuilder { get; } = new();
	private PopupInputString PopupSetDevDirectory { get; } = new();
	private PopupInputString PopupAddNewGitHubOwner { get; } = new();
	private Collection<RelativePath> BrowserContentsBase { get; set; } = [];
	private Collection<RelativePath> BrowserContentsCompare { get; set; } = [];
	private PopupPropagateFile PopupPropagateFile { get; } = new();

	private ChatClient ChatClient { get; init; }

	private static void Main(string[] _)
	{
		ProjectDirector projectDirector = new();
		ImGuiApp.Start(nameof(ProjectDirector), projectDirector.Options.WindowState, null, projectDirector.Tick, projectDirector.ShowMenu, projectDirector.WindowResized);
	}

	public ProjectDirector()
	{
		Options = ProjectDirectorOptions.LoadOrCreate();
		Options.Save();
		ChatClient = new(model: "gpt-4o", new ApiKeyCredential(Options.OpenAIToken));
		DividerDiff = new("DiffDivider", DividerResized, DividerLayout.Columns);
		DividerContainerCols = new("VerticalDivider", DividerResized, DividerLayout.Columns);
		DividerContainerRows = new("HorizontalDivider", DividerResized, DividerLayout.Rows);
		DividerContainerCols.Add("Left", 0.25f, ShowLeftPanel);
		DividerContainerCols.Add("Right", 0.75f, ShowRightPanel);
		DividerContainerRows.Add("Top", 0.80f, ShowTopPanel);
		DividerContainerRows.Add("Bottom", 0.20f, ShowBottomPanel);
		DividerDiff.Add("Left", 0.50f, (dt) =>
		{
			var repoA = Options.Repos[Options.BaseRepo];
			var repoB = Options.Repos[Options.CompareRepo];
			var diff = repoA.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
			ShowDiffLeft(repoA, repoB, diff);
		});
		DividerDiff.Add("Right", 0.50f, (dt) =>
		{
			var repoA = Options.Repos[Options.BaseRepo];
			var repoB = Options.Repos[Options.CompareRepo];
			var diff = repoA.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
			ShowDiffRight(repoA, repoB, diff);
		});

		RestoreDividerStates();

		LibGit2Sharp.GlobalSettings.LogConfiguration = new(LibGit2Sharp.LogLevel.Debug, new((level, message) =>
		{
			string logMessage = $"[{level}] {message}";
			_ = LogBuilder.AppendLine(logMessage);
		}));

		GitHubClient = new(new Octokit.ProductHeaderValue("ktsu.io.ProjectDirector"));

		if (!string.IsNullOrEmpty(Options.GitHubLogin) && !string.IsNullOrEmpty(Options.GitHubToken))
		{
			GitHubClient.Credentials = new(Options.GitHubLogin, Options.GitHubToken);
		}

		RefreshPage();
	}

	private void WindowResized()
	{
		Options.WindowState = ImGuiApp.WindowState;
		QueueSaveOptions();
	}

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

		if (Options.DividerStates.TryGetValue(DividerContainerRows.Id, out sizes))
		{
			DividerContainerRows.SetSizesFromList(sizes);
		}

		if (Options.DividerStates.TryGetValue(DividerDiff.Id, out sizes))
		{
			DividerDiff.SetSizesFromList(sizes);
		}
	}

	// Don't call this directly, call QueueSaveOptions instead so that we can debounce the saves and avoid saving multiple times per frame or multiple frames in a row
	private void SaveOptionsInternal() => Options.Save();

	private void QueueSaveOptions() => SaveOptionsQueuedTime = DateTime.UtcNow;

	private void SaveOptionsIfRequired()
	{
		//debounce the save requests and avoid saving multiple times per frame or multiple frames in a row
		if ((SaveOptionsQueuedTime > LastSaveOptionsTime)
			&& ((DateTime.UtcNow - SaveOptionsQueuedTime) > SaveOptionsDebounceTime))
		{
			SaveOptionsInternal();
			LastSaveOptionsTime = DateTime.UtcNow;
		}
	}

	private void FetchReposIfRequired()
	{
		foreach (var (repoPath, repoName) in Options.ClonedRepos)
		{
			if (Options.Repos.TryGetValue(repoName, out var repo)
				&& repo.MinFetchIntervalSeconds > 0
				&& (DateTime.UtcNow - repo.LastFetchTime) > TimeSpan.FromSeconds(repo.MinFetchIntervalSeconds))
			{
				repo.LastFetchTime = DateTime.UtcNow;
				QueueSaveOptions();

				var task = new Task(() =>
				{
					var localRepo = new LibGit2Sharp.Repository(repoPath);
					var fetchOptions = new LibGit2Sharp.FetchOptions();
					var origin = localRepo.Network.Remotes["origin"];
					var refSpecs = origin.FetchRefSpecs.Select(x => x.Specification);
					//LibGit2Sharp.Commands.Fetch(localRepo, "origin", refSpecs, fetchOptions, $"Fetching {repoName}");
				});

				task.Start();
			}
		}
	}

	private void SwitchPage(FullyQualifiedGitHubRepoName baseRepo)
	{
		if (Options.Repos.TryGetValue(baseRepo, out var repo))
		{
			_ = UpdateClonedStatus(repo);
			UpdateSimilarRepos(repo);
		}

		SwitchPage(baseRepo, new(), new());
	}

	private void SwitchPage(FullyQualifiedGitHubRepoName baseRepo, FullyQualifiedGitHubRepoName compareRepo) => SwitchPage(baseRepo, compareRepo, new());
	private void SwitchPage(FullyQualifiedGitHubRepoName baseRepo, FullyQualifiedGitHubRepoName compareRepo, RelativeFilePath compareFile)
	{
		Options.BaseRepo = baseRepo;
		Options.CompareRepo = compareRepo;
		Options.CompareFile = compareFile;
		if (compareRepo.IsEmpty())
		{
			SwitchRepoBrowserPath(baseRepo, new());
		}
		else
		{
			SwitchCompareBrowserPath(baseRepo, compareRepo, new());
		}
		QueueSaveOptions();
	}

	private void Tick(float dt)
	{
		DividerContainerCols.Tick(dt);

		_ = PopupSetDevDirectory.ShowIfOpen();
		_ = PopupAddNewGitHubOwner.ShowIfOpen();

		FetchReposIfRequired();
		SaveOptionsIfRequired();
	}

	private void ShowTopPanel(float dt)
	{
		if (Options.Repos.TryGetValue(Options.BaseRepo, out var repo))
		{
			ImGui.TextUnformatted($"Selected Repo: {Options.BaseRepo}");
			ImGui.TextUnformatted($"Local Path: {repo.LocalPath}");
			ImGui.TextUnformatted($"Remote Path: {repo.RemotePath}");

			ShowCollapsiblePanel($"Git Actions", () =>
			{
				if (!Options.ClonedRepos.ContainsValue(Options.BaseRepo))
				{
					if (ImGui.Button("Clone", new Vector2(FieldWidth, 0)))
					{
						Task.Run(() =>
						{
							_ = LibGit2Sharp.Repository.Clone(repo.RemotePath, repo.LocalPath);
						})
						.ContinueWith((t) =>
						{
							RefreshPage();
						},
						new CancellationToken(),
						TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Current);
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

					int timeUntilFetch = fetchInterval - (int)(DateTime.UtcNow - repo.LastFetchTime).TotalSeconds;
					_ = Knob.Draw("Fetch In", ref timeUntilFetch, 0, 300, 150);

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
					_ = ImGui.Button("Commit", new Vector2(FieldWidth, 0)); // TODO
					ImGui.SameLine();
					_ = ImGui.Button("Push", new Vector2(FieldWidth, 0)); // TODO
				}
			});

			if (!Options.CompareFile.IsEmpty())
			{
				ShowCollapsiblePanel($"Compare File", () => ShowComparedFile(dt, repo));
			}
			else if (!Options.CompareRepo.IsEmpty())
			{
				ShowCollapsiblePanel($"Compare Repo", () => ShowComparedRepo(repo));
			}
			else
			{
				ShowCollapsiblePanel($"Repo Browser", ShowRepoBrowser);
				ShowCollapsiblePanel($"Similar Repos", () => ShowSimilarRepos(repo));
			}
		}
	}

	private void RefreshPage()
	{
		if (Options.Repos.TryGetValue(Options.BaseRepo, out var repo))
		{
			_ = UpdateClonedStatus(repo);
			UpdateSimilarRepos(repo);
		}
		SwitchPage(Options.BaseRepo, Options.CompareRepo, Options.CompareFile);
	}

	private void ShowBottomPanel(float dt)
	{
		if (ImGui.BeginChild("Log", new Vector2(0, 0), true, ImGuiWindowFlags.HorizontalScrollbar))
		{
			ImGui.TextUnformatted(LogBuilder.ToString());
			ImGui.SetScrollHereY(1);
		}

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
				PopupSetDevDirectory.Open("Set Dev Directory?", "Set Dev Directory?", Options.DevDirectory, (string result) =>
				{
					if (!string.IsNullOrEmpty(result))
					{
						Options.DevDirectory = (AbsoluteDirectoryPath)result;
						QueueSaveOptions();
					}
				});
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
				_ = p.Start();
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Add New GitHub Owner"))
			{
				PopupAddNewGitHubOwner.Open("New Owner Name?", "New Owner Name?", "ktsu-io", (string result) =>
				{
					if (!string.IsNullOrEmpty(result))
					{
						var newName = (GitHubOwnerName)result;
						_ = Options.GitHubOwners.TryAdd(newName, (GitHubToken)string.Empty);
						SyncGitHubOwnerInfo(newName);
					}
				});
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
		foreach (var (repoName, repo) in Options.Repos.OrderBy(r => r.Key))
		{
			if (repo is GitHubRepository gitHubRepo)
			{
				if (gitHubRepo.OwnerName == owner)
				{
					bool isCloned = Options.ClonedRepos.ContainsKey(repo.LocalPath);
					ColorIndicator.Show(Color.Green, isCloned);
					ImGui.SameLine();
					bool isSelected = Options.BaseRepo == repoName;
					if (ImGui.Selectable(gitHubRepo.RepoName, ref isSelected))
					{
						SwitchPage(repoName);
					}
				}
			}
			else
			{
				_ = ImGui.Selectable(((string)repo.LocalPath).RemovePrefix(Options.DevDirectory + "\\"));
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
					else
					{
						throw new InvalidOperationException("Only GitHub Repos are supported at this time");
					}
				}
			}

			QueueSaveOptions();
		}
		catch (Octokit.ApiException)
		{
			// skip this owner
		}
	}

	private void UpdateClonedStatus()
	{
		bool changed = false;
		foreach (var (_, repo) in Options.Repos)
		{
			changed |= UpdateClonedStatus(repo);
		}

		if (changed)
		{
			QueueSaveOptions();
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0045:Convert to conditional expression", Justification = "<Pending>")]
	private bool UpdateClonedStatus(GitRepository repo)
	{
		var repoPath = repo.LocalPath;
		bool wasCloned = Options.ClonedRepos.ContainsKey(repoPath);
		bool isCloned = true;
		try
		{
			using var _ = new LibGit2Sharp.Repository(repoPath);
		}
		catch (LibGit2Sharp.RepositoryNotFoundException)
		{
			isCloned = false;
		}

		if (isCloned)
		{
			if (repo is GitHubRepository gitHubRepo)
			{
				Options.ClonedRepos[repoPath] = GetFullyQualifiedRepoName(gitHubRepo.OwnerName, gitHubRepo.RepoName);
			}
			else
			{
				throw new InvalidOperationException("Only GitHub Repos are supported at this time");
			}
		}
		else
		{
			_ = Options.ClonedRepos.Remove(repoPath);
		}

		return wasCloned != isCloned;
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
						_ = Options.GitHubOwners.TryAdd(gitHubRepo.OwnerName, (GitHubToken)string.Empty);
					}
				}
			}
			catch (NotSupportedException)
			{
				// skip non-git repos
			}
		}

		UpdateClonedStatus();
	}

	private void ScanRemoteAccountsForRepos()
	{
		var knownOwners = Options.GitHubOwners;
		foreach (var (owner, pat) in knownOwners)
		{
			if (!string.IsNullOrEmpty(pat) || (!string.IsNullOrEmpty(Options.GitHubLogin) && !string.IsNullOrEmpty(Options.GitHubToken)))
			{
				GitHubClient.Credentials = !string.IsNullOrEmpty(pat) ? new(owner, pat) : new(Options.GitHubLogin, Options.GitHubToken);
			}

			SyncGitHubOwnerInfo(owner);
		}

		UpdateClonedStatus();
	}

	private static FullyQualifiedLocalRepoPath MakeFullyQualifyLocalRepoPath(AbsoluteDirectoryPath localPath) => (FullyQualifiedLocalRepoPath)Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

	private void UpdateSimilarRepos(GitRepository repo)
	{
		foreach (var (_, otherRepo) in Options.Repos)
		{
			if (repo != otherRepo)
			{
				var diffs = DiffRepos(repo, otherRepo);
				if (otherRepo is GitHubRepository gitHubRepo)
				{
					var otherRepoName = GetFullyQualifiedRepoName(gitHubRepo.OwnerName, gitHubRepo.RepoName);
					repo.SimilarRepoDiffs[otherRepoName] = diffs;
				}
				else
				{
					throw new InvalidOperationException("Only GitHub Repos are supported at this time");
				}
			}
		}
	}

	private static Dictionary<RelativeFilePath, DiffResult> DiffRepos(GitRepository repoA, GitRepository repoB)
	{
		var diffs = new Dictionary<RelativeFilePath, DiffResult>();
		try
		{
			using var gitRepo = new LibGit2Sharp.Repository(repoA.LocalPath);
			var fileList = gitRepo.Index.Select(x => x.Path);

			if (repoA != repoB)
			{
				try
				{
					using var otherGitRepo = new LibGit2Sharp.Repository(repoB.LocalPath);
					var otherFileList = otherGitRepo.Index.Select(x => x.Path);
					var matches = fileList.Intersect(otherFileList).ToCollection();
					var fileContents = matches.ToDictionary(x => x, x =>
					{
						try
						{
							return File.ReadAllText(Path.Combine(repoA.LocalPath, x));
						}
						catch (FileNotFoundException)
						{
							return string.Empty;
						}
					});
					var otherFileContents = matches.ToDictionary(x => x, x =>
					{
						try
						{
							return File.ReadAllText(Path.Combine(repoB.LocalPath, x));
						}
						catch (FileNotFoundException)
						{
							return string.Empty;
						}
					});
					foreach (string match in matches)
					{
						diffs[(RelativeFilePath)match] = Differ.Instance.CreateLineDiffs(fileContents[match], otherFileContents[match], ignoreWhitespace: false, ignoreCase: false);
					}
				}
				catch (LibGit2Sharp.RepositoryNotFoundException)
				{
					// skip this repo
				}
			}
		}
		catch (LibGit2Sharp.RepositoryNotFoundException)
		{
			// skip this repo
		}

		return diffs;
	}

	private static DiffResult DiffSingleFile(GitRepository repoA, GitRepository repoB, RelativeFilePath filePath)
	{
		try
		{
			using var gitRepo = new LibGit2Sharp.Repository(repoA.LocalPath);

			if (repoA != repoB)
			{
				try
				{
					using var otherGitRepo = new LibGit2Sharp.Repository(repoB.LocalPath);

					string fileContents = File.ReadAllText(Path.Combine(repoA.LocalPath, filePath));
					string otherFileContents = File.ReadAllText(Path.Combine(repoB.LocalPath, filePath));
					return Differ.Instance.CreateLineDiffs(fileContents, otherFileContents, ignoreWhitespace: false, ignoreCase: false);
				}
				catch (LibGit2Sharp.RepositoryNotFoundException)
				{
					// skip this repo
				}
			}
		}
		catch (LibGit2Sharp.RepositoryNotFoundException)
		{
			// skip this repo
		}

		return new([], [], []);
	}

	private static void RefreshFileDiff(GitRepository repoA, GitRepository repoB, RelativeFilePath filePath)
	{
		var diff = DiffSingleFile(repoA, repoB, filePath);
		if (repoB is GitHubRepository gitHubRepo)
		{
			var otherRepoName = GetFullyQualifiedRepoName(gitHubRepo.OwnerName, gitHubRepo.RepoName);
			repoA.SimilarRepoDiffs[otherRepoName][filePath] = diff;
		}
		else
		{
			throw new InvalidOperationException("Only GitHub Repos are supported at this time");
		}
	}

	private void ShowSimilarRepos(GitRepository repo)
	{
		var sortedRepos = repo.SimilarRepoDiffs
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Sum(x => x.Value.DiffBlocks.Sum(y => y.InsertCountB + y.DeleteCountA) > 0 ? 30 : 70))
			.OrderByDescending(kvp => kvp.Value)
			.Select(kvp => kvp.Key);

		if (ImGui.BeginTable("SimilarRepos", 3, ImGuiTableFlags.Borders))
		{
			ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 40);
			ImGui.TableSetupColumn("Similar", ImGuiTableColumnFlags.None, 3);
			ImGui.TableSetupColumn("Exact", ImGuiTableColumnFlags.None, 3);
			ImGui.TableHeadersRow();

			foreach (var otherRepoName in sortedRepos)
			{
				int numExactDuplicates = repo.SimilarRepoDiffs[otherRepoName]
					.Count(kvp => kvp.Value.DiffBlocks.Count == 0);

				int numMatches = repo.SimilarRepoDiffs[otherRepoName].Count;

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(otherRepoName, selected: false, ImGuiSelectableFlags.SpanAllColumns);
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						SwitchPage(Options.BaseRepo, otherRepoName);
					}
				}
				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{numMatches}");
				}
				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{numExactDuplicates}");
				}
			}
		}
		ImGui.EndTable();
	}

	private void ShowComparedRepo(GitRepository repo)
	{
		if (ImGui.ArrowButton("Back", ImGuiDir.Left))
		{
			Options.CompareRepo = new();
			return;
		}

		ImGui.SameLine();
		ImGui.TextUnformatted($"Comparing {Options.BaseRepo} vs {Options.CompareRepo}");

		var diffs = repo.SimilarRepoDiffs[Options.CompareRepo];
		var sortedDiffs = diffs
			.ToDictionary(kvp => kvp.Key, CountChangedLinesInDiffBlock)
			.Where(kvp => kvp.Value > 0)
			.OrderBy(kvp => kvp.Value)
			.Select(kvp => kvp.Key);

		if (ImGui.BeginTable("SimilarFiles", 3, ImGuiTableFlags.Borders))
		{
			ImGui.TableSetupColumn("Similar Files", ImGuiTableColumnFlags.WidthStretch, 40);
			ImGui.TableSetupColumn("Deletions", ImGuiTableColumnFlags.None, 4);
			ImGui.TableSetupColumn("Additions", ImGuiTableColumnFlags.None, 4);
			ImGui.TableHeadersRow();

			foreach (var filePath in sortedDiffs)
			{
				var diff = diffs[filePath];

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(filePath, selected: false, ImGuiSelectableFlags.SpanAllColumns);
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						SwitchPage(Options.BaseRepo, Options.CompareRepo, filePath);
					}
				}
				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{diff.DiffBlocks.Sum(x => x.DeleteCountA)}");
				}
				if (ImGui.TableNextColumn())
				{
					ImGui.TextUnformatted($"{diff.DiffBlocks.Sum(x => x.InsertCountB)}");
				}
			}
		}
		ImGui.EndTable();

		ImGui.NewLine();
		ShowCompareBrowser();

		static int CountChangedLinesInDiffBlock(KeyValuePair<RelativeFilePath, DiffResult> kvp) =>
			kvp.Value.DiffBlocks.Sum(y => y.InsertCountB + y.DeleteCountA);
	}

	private void ShowComparedFile(float dt, GitRepository repo)
	{
		if (ImGui.ArrowButton("Back", ImGuiDir.Left))
		{
			Options.CompareFile = new();
			return;
		}

		ImGui.SameLine();
		ImGui.TextUnformatted($"Comparing {Options.BaseRepo} vs {Options.CompareRepo}");
		ImGui.SameLine();

		var diff = repo.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
		ShowWholeDiffSummary(diff);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
		if (ImGui.BeginChild("Diff", new(0, 0), border: false, ImGuiWindowFlags.NoDecoration))
		{
			ImGui.PopStyleVar();
			DividerDiff.Tick(dt);
		}
		ImGui.EndChild();
	}

	private const float DiffColorDesaturate = 0.3f;

	private static readonly Vector4 DiffDeletionLineColor = new(1, DiffColorDesaturate, DiffColorDesaturate, 0.4f);
	private static readonly Vector4 DiffDeletionFillerLineColor = new(1, DiffColorDesaturate, DiffColorDesaturate, 0.2f);
	private static readonly Vector4 DiffAdditionLineColor = new(DiffColorDesaturate, 1, DiffColorDesaturate, 0.4f);
	private static readonly Vector4 DiffAdditionFillerLineColor = new(DiffColorDesaturate, 1, DiffColorDesaturate, 0.2f);
	private static readonly Vector4 UnchangedLineColor = new(1, 1, 1, 0.1f);

	private static Vector2 ScrollLeft { get; set; }
	private static Vector2 ScrollRight { get; set; }
	private void ShowDiffLeft(GitRepository repoA, GitRepository repoB, DiffResult? diff)
	{
		if (diff is null)
		{
			return;
		}

		int i = 0;
		foreach (var block in diff.DiffBlocks)
		{
			if (ImGui.ArrowButton($"DiffTakeLeft{i}", ImGuiDir.Right))
			{
				List<string> newLines = [];
				AddPrologueForDeletedLines(diff, block, newLines);
				AddDeletedLines(diff, block, newLines);
				AddEpilogueForDeletedLines(diff, block, newLines);
				string newText = string.Join(Environment.NewLine, newLines);
				File.WriteAllText(Path.Combine(repoB.LocalPath, Options.CompareFile), newText);
				RefreshFileDiff(repoA, repoB, Options.CompareFile);
			}
			ImGui.SameLine();
			ShowDiffBlockSummary(block);
			if (ImGui.BeginTable($"DiffBlockLeft{i}", 3, ImGuiTableFlags.SizingFixedFit))
			{
				ShowPrologueForDeletedLines(diff, block);
				ShowDeletedLines(diff, block);
				ShowEpilogueForDeletedLines(diff, block);
			}
			ImGui.EndTable();
			ImGui.NewLine();

			++i;
		}

		// synchronize left and right scrollbars
		if (ImGui.IsWindowHovered())
		{
			ScrollRight = new(ImGui.GetScrollX(), ImGui.GetScrollY());
		}
		else
		{
			ImGui.SetScrollX(ScrollLeft.X);
			ImGui.SetScrollY(ScrollLeft.Y);
		}

		ScrollLeft = new(ImGui.GetScrollX(), ImGui.GetScrollY());

		static void AddPrologueForDeletedLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int endIndex = block.InsertStartB;
			newLines.AddRange(diff.PiecesNew[..endIndex]);
		}

		static void AddEpilogueForDeletedLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int startIndex = block.InsertStartB + block.InsertCountB;
			newLines.AddRange(diff.PiecesNew[startIndex..]);
		}

		static void AddDeletedLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int startIndex = block.DeleteStartA;
			int endIndex = startIndex + block.DeleteCountA;
			newLines.AddRange(diff.PiecesOld[startIndex..endIndex]);
		}

		static void ShowPrologueForDeletedLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = Math.Max(block.DeleteStartA - 3, 0);
			int endIndex = block.DeleteStartA;
			var formattedLines = FormatUnchangedLines(diff.PiecesOld[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
		}

		static void ShowDeletedLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = block.DeleteStartA;
			int endIndex = startIndex + block.DeleteCountA;
			var formattedLines = FormatDeletedLines(diff.PiecesOld[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, "-", DiffDeletionLineColor);

			int extraLines = Math.Max(0, block.InsertCountB - block.DeleteCountA);
			ShowDiffLines(Enumerable.Repeat(string.Empty, extraLines), -1, "-", DiffAdditionFillerLineColor);
		}

		static void ShowEpilogueForDeletedLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = block.DeleteStartA + block.DeleteCountA;
			int endIndex = Math.Min(startIndex + 3, diff.PiecesOld.Length);
			var formattedLines = FormatUnchangedLines(diff.PiecesOld[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
		}
	}

	private void ShowDiffRight(GitRepository repoA, GitRepository repoB, DiffResult? diff)
	{
		if (diff is null)
		{
			return;
		}

		int i = 0;
		foreach (var block in diff.DiffBlocks)
		{
			if (ImGui.ArrowButton($"DiffTakeRight{i}", ImGuiDir.Left))
			{
				List<string> newLines = [];
				AddPrologueForNewLines(diff, block, newLines);
				AddNewLines(diff, block, newLines);
				AddEpilogueForNewLines(diff, block, newLines);
				string newText = string.Join(Environment.NewLine, newLines);
				File.WriteAllText(Path.Combine(repoA.LocalPath, Options.CompareFile), newText);
				RefreshFileDiff(repoA, repoB, Options.CompareFile);
			}
			ImGui.SameLine();
			ShowDiffBlockSummary(block);
			if (ImGui.BeginTable($"DiffBlockRight{i}", 3, ImGuiTableFlags.SizingFixedFit))
			{
				ShowPrologueForNewLines(diff, block);
				ShowNewLines(diff, block);
				ShowEpilogueForNewLines(diff, block);
			}
			ImGui.EndTable();
			ImGui.NewLine();
			++i;
		}

		// synchronize left and right scrollbars
		if (ImGui.IsWindowHovered())
		{
			ScrollLeft = new(ImGui.GetScrollX(), ImGui.GetScrollY());
		}
		else
		{
			ImGui.SetScrollX(ScrollRight.X);
			ImGui.SetScrollY(ScrollRight.Y);
		}

		ScrollRight = new(ImGui.GetScrollX(), ImGui.GetScrollY());

		static void AddPrologueForNewLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int endIndex = block.DeleteStartA;
			newLines.AddRange(diff.PiecesOld[..endIndex]);
		}

		static void AddNewLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int startIndex = block.InsertStartB;
			int endIndex = startIndex + block.InsertCountB;
			newLines.AddRange(diff.PiecesNew[startIndex..endIndex]);
		}

		static void AddEpilogueForNewLines(DiffResult diff, DiffBlock block, List<string> newLines)
		{
			int startIndex = block.DeleteStartA + block.DeleteCountA;
			newLines.AddRange(diff.PiecesOld[startIndex..]);
		}

		static void ShowPrologueForNewLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = Math.Max(block.InsertStartB - 3, 0);
			int endIndex = block.InsertStartB;
			var formattedLines = FormatUnchangedLines(diff.PiecesNew[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
		}

		static void ShowNewLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = block.InsertStartB;
			int endIndex = startIndex + block.InsertCountB;
			var formattedLines = FormatAddedLines(diff.PiecesNew[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, "+", DiffAdditionLineColor);

			int extraLines = Math.Max(0, block.DeleteCountA - block.InsertCountB);
			ShowDiffLines(Enumerable.Repeat(string.Empty, extraLines), -1, "-", DiffDeletionFillerLineColor);
		}

		static void ShowEpilogueForNewLines(DiffResult diff, DiffBlock block)
		{
			int startIndex = block.InsertStartB + block.InsertCountB;
			int endIndex = Math.Min(startIndex + 3, diff.PiecesNew.Length);
			var formattedLines = FormatUnchangedLines(diff.PiecesNew[startIndex..endIndex]);
			ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
		}
	}

	private static void ShowDiffLines(IEnumerable<string> lines, int lineIndex, string prefix, Vector4 color)
	{
		foreach (string line in lines)
		{
			if (ImGui.TableNextColumn())
			{
				ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(color));
				if (lineIndex >= 0)
				{
					ImGui.TextUnformatted($"{lineIndex++}");
				}
			}
			if (ImGui.TableNextColumn())
			{
				ImGui.TextUnformatted(prefix);
			}
			if (ImGui.TableNextColumn())
			{
				ImGui.TextUnformatted(line);
			}
		}
	}

	private static void ShowWholeDiffSummary(DiffResult diff) => ShowDiffSummaryText(diff.DiffBlocks.Sum(x => x.DeleteCountA), diff.DiffBlocks.Sum(x => x.InsertCountB));

	private static void ShowDiffBlockSummary(DiffBlock diff) => ShowDiffSummaryText(diff.DeleteCountA, diff.InsertCountB);

	private static void ShowDiffSummaryText(int linesDeleted, int linesAdded)
	{
		int totalModifications = linesDeleted + linesAdded;
		if (totalModifications > 0)
		{
			int displayedModifications = Math.Clamp(totalModifications, 1, 10);
			int displayedDeletions = (int)Math.Round((double)linesDeleted / totalModifications * displayedModifications, 0);
			int displayedAdditions = displayedModifications - displayedDeletions;

			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
			ImGui.TextUnformatted($"{new string('-', displayedDeletions)}");
			ImGui.PopStyleColor();
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
			ImGui.SameLine();
			ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
			ImGui.TextUnformatted($"{new string('+', displayedAdditions)}");
			ImGui.PopStyleColor();
			ImGui.PopStyleVar();
		}
		else
		{
			ImGui.TextUnformatted("No Changes");
		}
	}

	private static IEnumerable<string> FormatLines(IEnumerable<string> lines) => lines.Select(x => x.ReplaceOrdinal("\t", "  ").Trim('\r', '\n'));
	private static IEnumerable<string> FormatDeletedLines(IEnumerable<string> lines) => FormatLines(lines);
	private static IEnumerable<string> FormatAddedLines(IEnumerable<string> lines) => FormatLines(lines);
	private static IEnumerable<string> FormatUnchangedLines(IEnumerable<string> lines) => FormatLines(lines);

	private void ShowCompareBrowser()
	{
		var allFilesystemEntries = BrowserContentsBase.Union(BrowserContentsCompare);
		var directories = allFilesystemEntries.Where(x => x.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)).ToCollection();
		var files = allFilesystemEntries.Except(directories).ToCollection();

		if (ImGui.BeginTable("CompareBrowser", 3, ImGuiTableFlags.Borders))
		{
			ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 40);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, 1);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, 1);
			ImGui.TableHeadersRow();

			if (!Options.BrowsePath.IsEmpty())
			{
				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable($"..");
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						string newPath = Path.GetDirectoryName(((string)Options.BrowsePath).RemoveSuffix(Path.DirectorySeparatorChar.ToString()))!;
						if (string.IsNullOrEmpty(newPath))
						{
							SwitchCompareBrowserPath(Options.BaseRepo, Options.CompareRepo, new());
						}
						else
						{
							SwitchCompareBrowserPath(Options.BaseRepo, Options.CompareRepo, (RelativeDirectoryPath)newPath);
						}
					}
				}
			}

			foreach (var path in directories)
			{
				bool existsInA = BrowserContentsBase.Contains(path);
				bool existsInB = BrowserContentsCompare.Contains(path);

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(path, selected: false, ImGuiSelectableFlags.SpanAllColumns);
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						var repoA = Options.Repos[Options.BaseRepo];
						var repoB = Options.Repos[Options.CompareRepo];
						if (repoA is GitHubRepository githubRepoA && repoB is GitHubRepository githubRepoB)
						{
							SwitchCompareBrowserPath(GetFullyQualifiedRepoName(githubRepoA.OwnerName, githubRepoA.RepoName), GetFullyQualifiedRepoName(githubRepoB.OwnerName, githubRepoB.RepoName), (RelativeDirectoryPath)Path.Combine(Options.BrowsePath, path));

						}
						else
						{
							throw new InvalidOperationException("Only GitHub Repos are supported at this time");
						}
					}
				}
				if (ImGui.TableNextColumn() && existsInA != existsInB)
				{
					if (existsInA && ImGui.ArrowButton("##Copy", ImGuiDir.Right))
					{
						_ = Directory.CreateDirectory(Path.Combine(Options.Repos[Options.CompareRepo].LocalPath, Options.BrowsePath, path));
					}
					else if (existsInB && ImGui.ArrowButton("##Copy", ImGuiDir.Left))
					{
						_ = Directory.CreateDirectory(Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path));
					}

					if (ImGui.IsItemHovered())
					{
						if (ImGui.BeginTooltip())
						{
							ImGui.TextUnformatted(existsInA ? "Give" : "Take");
						}
						ImGui.EndTooltip();
					}
				}
				if (ImGui.TableNextColumn() && existsInA != existsInB)
				{
					if (existsInA && ImGui.Button("X##Remove"))
					{
						Directory.Delete(Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path));
					}
					else if (existsInB && ImGui.Button("X##Remove"))
					{
						Directory.Delete(Path.Combine(Options.Repos[Options.CompareRepo].LocalPath, Options.BrowsePath, path));
					}

					if (ImGui.IsItemHovered())
					{
						if (ImGui.BeginTooltip())
						{
							ImGui.TextUnformatted("Delete");
						}
						ImGui.EndTooltip();
					}
				}
			}

			foreach (var path in files)
			{
				bool existsInA = BrowserContentsBase.Contains(path);
				bool existsInB = BrowserContentsCompare.Contains(path);

				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(path, selected: false, ImGuiSelectableFlags.SpanAllColumns);
				}

				if (ImGui.TableNextColumn() && existsInA != existsInB)
				{
					if (existsInA && ImGui.ArrowButton("##Copy", ImGuiDir.Right))
					{
						string srcPath = Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path);
						string dstPath = Path.Combine(Options.Repos[Options.CompareRepo].LocalPath, Options.BrowsePath, path);
						File.Copy(srcPath, dstPath);
					}
					else if (existsInB && ImGui.ArrowButton("##Copy", ImGuiDir.Left))
					{
						string srcPath = Path.Combine(Options.Repos[Options.CompareRepo].LocalPath, Options.BrowsePath, path);
						string dstPath = Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path);
						File.Copy(srcPath, dstPath);
					}

					if (ImGui.IsItemHovered())
					{
						if (ImGui.BeginTooltip())
						{
							ImGui.TextUnformatted(existsInA ? "Give" : "Take");
						}
						ImGui.EndTooltip();
					}
				}

				if (ImGui.TableNextColumn() && existsInA != existsInB)
				{
					if (existsInA && ImGui.Button("X##Remove"))
					{
						File.Delete(Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path));
					}
					else if (existsInB && ImGui.Button("X##Remove"))
					{
						File.Delete(Path.Combine(Options.Repos[Options.CompareRepo].LocalPath, Options.BrowsePath, path));
					}

					if (ImGui.IsItemHovered())
					{
						if (ImGui.BeginTooltip())
						{
							ImGui.TextUnformatted("Delete");
						}
						ImGui.EndTooltip();
					}
				}
			}
		}
		ImGui.EndTable();
	}

	private void ShowRepoBrowser()
	{
		var allFilesystemEntries = BrowserContentsBase;
		var directories = allFilesystemEntries.Where(x => x.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)).ToCollection();
		var files = allFilesystemEntries.Except(directories).ToCollection();

		bool shouldOpenPopup = false;

		if (ImGui.BeginTable("RepoBrowser", 3, ImGuiTableFlags.Borders))
		{
			ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 40);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, 10);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None, 1);
			ImGui.TableHeadersRow();

			if (!Options.BrowsePath.IsEmpty())
			{
				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable($"..");
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						string newPath = Path.GetDirectoryName(((string)Options.BrowsePath).RemoveSuffix(Path.DirectorySeparatorChar.ToString()))!;
						if (string.IsNullOrEmpty(newPath))
						{
							SwitchRepoBrowserPath(Options.BaseRepo, new());
						}
						else
						{
							SwitchRepoBrowserPath(Options.BaseRepo, (RelativeDirectoryPath)newPath);
						}
					}
				}
			}

			foreach (var path in directories)
			{
				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(path);
					if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
					{
						var repoA = Options.Repos[Options.BaseRepo];
						if (repoA is GitHubRepository githubRepoA)
						{
							SwitchRepoBrowserPath(GetFullyQualifiedRepoName(githubRepoA.OwnerName, githubRepoA.RepoName), (RelativeDirectoryPath)path.WeakString);
						}
						else
						{
							throw new InvalidOperationException("Only GitHub Repos are supported at this time");
						}
					}
				}

				if (ImGui.TableNextColumn())
				{
					//if (ImGui.Button($"Propagate Directory###Propagate{path.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')}"))
					//{
					//	shouldOpenPopup |= true;
					//	Options.PropagatePath = path;
					//}
				}

				if (ImGui.TableNextColumn())
				{
					//if (ImGui.Button($"X"))
					//{
					//	//Directory.Delete(Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path));
					//}

					//if (ImGui.IsItemHovered())
					//{
					//	ImGui.BeginTooltip();
					//	ImGui.TextUnformatted("Delete Directory");
					//	ImGui.EndTooltip();
					//}
				}
			}

			foreach (var path in files)
			{
				ImGui.TableNextRow();
				if (ImGui.TableNextColumn())
				{
					_ = ImGui.Selectable(path);
					if (ImGui.BeginPopupContextItem(path, ImGuiPopupFlags.MouseButtonRight))
					{
						ImGui.Selectable(path);
						ImGui.Separator();
						if (ImGui.Selectable($"Propagate"))
						{
							shouldOpenPopup |= true;
							Options.PropagatePath = path;
						}
						if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
						{
							if (ImGui.Selectable($"Generate Tests"))
							{

							}

							if (ImGui.Selectable($"Generate Documentation Comments"))
							{

							}

							if (ImGui.Selectable($"Generate Readme"))
							{
								var repo = Options.Repos[Options.BaseRepo];
								var csFiles = Directory.EnumerateFiles(repo.LocalPath, "*.cs", SearchOption.AllDirectories);
								var allFileContents = new StringBuilder();
								foreach (string file in csFiles)
								{
									string relativePath = file.RemovePrefix(repo.LocalPath);
									allFileContents.AppendLine($"Begin: {relativePath}\n\n" + File.ReadAllText(file) + $"\nEnd: {relativePath}\n\n");
								}

								var response = ChatClient.CompleteChat("write a github readme file for a project that includes the following files:\n\n" + allFileContents.ToString());
								_ = LogBuilder.AppendLine(CultureInfo.InvariantCulture, $"[ASSISTANT]: {response.Value}");
							}

							if (ImGui.Selectable($"Summarize"))
							{
								var repo = Options.Repos[Options.BaseRepo];
								string filePath = Path.Combine(repo.LocalPath, path);
								string fileContents = File.ReadAllText(filePath);
								var response = ChatClient.CompleteChat("summarize the following dotnet file:\n\n" + fileContents);
								_ = LogBuilder.AppendLine(CultureInfo.InvariantCulture, $"[ASSISTANT]: {response.Value}");
							}
						}
						ImGui.EndPopup();
					}
				}

				if (ImGui.TableNextColumn())//&& ImGui.Button($"Propagate###Propagate{path.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')}"))
				{
					//shouldOpenPopup |= true;
					//Options.PropagatePath = path;
				}

				if (ImGui.TableNextColumn())
				{
					//if (ImGui.Button($"X"))
					//{
					//	//File.Delete(Path.Combine(Options.Repos[Options.BaseRepo].LocalPath, Options.BrowsePath, path));
					//}

					//if (ImGui.IsItemHovered())
					//{
					//	ImGui.BeginTooltip();
					//	ImGui.TextUnformatted("Delete File");
					//	ImGui.EndTooltip();
					//}
				}
			}
		}
		ImGui.EndTable();

		if (shouldOpenPopup)
		{
			PopupPropagateFile.Open(Options);
		}

		_ = PopupPropagateFile.ShowIfOpen();
	}

	private void SwitchCompareBrowserPath(FullyQualifiedGitHubRepoName baseRepo, FullyQualifiedGitHubRepoName compareRepo, RelativeDirectoryPath newPath)
	{
		Options.BrowsePath = newPath;
		var repoA = Options.Repos[baseRepo];
		var repoB = Options.Repos[compareRepo];

		static RelativePath formatPath(string path, string prefix) => (RelativePath)(path.RemovePrefix(prefix + Path.DirectorySeparatorChar) + (Directory.Exists(path) ? Path.DirectorySeparatorChar : string.Empty));

		BrowserContentsBase.Clear();
		BrowserContentsCompare.Clear();

		try
		{
			BrowserContentsBase = Directory.EnumerateFileSystemEntries(Path.Combine(repoA.LocalPath, Options.BrowsePath)).Select(x => formatPath(x, repoA.LocalPath)).ToCollection();
		}
		catch (DirectoryNotFoundException)
		{
			// skip this repo
		}

		try
		{
			BrowserContentsCompare = Directory.EnumerateFileSystemEntries(Path.Combine(repoB.LocalPath, Options.BrowsePath)).Select(x => formatPath(x, repoB.LocalPath)).ToCollection();
		}
		catch (DirectoryNotFoundException)
		{
			// skip this repo
		}

		QueueSaveOptions();
	}

	private void SwitchRepoBrowserPath(FullyQualifiedGitHubRepoName baseRepo, RelativeDirectoryPath newPath)
	{
		Options.BrowsePath = newPath;
		var repoA = Options.Repos[baseRepo];

		static RelativePath formatPath(string path, string prefix) => (RelativePath)(path.RemovePrefix(prefix + Path.DirectorySeparatorChar) + (Directory.Exists(path) ? Path.DirectorySeparatorChar : string.Empty));

		BrowserContentsBase.Clear();
		BrowserContentsCompare.Clear();

		try
		{
			BrowserContentsBase = Directory.EnumerateFileSystemEntries(Path.Combine(repoA.LocalPath, Options.BrowsePath)).Select(x => formatPath(x, repoA.LocalPath)).ToCollection();
		}
		catch (DirectoryNotFoundException)
		{
			// skip this repo
		}

		QueueSaveOptions();
	}
}
