# Requirements Document

## Introduction

This document captures requirements for a set of targeted quality improvements to the BSE-Code .NET CLI tool. The improvements address four categories of concern: test infrastructure (coverage instrumentation, untested components), runtime reliability (session persistence, error surfacing, input validation), security documentation (BashTool warnings, SECURITY.md), and developer-experience polish (cliff.toml portability, cross-platform pre-commit hooks, CI badge, dotnet format compliance). Together these changes raise the project's overall reliability, testability, and contributor confidence without altering any user-facing behaviour.

## Glossary

- **BSE_Code**: The .NET CLI tool that is the subject of this spec.
- **Core_Library**: The new `BSE_Code.Core` class library project that will hold all non-entry-point source files.
- **CLI_Project**: The existing `BSE_Code.csproj` executable project, reduced to a thin entry point after extraction.
- **Test_Project**: The existing `BSE_Code.Tests.csproj` xUnit project.
- **ReplEngine**: A new testable class extracted from `Program.cs` that owns the REPL loop, argument parsing, `@`-injection, `!`-passthrough, and tool-dispatch logic.
- **SlashCommandHandler**: The existing `SlashCommandHandler` class in `src/SlashCommandHandler.cs`.
- **InteractiveInput**: The existing `InteractiveInput` static class in `src/InteractiveInput.cs`.
- **SessionManager**: The existing `SessionManager` static class in `src/SessionManager.cs`.
- **BashTool**: The existing `BashTool` class in `src/Tools/BashTool.cs`.
- **McpManager**: The existing `McpManager` static class in `src/McpManager.cs`.
- **ConfigManager**: The existing `ConfigManager` static class in `src/ConfigManager.cs`.
- **AppConfig**: The `AppConfig` class whose `BaseUrl` property is persisted to `~/.bse-code/config.json`.
- **Coverlet**: The `coverlet.collector` NuGet package used for code-coverage instrumentation during `dotnet test`.
- **Pre_Commit_Hook**: The shell script at `.githooks/pre-commit` that runs formatting and tests before each commit.
- **cliff_toml**: The `cliff.toml` changelog-generation configuration file at the repository root.
- **CI_Workflow**: The GitHub Actions workflow defined under `.github/workflows/`.

---

## Requirements

### Requirement 1: Extract Core Library for Coverage Instrumentation

**User Story:** As a developer, I want Coverlet to produce accurate line-coverage data, so that I can identify untested code paths and track coverage trends over time.

#### Acceptance Criteria

1. THE Core_Library SHALL be a `<OutputType>Library</OutputType>` MSBuild project named `BSE_Code.Core.csproj` that compiles all source files currently listed under the `<Compile Include="../src/...">` items in the Test_Project.
2. THE CLI_Project SHALL reference the Core_Library via a `<ProjectReference>` and SHALL NOT duplicate any source files already compiled by the Core_Library.
3. THE Test_Project SHALL reference the Core_Library via a `<ProjectReference>` and SHALL remove all `<Compile Include="../src/...">` items that were previously used to pull source files in directly.
4. WHEN `dotnet test tests/BSE_Code.Tests.csproj --collect:"XPlat Code Coverage"` is executed, THE Coverlet SHALL produce a `coverage.cobertura.xml` report in which the line coverage percentage for the Core_Library assembly is greater than 0%.
5. WHEN the solution is built with `dotnet build BSE_Code.sln`, THE Build_System SHALL produce zero errors and zero warnings related to duplicate symbol definitions or missing project references.
6. THE Core_Library SHALL expose its internals to the Test_Project via an `[InternalsVisibleTo("BSE_Code.Tests")]` assembly attribute, preserving access to any `internal` members currently tested.

---

### Requirement 2: Extract ReplEngine from Program.cs

**User Story:** As a developer, I want the REPL logic isolated in a testable class, so that I can write unit tests for argument parsing, `@`-injection, `!`-passthrough, and the tool-dispatch loop without running the full CLI.

#### Acceptance Criteria

1. THE ReplEngine SHALL be a non-static class in the Core_Library that encapsulates the REPL loop, `RunTurnAsync`, `InjectAtPath`, `HandleMcpToolAsync`, `ValidateUnknownFlags`, and all UI-helper methods currently defined as local functions or static methods in `Program.cs`.
2. THE CLI_Project `Program.cs` SHALL be reduced to a top-level-statements entry point of no more than 50 lines that constructs a ReplEngine instance and delegates execution to it.
3. WHEN `ValidateUnknownFlags` is called with an array containing an unrecognised flag (e.g. `--foo`), THE ReplEngine SHALL throw an `ArgumentException` (or equivalent testable signal) rather than calling `Environment.Exit` directly, so that the behaviour is verifiable in unit tests.
4. WHEN `InjectAtPath` is called with a path to an existing file, THE ReplEngine SHALL return a string containing the file's content wrapped in a fenced code block.
5. WHEN `InjectAtPath` is called with a path to an existing directory, THE ReplEngine SHALL return a string listing up to 20 files from that directory with their contents.
6. IF `InjectAtPath` is called with a path that does not exist, THEN THE ReplEngine SHALL return `null` and emit an error message via `UI.Error`.
7. THE Test_Project SHALL contain at least one test class for ReplEngine covering `ValidateUnknownFlags`, `InjectAtPath` (file), `InjectAtPath` (directory), and `InjectAtPath` (missing path).

---

### Requirement 3: Add Test Coverage for SlashCommandHandler

**User Story:** As a developer, I want unit tests for SlashCommandHandler, so that regressions in command dispatch, model switching, memory refresh, and compact logic are caught automatically.

#### Acceptance Criteria

1. THE Test_Project SHALL contain a `SlashCommandHandlerTests` test class with tests covering at minimum: `/clear`, `/model <id>`, `/model` (no arg), `/save <tag>`, `/resume <tag>`, `/compact`, `/exit`, and an unknown command that falls through to skill invocation.
2. WHEN `/clear` is handled, THE SlashCommandHandler SHALL remove all non-system messages from the message list and return `0`.
3. WHEN `/model <id>` is handled, THE SlashCommandHandler SHALL update `_config.Model` to the new id, rebuild the client via `_buildClient`, and return `0`.
4. WHEN `/exit` is handled, THE SlashCommandHandler SHALL return `1`.
5. WHEN an unrecognised verb is handled and no matching skill exists, THE SlashCommandHandler SHALL print an "unknown command" message and return `0`.
6. WHEN `/compact` is handled with fewer than 3 user messages in history, THE SlashCommandHandler SHALL print a "not enough history" message and return `0` without invoking `_runTurn`.
7. THE SlashCommandHandler tests SHALL use injected `Func<>` delegates and in-memory message lists so that no real `ChatClient` or file I/O is required.

---

### Requirement 4: Add Test Coverage for InteractiveInput

**User Story:** As a developer, I want unit tests for the pure-logic parts of InteractiveInput, so that history deduplication, tab-completion filtering, and `GetSlashItems` matching are verified without a real TTY.

#### Acceptance Criteria

1. THE Test_Project SHALL contain an `InteractiveInputTests` test class.
2. WHEN a non-empty, non-duplicate line is submitted, THE InteractiveInput history SHALL contain that line exactly once at the end of the history list.
3. WHEN the same line is submitted consecutively, THE InteractiveInput history SHALL contain that line exactly once (no duplicate consecutive entries).
4. WHEN `GetSlashItems` is called with an empty filter, THE InteractiveInput SHALL return all built-in commands plus any loaded skills.
5. WHEN `GetSlashItems` is called with a filter string, THE InteractiveInput SHALL return only items whose label or value contains the filter string (case-insensitive).
6. WHEN `GetSlashItems` is called with a filter that matches no commands or skills, THE InteractiveInput SHALL return an empty list.
7. WHERE the `GetSlashItems` method is currently private, THE Core_Library SHALL expose it as `internal` so that the Test_Project can access it via `InternalsVisibleTo`.

---

### Requirement 5: Fix SessionManager Tool-Message Persistence

**User Story:** As a developer, I want sessions that involved tool calls to resume correctly, so that the model does not receive assistant messages that reference non-existent tool results.

#### Acceptance Criteria

1. WHEN `SessionManager.Save` is called with a message list that contains `AssistantChatMessage` entries with tool calls, THE SessionManager SHALL either persist the corresponding `ToolChatMessage` results alongside those assistant messages, OR strip the assistant messages that contain tool calls from the saved session.
2. WHEN `SessionManager.Resume` is called for a session that was saved under Acceptance Criterion 1, THE SessionManager SHALL return a message list in which every `AssistantChatMessage` that references a tool call is paired with its corresponding `ToolChatMessage` result, with no orphaned tool-call references.
3. THE Test_Project SHALL contain tests in `SessionManagerTests` that verify: (a) a session saved with tool-call messages resumes without orphaned references, and (b) a session saved with only text messages resumes unchanged.
4. IF a session file on disk contains an assistant message with tool calls but no corresponding tool result, THEN THE SessionManager SHALL silently drop that assistant message during `Resume` rather than returning a malformed history.

---

### Requirement 6: Document BashTool Security Constraints

**User Story:** As a contributor, I want clear documentation on BashTool's security boundaries, so that I understand the risks before modifying or extending the tool.

#### Acceptance Criteria

1. THE BashTool `RunShell` method SHALL have an XML doc comment (or inline block comment) that explicitly states: (a) the method executes arbitrary shell commands supplied by the LLM, (b) no input sanitization is performed, and (c) callers are responsible for ensuring the command originates from a trusted source.
2. THE BashTool `ExecuteAsync` method SHALL include an inline comment warning that the `command` argument is passed directly to the platform shell without sanitization.
3. THE SECURITY.md file SHALL exist at the repository root and SHALL document: (a) the fact that BSE-Code executes arbitrary shell commands via BashTool, (b) the fact that BSE-Code reads and writes arbitrary files via ReadFileTool and WriteFileTool, (c) the process for reporting security vulnerabilities (e.g. GitHub private security advisory), and (d) the scope of supported security guarantees.

---

### Requirement 7: Surface McpManager Errors Visibly

**User Story:** As a user, I want MCP tool failures to be clearly reported, so that I can diagnose MCP server crashes or malformed responses without confusion.

#### Acceptance Criteria

1. WHEN `McpManager.CallToolAsync` catches an exception, THE McpManager SHALL prefix the returned error string with `"ERROR: "` and SHALL also call `UI.Warn` with a human-readable description of the failure before returning.
2. WHEN `McpManager.SendMcpRequestAsync` receives a JSON-RPC error response (a response containing an `"error"` property), THE McpManager SHALL throw an exception whose message includes the raw error JSON, so that `CallToolAsync` can surface it via Acceptance Criterion 1.
3. WHEN `McpManager.CallToolAsync` receives a `null` result from `SendMcpRequestAsync` (timeout or empty response), THE McpManager SHALL return a string beginning with `"ERROR: No response"` and SHALL call `UI.Warn` with the server name.
4. THE Test_Project SHALL contain tests in `McpManagerTests` that verify the error-string prefix and `UI.Warn` invocation for at least: (a) a thrown exception, and (b) a null response.

---

### Requirement 8: Validate AppConfig.BaseUrl at Startup

**User Story:** As a user, I want a clear error message when my configured base URL is malformed, so that I can fix it immediately rather than receiving a cryptic exception on the first API call.

#### Acceptance Criteria

1. WHEN `ConfigManager.LoadOrSetupAsync` loads a saved configuration, THE ConfigManager SHALL validate `AppConfig.BaseUrl` using `Uri.TryCreate` with `UriKind.Absolute`.
2. IF `AppConfig.BaseUrl` fails the `Uri.TryCreate` check, THEN THE ConfigManager SHALL print a human-readable error message identifying the invalid URL and SHALL exit the process with a non-zero exit code before returning the config object.
3. WHEN `ConfigManager.LoadOrSetupAsync` is called and `AppConfig.BaseUrl` is a valid absolute URI, THE ConfigManager SHALL return the config object without error.
4. THE Test_Project SHALL contain tests in `ConfigManagerTests` that verify: (a) a valid URL passes validation, and (b) a malformed URL (e.g. `"not-a-url"`) triggers the error path.
5. WHERE the URL is supplied via the `BSE_BASE_URL` environment variable, THE ConfigManager SHALL apply the same validation before using the value.

---

### Requirement 9: Make cliff.toml Repository-Agnostic

**User Story:** As a contributor who forks or renames the repository, I want changelog commit links to resolve correctly, so that the generated changelog is not broken by a hardcoded GitHub path.

#### Acceptance Criteria

1. THE cliff_toml SHALL NOT contain a hardcoded repository path (e.g. `BibleSocietyEg/bse-code`) in the commit URL template.
2. THE cliff_toml SHALL use the `git-cliff` built-in `remote.github.owner` and `remote.github.repo` template variables (or equivalent dynamic substitution) to construct commit URLs, so that the correct URL is derived from the repository's configured remote at generation time.
3. WHEN `git-cliff` is run in a fork with a different remote URL, THE cliff_toml template SHALL produce commit links that point to the fork's repository rather than the original.

---

### Requirement 10: Add Cross-Platform Pre-Commit Hook Support

**User Story:** As a Windows contributor, I want the pre-commit hook to run on my machine, so that formatting and test checks are enforced consistently regardless of operating system.

#### Acceptance Criteria

1. THE repository SHALL contain a `.githooks/pre-commit.ps1` PowerShell script that performs the same checks as `.githooks/pre-commit` (dotnet format verification and dotnet test execution).
2. THE `.githooks/pre-commit` shell script SHALL be updated to detect when it is running on Windows without a POSIX shell and SHALL print a message directing the user to run `.githooks/pre-commit.ps1` manually or configure a cross-platform hook runner.
3. THE CONTRIBUTING.md SHALL document how Windows contributors can install and use the PowerShell pre-commit hook.
4. WHEN `.githooks/pre-commit.ps1` is executed on Windows and `dotnet format --verify-no-changes` exits with a non-zero code, THE Pre_Commit_Hook SHALL exit with a non-zero exit code and print a remediation message.
5. WHEN `.githooks/pre-commit.ps1` is executed on Windows and `dotnet test` exits with a non-zero code, THE Pre_Commit_Hook SHALL exit with a non-zero exit code and print a remediation message.

---

### Requirement 11: Add SECURITY.md

**User Story:** As a security researcher, I want a documented vulnerability-reporting process, so that I can responsibly disclose issues in a tool that executes shell commands and accesses the filesystem.

#### Acceptance Criteria

1. THE repository SHALL contain a `SECURITY.md` file at the root.
2. THE SECURITY.md SHALL describe the security-sensitive capabilities of BSE-Code: arbitrary shell command execution via BashTool, arbitrary file read/write via ReadFileTool and WriteFileTool, and MCP server subprocess spawning.
3. THE SECURITY.md SHALL specify the supported versions of BSE-Code that receive security fixes.
4. THE SECURITY.md SHALL provide instructions for reporting a vulnerability via GitHub's private security advisory feature.
5. THE SECURITY.md SHALL state the expected response timeline for security reports (e.g. acknowledgement within 7 days, fix timeline within 90 days for critical issues).

---

### Requirement 12: Fix dotnet format Compliance

**User Story:** As a contributor, I want `dotnet format --verify-no-changes` to pass on the existing codebase, so that the pre-commit hook and CI do not fail on code that was written before the `.editorconfig` was added.

#### Acceptance Criteria

1. WHEN `dotnet format --verify-no-changes` is run against the repository, THE Build_System SHALL exit with code 0 and report no formatting violations.
2. THE source files in `src/` and `tests/` SHALL conform to the indentation, spacing, and naming rules defined in `.editorconfig`.
3. THE CI_Workflow SHALL include a step that runs `dotnet format --verify-no-changes --no-restore` and fails the build if any violations are found.
4. IF the CI_Workflow already contains a `dotnet format` step, THEN THE CI_Workflow SHALL ensure that step runs before the test step so that formatting failures are reported first.

---

### Requirement 13: Add CI Status Badge to README

**User Story:** As a potential contributor, I want to see the CI build status at a glance in the README, so that I can quickly assess the health of the project before contributing.

#### Acceptance Criteria

1. THE `README.md` SHALL contain a CI status badge that links to the primary CI workflow run page on GitHub Actions.
2. THE CI badge SHALL use the standard GitHub Actions badge URL format: `https://github.com/{owner}/{repo}/actions/workflows/{workflow-file}/badge.svg`.
3. THE CI badge SHALL be placed in the badges section of the README alongside the existing NuGet and npm badges.
4. WHEN the CI workflow passes, THE CI badge SHALL display a "passing" status.
5. WHEN the CI workflow fails, THE CI badge SHALL display a "failing" status.
