// Copyright (c) 2023-2026 ktsu-dev contributors

// ImGui keeps its context in global state, so only one harness can exist per process, and the
// application's settings and secret store seams are process-wide too. Sequential execution is a
// correctness requirement here, not a performance preference.
[assembly: DoNotParallelize]
