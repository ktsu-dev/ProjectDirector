# ProjectDirector

> A desktop application for managing and comparing many Git repositories side by side.

[![License](https://img.shields.io/github/license/ktsu-dev/ProjectDirector.svg?label=License&logo=nuget)](LICENSE.md)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/ProjectDirector?label=Commits&logo=github)](https://github.com/ktsu-dev/ProjectDirector/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/ProjectDirector?label=Contributors&logo=github)](https://github.com/ktsu-dev/ProjectDirector/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/ProjectDirector/dotnet.yml?label=Build&logo=github)](https://github.com/ktsu-dev/ProjectDirector/actions)

## Introduction

ProjectDirector is a .NET desktop application for working across a large collection of related Git
repositories at once. It scans a local development directory, pairs what it finds with your remote
repositories on GitHub and Azure DevOps, and gives you a single window from which to fetch, pull,
diff, and propagate changes.

It exists for the case where the same file — a workflow, an editorconfig, a shared props file —
drifts across dozens of sibling repositories, and you want to see the differences and push one
canonical version out to the rest.

## Features

- **Repository Discovery**: Scans a configured development directory for local clones and lists
  them alongside your remote repositories.
- **Multi-Provider Remotes**: Browses repositories from GitHub (via Octokit) and Azure DevOps.
- **Bulk Git Operations**: Fetch and pull across many repositories, with per-repository status and
  timing.
- **Cross-Repository Diffing**: Compares a given file across every repository that contains it and
  shows a line-by-line diff.
- **File Propagation**: Copies a chosen version of a file out to the other repositories that have
  it, so a shared file can be reconciled in one step.
- **Three-Panel Layout**: Repository list on the left, repository detail and diff view on the
  right, and an operation log along the bottom.
- **Persistent State**: Development directory, credentials, repository cache, and UI layout are
  saved between sessions.

## Requirements

- .NET 9.0
- Windows (the application uses a Dear ImGui desktop window)

## Installation

```bash
git clone https://github.com/ktsu-dev/ProjectDirector.git
cd ProjectDirector
dotnet build
```

## Usage

```bash
# Run the application
dotnet run

# Publish a distributable build
dotnet publish --configuration Release --output ./staging
```

On first run, set the development directory that ProjectDirector should scan and supply GitHub
credentials if you want remote repositories listed. Both are persisted to the application data
folder, so subsequent runs start where you left off.

### Typical Workflow

1. Point ProjectDirector at your development directory.
2. Let it scan for local clones and fetch the remote repository list.
3. Select a repository to see its details, or select a file to diff it across every repository that
   has one.
4. Choose the version you want to keep and propagate it to the others.
5. Review the log panel, then commit and push from your normal Git tooling.

## Architecture

| Component | Responsibility |
| --- | --- |
| `ProjectDirector` | Main application class: ImGui loop, three-panel layout, and the fetch/pull/diff/propagate operations. |
| `ProjectDirectorOptions` | Application state persisted as JSON — dev directory, credentials, repository cache, and UI state. |
| `GitRepository` | Abstract repository model with polymorphic JSON serialization. |
| `GitHubRepository` / `AzureDevOpsRepository` | Provider-specific repository implementations. |
| `PopupPropagateFile` | Modal that drives the file propagation flow. |
| `DictionaryOfHashSets` | Helper collection for grouping repositories by file hash. |

### Key Dependencies

| Package | Used for |
| --- | --- |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) | Git operations — clone, fetch, pull, status |
| [Octokit](https://github.com/octokit/octokit.net) | GitHub API access |
| [DiffPlex](https://github.com/mmanela/diffplex) | Line-by-line file diffing |
| [ktsu.ImGuiApp](https://github.com/ktsu-dev/ImGuiApp) | Application shell and window management |
| [ktsu.AppDataStorage](https://github.com/ktsu-dev/AppDataStorage) | Persistent options storage |
| [ktsu.Semantics](https://github.com/ktsu-dev/Semantics) | Type-safe paths and identifiers |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
