// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.ProjectDirector;

/// <summary>
/// Whether a pull can proceed immediately or should ask the user first.
/// </summary>
internal enum PullDecision
{
	/// <summary>
	/// The working tree is clean, so the pull can run without interrupting the user.
	/// </summary>
	PullNow,

	/// <summary>
	/// The working tree has uncommitted changes, so the user should be asked before pulling.
	/// </summary>
	Confirm,
}
