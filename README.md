# BSE-Code

An AI coding assistant CLI powered by OpenRouter. Understands natural language, reads and writes files, runs shell commands, supports MCP servers, skills, project memory, themes, and session management — right from your terminal.

---

## Prerequisites

- An [OpenRouter](https://openrouter.ai) API key (free tier available)

---

## Install

### via npm (recommended)

Requires [Node.js 18+](https://nodejs.org). No .NET SDK needed.

```sh
npm install -g bse-code
```

### via .NET tool

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet tool install --global --add-source ./nupkg BSE_Code
```

**To build and install from source:**

```sh
git clone <repo-url>
cd <repo-folder>
dotnet pack BSE_Code.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg BSE_Code
```

**To update** after a code change — bump `<Version>` in the csproj, repack, then:
```sh
dotnet tool update --global --add-source ./nupkg BSE_Code
```

---

## First-run Setup

On first run, an interactive wizard will:

1. Ask for your **OpenRouter API key** — get one free at https://openrouter.ai/keys
2. Fetch the **live model list** from OpenRouter, grouped by Free / Paid
3. Let you pick a model by number
4. Save everything to `~/.bse-code/config.json`

Re-run the wizard any time:
```sh
bse-code --config
```

Config location:
- **Windows**: `%USERPROFILE%\.bse-code\config.json`
- **Linux/macOS**: `~/.bse-code/config.json`

---

## Usage

### Interactive REPL (recommended)

```sh
bse-code
```

```
  ╭──────────────────────────────────────────╮
  │   ██████╗ ███████╗███████╗                │
  │   ██╔══██╗██╔════╝██╔════╝                │
  │   ██████╔╝███████╗█████╗   ─ code         │
  │   ██╔══██╗╚════██║██╔══╝                  │
  │   ██████╔╝███████║███████╗                │
  │   ╚═════╝ ╚══════╝╚══════╝                │
  ╰──────────────────────────────────────────╯
  model  : google/gemini-2.5-pro-exp-03-25:free
  theme  : default
  cwd    : my-project
  skills : 2 loaded
  mcp    : 5 tools from 1 server(s)
  memory : 1 BSE.md file(s) loaded
  type /help for commands · /exit to quit

 my-project (main) ❯ 
```

### One-shot mode

```sh
bse-code -p "<prompt>"
bse-code -p "<prompt>" --output-format json
```

### All flags

```sh
bse-code                              # Interactive REPL
bse-code -p "<prompt>"                # One-shot prompt
bse-code --model <model-id>           # Override model for this session
bse-code --theme <name>               # Set color theme for this session
bse-code --output-format json|text    # Output format (one-shot only)
bse-code --config                     # Re-run the setup wizard
bse-code --version, -v                # Show version
bse-code --help, -h                   # Show help
```

---

## REPL Slash Commands

### Core
| Command | Description |
|---------|-------------|
| `/clear` | Clear conversation history |
| `/model [id]` | Show or switch model |
| `/compact [hint]` | Summarize history to save tokens |
| `/stats` | Show session statistics |
| `/tools` | List available tools |
| `/help` | Show all commands |
| `/exit` | Quit |

### Appearance
| Command | Description |
|---------|-------------|
| `/theme` | List available themes |
| `/theme <name>` | Switch color theme (persisted) |

Built-in themes: `default`, `dracula`, `monokai`, `ocean`, `forest`, `light`

### Skills
| Command | Description |
|---------|-------------|
| `/skills` | List all loaded skills |
| `/<skill-name> [arg]` | Invoke a skill |

### MCP
| Command | Description |
|---------|-------------|
| `/mcp` | List MCP servers and tools |
| `/mcp reload` | Reload MCP servers |

### Memory (BSE.md)
| Command | Description |
|---------|-------------|
| `/memory` | Show loaded BSE.md files |
| `/memory add <text>` | Append a note to ./BSE.md |
| `/memory refresh` | Reload BSE.md files |
| `/init` | Create BSE.md in current directory |

### Sessions
| Command | Description |
|---------|-------------|
| `/save <tag>` | Save current conversation |
| `/resume [tag]` | List or resume a saved session |

---

## Special Input Prefixes

### `@` — File/directory injection (like Gemini CLI)
```
@src/Program.cs explain this file
@src/ summarize all source files
```
Injects file or directory contents directly into your prompt.

### `!` — Shell passthrough (like Gemini CLI)
```
!git status
!dotnet build
!ls -la
```
Runs a shell command directly without involving the AI.

---

## Skills

Skills are markdown files that provide reusable instructions or workflows.

**Locations:**
- `~/.bse-code/skills/` — user-level (available in all projects)
- `.bse-code/skills/` — project-level

**Example skill** (`.bse-code/skills/review.md`):
```markdown
# Code Review Skill

Review the provided code for:
- Correctness and logic errors
- Performance issues
- Security vulnerabilities
- Code style and readability

Provide specific, actionable feedback.
```

Invoke with `/review` or pass a file: `/review @src/MyClass.cs`

---

## Project Memory (BSE.md)

BSE.md files are loaded automatically and injected into the system prompt — similar to Claude's `CLAUDE.md` and Gemini's `GEMINI.md`.

**Hierarchy (all merged):**
1. `~/.bse-code/BSE.md` — global user preferences
2. `./BSE.md` — project-specific context
3. `./BSE.local.md` — local overrides (add to .gitignore)

**Create one:**
```sh
bse-code
/init
```

**Example BSE.md:**
```markdown
# My Project

## Tech Stack
- .NET 10, C#, ASP.NET Core

## Coding Standards
- Use record types for DTOs
- Prefer async/await throughout
- Write XML docs for public APIs

## Development Commands
dotnet run
dotnet test
```

---

## MCP (Model Context Protocol)

Connect external tools and services via MCP servers.

**Config file:** `~/.bse-code/mcp.json`

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/dir"],
      "disabled": false
    },
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "your-token"
      }
    }
  }
}
```

MCP tools are automatically available to the AI with the naming convention `mcp__serverName__toolName`.

---

## Available AI Tools

| Tool | Description |
|------|-------------|
| `read_file` | Read file contents |
| `Write` | Write/create a file (creates dirs) |
| `Bash` | Execute a shell command |
| `list_dir` | List directory contents |
| `glob` | Find files by glob pattern |
| `grep` | Search text in files |
| `mcp__*__*` | Any tool from connected MCP servers |

---

## Themes

| Theme | Description |
|-------|-------------|
| `default` | Cyan accent (classic terminal) |
| `dracula` | Magenta/purple (Dracula palette) |
| `monokai` | Yellow accent (Monokai inspired) |
| `ocean` | Blue accent (ocean tones) |
| `forest` | Green accent (forest tones) |
| `light` | Dark colors for light terminals |

Switch theme:
```
/theme dracula
```
Or for a single session:
```sh
bse-code --theme monokai
```

---

## Configuration

### Config file: `~/.bse-code/config.json`
```json
{
  "api_key": "sk-...",
  "model": "google/gemini-2.5-pro-exp-03-25:free",
  "base_url": "https://openrouter.ai/api/v1",
  "theme": "default"
}
```

### Environment variables (always override config file)

| Variable | Description |
|----------|-------------|
| `OPENROUTER_API_KEY` | Your OpenRouter API key |
| `OPENROUTER_MODEL` | Model ID to use |
| `OPENROUTER_BASE_URL` | Override the API base URL |

**PowerShell (persist):**
```powershell
[System.Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'your-key', 'User')
[System.Environment]::SetEnvironmentVariable('OPENROUTER_MODEL', 'deepseek/deepseek-r1:free', 'User')
```

**Bash (persist):**
```sh
export OPENROUTER_API_KEY="your-key"
export OPENROUTER_MODEL="deepseek/deepseek-r1:free"
```

---

## File Structure

```
~/.bse-code/
├── config.json          # Main config (API key, model, theme)
├── mcp.json             # MCP server definitions
├── BSE.md               # Global memory (injected into every session)
└── skills/
    └── *.md             # User-level skills

.bse-code/               # Project-level (in your repo)
├── BSE.md               # Project memory
└── skills/
    └── *.md             # Project-level skills

./BSE.md                 # Project memory (root level)
./BSE.local.md           # Local overrides (gitignore this)
```

---

## Dependencies

| Package | Version |
|---------|---------|
| [OpenAI](https://www.nuget.org/packages/OpenAI) | 2.10.0 |

---

## License

MIT
