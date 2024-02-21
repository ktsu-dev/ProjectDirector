namespace ktsu.io.ProjectDirector;

using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using DiffPlex;
using DiffPlex.Model;
using ImGuiNET;
using ktsu.io.Extensions;
using ktsu.io.ImGuiApp;
using ktsu.io.ImGuiWidgets;
using ktsu.io.StrongPaths;

internal sealed class ProjectDirector
{
	internal ProjectDirectorOptions Options { get; } = new();
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

	private static void Main(string[] _)
	{
		ProjectDirector projectDirector = new();
		ImGuiApp.Start(nameof(ProjectDirector), new ImGuiAppWindowState(), projectDirector.Tick, projectDirector.ShowMenu, projectDirector.WindowResized);
	}

	public ProjectDirector()
	{
		Options = ProjectDirectorOptions.LoadOrCreate();

		DividerDiff = new("DiffDivider", DividerResized, DividerLayout.Columns);
		DividerContainerCols = new("VerticalDivider", DividerResized, DividerLayout.Columns);
		DividerContainerRows = new("HorizontalDivider", DividerResized, DividerLayout.Rows);
		DividerContainerCols.Add("Left", 0.25f, ShowLeftPanel);
		DividerContainerCols.Add("Right", 0.75f, ShowRightPanel);
		DividerContainerRows.Add("Top", 0.80f, ShowTopPanel);
		DividerContainerRows.Add("Bottom", 0.20f, ShowBottomPanel);
		DividerDiff.Add("Left", 0.50f, (dt) =>
		{
			var repo = Options.Repos[Options.SelectedRepo];
			var diff = repo.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
			ShowDiffLeft(diff);
		});
		DividerDiff.Add("Right", 0.50f, (dt) =>
		{
			var repo = Options.Repos[Options.SelectedRepo];
			var diff = repo.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
			ShowDiffRight(diff);
		});

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

		SwitchRepo(Options.Repos[Options.SelectedRepo]);
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

		if (Options.DividerStates.TryGetValue(DividerContainerRows.Id, out sizes))
		{
			DividerContainerRows.SetSizesFromList(sizes);
		}

		if (Options.DividerStates.TryGetValue(DividerDiff.Id, out sizes))
		{
			DividerDiff.SetSizesFromList(sizes);
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
						//LibGit2Sharp.Commands.Fetch(localRepo, "origin", refSpecs, fetchOptions, $"Fetching {repoName}");
					});

					task.Start();
				}
			}
		}
	}

	private void SwitchRepo(GitRepository repo)
	{
		if (repo is GitHubRepository gitHubRepo)
		{
			var repoName = GetFullyQualifiedRepoName(gitHubRepo.OwnerName, gitHubRepo.RepoName);
			Options.SelectedRepo = repoName;
			Options.CompareRepo = new();
			Options.CompareFile = new();
			UpdateClonedStatus(repo);
			UpdateSimilarRepos(repo);
			QueueSaveOptions();
		}
		else
		{
			throw new InvalidOperationException("Only GitHub Repos are supported at this time");
		}
	}

	private void SwitchComparedRepo(GitRepository repo)
	{
		if (repo is GitHubRepository gitHubRepo)
		{
			var repoName = GetFullyQualifiedRepoName(gitHubRepo.OwnerName, gitHubRepo.RepoName);
			Options.CompareRepo = repoName;
			Options.CompareFile = new();
			QueueSaveOptions();
		}
		else
		{
			throw new InvalidOperationException("Only GitHub Repos are supported at this time");
		}
	}

	private void SwitchComparedFile(RelativeFilePath filePath)
	{
		Options.CompareFile = filePath;
		QueueSaveOptions();
	}

	private void Tick(float dt)
	{
		DividerContainerCols.Tick(dt);

		PopupSetDevDirectory.ShowIfOpen();
		PopupAddNewGitHubOwner.ShowIfOpen();

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
							SwitchRepo(repo);
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

			ShowCollapsiblePanel($"Similar Repos", () =>
			{
				if (!Options.CompareFile.IsEmpty())
				{
					ShowComparedFile(dt, repo);
				}
				else if (!Options.CompareRepo.IsEmpty())
				{
					ShowComparedRepo(repo);
				}
				else
				{
					ShowSimilarRepos(repo);
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
				p.Start();
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Add New GitHub Owner"))
			{
				PopupAddNewGitHubOwner.Open("New Owner Name?", "New Owner Name?", "ktsu-io", (string result) =>
				{
					if (!string.IsNullOrEmpty(result))
					{
						var newName = (GitHubOwnerName)result;
						Options.GitHubOwners.TryAdd(newName, (GitHubPAT)string.Empty);
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
		foreach (var (repoName, repo) in Options.Repos)
		{
			if (repo is GitHubRepository gitHubRepo)
			{
				if (gitHubRepo.OwnerName == owner)
				{
					bool isChecked = Options.ClonedRepos.ContainsKey(repo.LocalPath);
					ImGui.Checkbox($"##cb{repoName}", ref isChecked);
					ImGui.SameLine();
					bool isSelected = Options.SelectedRepo == repoName;
					if (ImGui.Selectable(gitHubRepo.RepoName, ref isSelected))
					{
						SwitchRepo(repo);
						QueueSaveOptions();
					}
				}
			}
			else
			{
				ImGui.Selectable(((string)repo.LocalPath).RemovePrefix(Options.DevDirectory + "\\"));
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
		catch (Exception)
		{
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
			Options.ClonedRepos.Remove(repoPath);
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
					var fileContents = matches.ToDictionary(x => x, x => File.ReadAllText(Path.Combine(repoA.LocalPath, x)));
					var otherFileContents = matches.ToDictionary(x => x, x => File.ReadAllText(Path.Combine(repoB.LocalPath, x)));
					foreach (string match in matches)
					{
						diffs[(RelativeFilePath)match] = Differ.Instance.CreateLineDiffs(fileContents[match], otherFileContents[match], ignoreWhitespace: false, ignoreCase: false);
					}
				}
				catch (LibGit2Sharp.RepositoryNotFoundException)
				{
				}
			}
		}
		catch (LibGit2Sharp.RepositoryNotFoundException)
		{
		}
		return diffs;
	}

	private void ShowSimilarRepos(GitRepository repo)
	{
		var sortedRepos = repo.SimilarRepoDiffs
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Sum(x => x.Value.DiffBlocks.Sum(y => y.InsertCountB + y.DeleteCountA) > 0 ? 30 : 70))
			.OrderByDescending(kvp => kvp.Value)
			.Select(kvp => kvp.Key);

		ImGui.BeginTable("SimilarRepos", 4, ImGuiTableFlags.Borders);
		ImGui.TableSetupColumn("Similar Repos", ImGuiTableColumnFlags.WidthStretch, 40);
		ImGui.TableSetupColumn("Similar", ImGuiTableColumnFlags.None, 3);
		ImGui.TableSetupColumn("Exact", ImGuiTableColumnFlags.None, 3);
		ImGui.TableSetupColumn("Diff", ImGuiTableColumnFlags.NoHeaderLabel, 1);
		ImGui.TableHeadersRow();

		foreach (var otherRepoName in sortedRepos)
		{
			int numExactDuplicates = repo.SimilarRepoDiffs[otherRepoName]
				.Count(kvp => kvp.Value.DiffBlocks.Count == 0);

			int numMatches = repo.SimilarRepoDiffs[otherRepoName].Count;

			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(otherRepoName);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted($"{numMatches}");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted($"{numExactDuplicates}");
			ImGui.TableNextColumn();
			if (ImGui.ArrowButton($"Diff_{otherRepoName}", ImGuiDir.Right))
			{
				SwitchComparedRepo(Options.Repos[otherRepoName]);
			}
		}
		ImGui.EndTable();
	}

	private void ShowComparedRepo(GitRepository repo)
	{
		if (ImGui.Button("Back"))
		{
			Options.CompareRepo = new();
			return;
		}

		var otherRepo = Options.Repos[Options.CompareRepo];
		var diffs = repo.SimilarRepoDiffs[Options.CompareRepo];
		var sortedDiffs = diffs
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DiffBlocks.Sum(y => y.InsertCountB + y.DeleteCountA))
			.OrderBy(kvp => kvp.Value)
			.Where(kvp => kvp.Value > 0)
			.Select(kvp => kvp.Key);

		ImGui.BeginTable("SimilarFiles", 4, ImGuiTableFlags.Borders);
		ImGui.TableSetupColumn("Similar Files", ImGuiTableColumnFlags.WidthStretch, 40);
		ImGui.TableSetupColumn("Deletions", ImGuiTableColumnFlags.None, 4);
		ImGui.TableSetupColumn("Additions", ImGuiTableColumnFlags.None, 4);
		ImGui.TableSetupColumn("Diff", ImGuiTableColumnFlags.NoHeaderLabel, 1);
		ImGui.TableHeadersRow();

		foreach (var filePath in sortedDiffs)
		{
			var diff = diffs[filePath];

			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted($"{filePath}");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted($"{diff.DiffBlocks.Sum(x => x.DeleteCountA)}");
			ImGui.TableNextColumn();
			ImGui.TextUnformatted($"{diff.DiffBlocks.Sum(x => x.InsertCountB)}");
			ImGui.TableNextColumn();
			if (ImGui.ArrowButton($"Diff_{filePath}", ImGuiDir.Right))
			{
				SwitchComparedFile(filePath);
			}
		}
		ImGui.EndTable();
	}

	private void ShowComparedFile(float dt, GitRepository repo)
	{
		if (ImGui.Button("Back"))
		{
			Options.CompareFile = new();
			return;
		}

		ImGui.SameLine();

		var diff = repo.SimilarRepoDiffs[Options.CompareRepo][Options.CompareFile];
		ShowWholeDiffSummary(diff);

		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
		ImGui.BeginChild("Diff", new(0, 0), border: false, ImGuiWindowFlags.NoDecoration);
		ImGui.PopStyleVar();
		DividerDiff.Tick(dt);
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
	private static void ShowDiffLeft(DiffResult? diff)
	{
		if (diff is null)
		{
			return;
		}

		int i = 0;
		foreach (var block in diff.DiffBlocks)
		{
			ShowDiffBlockSummary(block);
			if (ImGui.BeginTable($"DiffBlockLeft{i}", 3, ImGuiTableFlags.SizingFixedFit))
			{
				// prologue
				{
					int startIndex = Math.Max(block.DeleteStartA - 3, 0);
					int endIndex = block.DeleteStartA;
					var formattedLines = FormatUnchangedLines(diff.PiecesOld[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
				}

				// body
				{
					int startIndex = block.DeleteStartA;
					int endIndex = startIndex + block.DeleteCountA;
					var formattedLines = FormatDeletedLines(diff.PiecesOld[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, "-", DiffDeletionLineColor);

					int extraLines = Math.Max(0, block.InsertCountB - block.DeleteCountA);
					ShowDiffLines(Enumerable.Repeat(string.Empty, extraLines), -1, "-", DiffAdditionFillerLineColor);
				}

				// epilogue
				{
					int startIndex = block.DeleteStartA + block.DeleteCountA;
					int endIndex = Math.Min(startIndex + 3, diff.PiecesOld.Length);
					var formattedLines = FormatUnchangedLines(diff.PiecesOld[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
				}
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
	}

	private static void ShowDiffRight(DiffResult? diff)
	{
		if (diff is null)
		{
			return;
		}

		int i = 0;
		foreach (var block in diff.DiffBlocks)
		{
			ShowDiffBlockSummary(block);
			if (ImGui.BeginTable($"DiffBlockRight{i}", 3, ImGuiTableFlags.SizingFixedFit))
			{
				// prologue
				{
					int startIndex = Math.Max(block.InsertStartB - 3, 0);
					int endIndex = block.InsertStartB;
					var formattedLines = FormatUnchangedLines(diff.PiecesNew[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
				}

				// body
				{
					int startIndex = block.InsertStartB;
					int endIndex = startIndex + block.InsertCountB;
					var formattedLines = FormatAddedLines(diff.PiecesNew[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, "+", DiffAdditionLineColor);

					int extraLines = Math.Max(0, block.DeleteCountA - block.InsertCountB);
					ShowDiffLines(Enumerable.Repeat(string.Empty, extraLines), -1, "-", DiffDeletionFillerLineColor);
				}

				// epilogue
				{
					int startIndex = block.InsertStartB + block.InsertCountB;
					int endIndex = Math.Min(startIndex + 3, diff.PiecesNew.Length);
					var formattedLines = FormatUnchangedLines(diff.PiecesNew[startIndex..endIndex]);
					ShowDiffLines(formattedLines, startIndex, string.Empty, UnchangedLineColor);
				}
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
	}

	private static void ShowDiffLines(IEnumerable<string> lines, int lineIndex, string prefix, Vector4 color)
	{
		foreach (string line in lines)
		{
			ImGui.TableNextColumn();
			ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.ColorConvertFloat4ToU32(color));
			if (lineIndex >= 0)
			{
				ImGui.TextUnformatted($"{lineIndex++}");
			}
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(prefix);
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(line);
		}
	}

	private static void ShowWholeDiffSummary(DiffResult diff) => ShowDiffSummaryText(diff.DiffBlocks.Sum(x => x.DeleteCountA), diff.DiffBlocks.Sum(x => x.InsertCountB));

	private static void ShowDiffBlockSummary(DiffBlock diff) => ShowDiffSummaryText(diff.DeleteCountA, diff.InsertCountB);

	private static void ShowDiffSummaryText(int linesDeleted, int linesAdded)
	{
		int totalModifications = linesDeleted + linesAdded;
		int displayedModifications = Math.Min(10, totalModifications);
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

	private static IEnumerable<string> FormatLines(IEnumerable<string> lines) => lines.Select(x => x.ReplaceOrdinal("\t", "  ").Trim('\r', '\n'));
	private static IEnumerable<string> FormatDeletedLines(IEnumerable<string> lines) => FormatLines(lines);
	private static IEnumerable<string> FormatAddedLines(IEnumerable<string> lines) => FormatLines(lines);
	private static IEnumerable<string> FormatUnchangedLines(IEnumerable<string> lines) => FormatLines(lines);

}
