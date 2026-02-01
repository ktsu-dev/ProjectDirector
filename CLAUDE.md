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

This project has no test suite.

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

### Key Dependencies

- **LibGit2Sharp** - Git operations (clone, fetch, pull, status)
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
