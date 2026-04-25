# BSE-Code

An AI coding assistant CLI that works with **any LLM provider** — OpenRouter, OpenAI, Anthropic, Google AI, Ollama, LM Studio, Local AI Foundry, or any OpenAI-compatible endpoint. Understands natural language, reads and writes files, runs shell commands, supports MCP servers, skills, project memory, themes, and session management — right from your terminal.

---

## Supported Providers

| # | Provider | Models | API Key Required |
|---|----------|--------|-----------------|
| 1 | **OpenRouter** | 100+ models, free tier available | Yes (free) |
| 2 | **OpenAI** | GPT-4o, o3, o1, GPT-3.5 | Yes |
| 3 | **Anthropic** | Claude 3.7/3.5 Sonnet, Haiku, Opus | Yes |
| 4 | **Google AI** | Gemini 2.5 Pro/Flash, 2.0, 1.5 | Yes (free tier) |
| 5 | **Ollama** | llama3, mistral, qwen, deepseek… | No (local) |
| 6 | **LM Studio** | Any model loaded in LM Studio | No (local) |
| 7 | **Local AI Foundry** | Phi-4, Phi-3.5 Mini, and more | No (local) |
| 8 | **Custom** | Any OpenAI-compatible endpoint | Optional |

---

## Install

### via .NET tool (NuGet)

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet tool install --global BSE_Code
```

Update to the latest version:
```sh
dotnet tool update --global BSE_Code
```

### via npm

Requires [Node.js 18+](https://nodejs.org). No .NET SDK needed.

```sh
npm install -g bse-code
```

---

## First-run Setup

On first run, an interactive wizard will:

1. Ask you to **pick a provider** (OpenRouter, OpenAI, Ollama, etc.)
2. Prompt for the **base URL** (pre-filled for known providers, customizable for local ones)
3. Ask for an **API key** (skipped for local providers like Ollama/LM Studio)
4. Show the **available models** for that provider and let you pick one
5. Save everything to `~/.bse-code/config.json`

Re-run the wizard any time:
```sh
bse-code --config
```

Config location:
- **Windows**: `%USERPROFILE%\.bse-code\config.json`
- **Linux/macOS**: `~/.bse-code/config.json`

---

## Provider Quick-start

### OpenRouter (free models available)
```sh
bse-code --config
# Select [1] OpenRouter
# Get a free key at: https://openrouter.ai/keys
# Pick any free model (Gemini 2.5 Pro, Llama 4, DeepSeek R1…)
```

### OpenAI
```sh
bse-code --config
# Select [2] OpenAI
# Enter your key from https://platform.openai.com/api-keys
# Pick gpt-4o, o3-mini, etc.
```

### Anthropic
```sh
bse-code --config
# Select [3] Anthropic
# Enter your key from https://console.anthropic.com/settings/keys
# Pick Claude 3.7 Sonnet, Claude 3.5 Haiku, etc.
```

### Google AI (Gemini)
```sh
bse-code --config
# Select [4] Google AI
# Enter your key from https://aistudio.google.com/app/apikey
# Pick Gemini 2.5 Pro, Flash, etc.
```

### Ollama (fully local, no API key)
```sh
# 1. Install Ollama: https://ollama.com
ollama pull llama3.2
# 2. Configure bse-code
bse-code --config
# Select [5] Ollama
# Accept default URL (http://localhost:11434/v1)
# Pick from your pulled models
```

### LM Studio (fully local, no API key)
```sh
# 1. Open LM Studio, load a model, start the local server
# 2. Configure bse-code
bse-code --config
# Select [6] LM Studio
# Accept default URL (http://localhost:1234/v1)
# Enter the model name shown in LM Studio
```

### Local AI Foundry
```sh
bse-code --config
# Select [7] Local AI Foundry
# Accept default URL (http://localhost:5272/v1)
# Enter your deployed model name
```

### Custom / Any OpenAI-compatible endpoint
```sh
bse-code --config
# Select [8] Custom
# Enter your endpoint URL (e.g. http://my-server:8080/v1)
# Enter API key if required
# Enter model name
```

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
  provider: OpenRouter
  model  : google/gemini-2.5-pro-exp-03-25:free
  theme  : default
  cwd    : my-project
  🧠 skills : 2 loaded
  🔌 mcp    : 5 tools from 1 server(s)
  💾 memory : 1 BSE.md file(s) loaded
  type /help for commands · /exit to quit 🚀

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

## Interactive Input

The REPL uses a fully interactive input reader — no more typing commands blind.

### `/` — Slash command picker
Type `/` and an inline menu appears immediately:

```
  /  ↑↓ navigate · Enter select · Esc cancel
  ▶ /clear                🧹 clear conversation history
    /model                🤖 show or switch model
    /compact              🗜️  summarize history to save tokens
    /theme                🎨 list or set color theme
    /skills               🧠 list loaded skills
    …
```

- **Arrow keys** navigate the list
- **Type more characters** to filter live (e.g. `/th` narrows to `/theme`)
- **Enter** selects, **Esc** cancels and lets you type manually
- **Tab** completes the top match
- Skills are included automatically alongside built-in commands

### History navigation
- **↑ / ↓** arrows cycle through previous inputs (like a shell)
- Your draft is preserved when you browse back

### Cursor editing
- **← / →** move the cursor within the line
- **Home / End** jump to start/end
- **Backspace / Delete** work at any cursor position

### Tab completion
- On a `/...` input: completes or opens the picker
- On an `@...` input: completes file paths from the filesystem

---

## Special Input Prefixes

### `@` — File/directory injection
```
@src/Program.cs explain this file
@src/ summarize all source files
```
Injects file or directory contents directly into your prompt. Tab-completes paths.

### `!` — Shell passthrough
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

## Configuration

### Config file: `~/.bse-code/config.json`
```json
{
  "provider": "OpenRouter",
  "api_key": "sk-or-...",
  "model": "google/gemini-2.5-pro-exp-03-25:free",
  "base_url": "https://openrouter.ai/api/v1",
  "theme": "default"
}
```

> The `base_url` is set automatically by the setup wizard for each provider. You only need to change it manually for custom endpoints.

For local providers (Ollama, LM Studio, Local AI Foundry), `api_key` is not needed:
```json
{
  "provider": "Ollama",
  "api_key": "local",
  "model": "llama3.2",
  "base_url": "http://localhost:11434/v1",
  "theme": "default"
}
```

### Environment variables (always override config file)

| Variable | Description |
|----------|-------------|
| `BSE_PROVIDER` | Provider name (OpenRouter, OpenAI, Anthropic, Google, Ollama, LmStudio, LocalAiFoundry, Custom) |
| `BSE_API_KEY` | API key for the selected provider |
| `BSE_MODEL` | Model ID to use |
| `BSE_BASE_URL` | Override the API base URL |

> Legacy variables `OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `OPENROUTER_BASE_URL` are still accepted as fallbacks for backwards compatibility.

**PowerShell (persist):**
```powershell
[System.Environment]::SetEnvironmentVariable('BSE_PROVIDER', 'OpenAI', 'User')
[System.Environment]::SetEnvironmentVariable('BSE_API_KEY', 'your-key', 'User')
[System.Environment]::SetEnvironmentVariable('BSE_MODEL', 'gpt-4o', 'User')
```

**Bash (persist):**
```sh
export BSE_PROVIDER="Ollama"
export BSE_MODEL="llama3.2"
# No BSE_API_KEY needed for local providers
```

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

## File Structure

```
~/.bse-code/
├── config.json          # Main config (provider, api_key, model, base_url, theme)
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
