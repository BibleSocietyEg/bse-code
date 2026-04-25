# Contributing to BSE-Code

Thanks for your interest in contributing. Here's everything you need to get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Getting started

```sh
git clone https://github.com/BibleSocietyEg/bse-code
cd bse-code
dotnet restore
```

## Running the app locally

```sh
dotnet run --project BSE_Code.csproj
```

## Running tests

```sh
dotnet test tests/BSE_Code.Tests.csproj
```

With coverage:

```sh
dotnet test tests/BSE_Code.Tests.csproj --collect:"XPlat Code Coverage"
```

## Code formatting

This project uses `dotnet format`. Run it before committing:

```sh
dotnet format
```

To verify without making changes (same check CI runs):

```sh
dotnet format --verify-no-changes
```

A pre-commit hook is provided to automate this. Install it once:

```sh
cp .githooks/pre-commit .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
```

Or configure Git to use the hooks directory globally:

```sh
git config core.hooksPath .githooks
```

## Windows Contributors

On Windows, the standard shell pre-commit hook (`.githooks/pre-commit`) requires a POSIX-compatible shell (e.g. Git Bash). If you're using PowerShell natively, a dedicated PowerShell hook is provided.

**Install the hook** (works for both the shell and PowerShell hooks):

```powershell
git config core.hooksPath .githooks
```

**Run it manually at any time:**

```powershell
pwsh .githooks/pre-commit.ps1
```

**What it checks:**

1. `dotnet format --verify-no-changes` — ensures all code is correctly formatted
2. `dotnet test tests/BSE_Code.Tests.csproj -c Release` — runs the full test suite

If either check fails, the script prints a remediation message and exits with a non-zero code.

## Branch strategy

- `main` — stable, always releasable
- Feature branches — `feat/your-feature`, merged via PR
- Releases are triggered by pushing a `v*` tag (e.g. `v1.9.0`)

## Commit messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/) for automated changelog generation:

```
feat: add timeout support to BashTool
fix: handle empty BSE.md files in MemoryManager
docs: update provider list in README
test: add ToolCallAccumulator tests
chore: bump OpenAI SDK to 2.11.0
```

## Cutting a release

1. Update `<Version>` in `BSE_Code.csproj`
2. Commit: `chore: bump version to 1.9.0`
3. Tag and push: `git tag v1.9.0 && git push origin v1.9.0`
4. The release workflow handles the rest (tests → binaries → GitHub Release → npm → NuGet)

## Adding a new tool

1. Create `src/Tools/YourTool.cs` implementing `IToolHandler`
2. Register it in `ToolRegistry.CreateDefault()`
3. Add tests in `tests/Tools/YourToolTests.cs`
