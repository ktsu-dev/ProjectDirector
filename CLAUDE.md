# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ProjectDirector is a .NET 9.0 Windows desktop application for managing and comparing multiple Git repositories. It provides a visual interface to scan local development directories, browse GitHub repositories, compare files across repos, and propagate changes between similar projects.

## Build Commands

```bash
# Build the project
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Run the application (GUI app - opens a window)
dotnet run

# Publish for distribution
dotnet publish --configuration Release --output ./staging
```

Tests live in `ProjectDirector.Test` (MSTest, via `MSTest.Sdk` + `ktsu.Sdk`). The app exposes its internals to the test project through `InternalsVisibleTo` in `ProjectDirector/AssemblyInfo.cs`. `GitCliTests` drives `GitCli` against throwaway repositories under the temp directory; the ImGui layer is not unit-tested.

```powershell
dotnet test --configuration Release
```

## Architecture

### Core Components

**[ProjectDirector.cs](ProjectDirector/ProjectDirector.cs)** - Main application class (~1600 lines)
- Entry point with ImGui application loop via `ktsu.ImGuiApp`
- Three-panel layout: left (repo list), right-top (repo details/diff), right-bottom (log)
- Key operations: fetch/pull repos, diff files across repos, propagate files

**[ProjectDirectorOptions.cs](ProjectDirector/ProjectDirectorOptions.cs)** - Application state
- Extends `AppData<T>` from ktsu.AppDataStorage for automatic JSON persistence
- Stores: dev directory path, GitHub credentials, repo cache, UI state (divider positions, panel states)
- Semantic string types for type-safe paths and identifiers

**[GitRepository.cs](ProjectDirector/GitRepository.cs)** - Repository abstraction
- Abstract base class with polymorphic JSON serialization (`[JsonDerivedType]`)
- Concrete implementations: `GitHubRepository`, `AzureDevOpsRepository`
- Tracks: remote/local paths, fetch timing, diff results against other repos

**[GitCli.cs](ProjectDirector/GitCli.cs)** - Git access
- `GitResult` (exit code plus both streams) and the runner that produces it, built on `ktsu.RunCommand`
- Arguments are passed as a list rather than as a command string, so paths containing spaces need no quoting
- `RunIn` uses `git -C <repo>`, which never touches the process working directory and so stays safe while repositories are fetched concurrently
- Queries answer from git's exit code rather than by searching its output for "fatal"

### Why the git command line rather than a library

Git LFS is a pair of filters plus a set of hooks, and all of them belong to the git command. A library that reads and writes the object database directly bypasses them: a commit stores raw bytes where a pointer belongs, and a clone or checkout lands the pointer text on disk where the file belongs. This application clones, fetches and pulls, so it is the checkout side that matters here. `ProjectDirector.Test` pins both halves down.

Authentication follows from the same decision. There are no credentials in this code, because git uses the platform credential helper, which is also what makes SSH remotes work.

### Key Dependencies

- **ktsu.RunCommand** - Starts the git command line, which is how all git work is done (see below)
- **Octokit** - GitHub API (list repos, user info)
- **DiffPlex** - Line-by-line file diffing
- **Hexa.NET.ImGui** - Immediate mode GUI framework
- **ktsu.ImGuiApp** - Application wrapper and window management
- **ktsu.AppDataStorage** - Persistent options storage in %APPDATA%

### Semantic Type Pattern

The codebase uses semantic string wrappers for type safety:
```csharp
public sealed record class GitHubOwnerName : SemanticString<GitHubOwnerName> { }
public sealed record class FullyQualifiedGitHubRepoName : SemanticString<FullyQualifiedGitHubRepoName> { }
public sealed record class FullyQualifiedLocalRepoPath : SemanticString<FullyQualifiedLocalRepoPath> { }
```

### UI Components

- `DividerContainer` - Resizable split panels (columns/rows)
- `PopupPropagateFile` - Modal dialog for copying files to multiple repos
- `ImGuiPopups.InputString` - Text input dialogs
- Collapsible panels with persisted open/closed state

### Data Flow

1. User sets dev directory → app scans for `.git` folders → discovers repos
2. User adds GitHub owners → app fetches repo list via Octokit API
3. Selected repo shows similar repos ranked by shared files
4. Comparing repos shows file diffs using DiffPlex
5. Changes can be applied to copy content between repos

## SDK Configuration

Uses custom ktsu MSBuild SDKs:
- `ktsu.Sdk` - Base configuration, analyzers, packaging
- `ktsu.Sdk.App` - GUI application settings (`OutputType=WinExe` on Windows)

The project targets `net9.0` only (not multi-targeted like library projects).
