# 🤖 BSE-Code

> **The AI coding assistant that lives in your terminal — works with ANY LLM, zero lock-in, zero compromise.**

Chat with your codebase, read and write files, run shell commands, connect MCP servers, build reusable skills, persist project memory, and pick up right where you left off — all from a gorgeous interactive REPL.

[![NuGet](https://img.shields.io/nuget/v/bse-code?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/bse-code)
[![npm](https://img.shields.io/npm/v/bse-code?style=flat-square&logo=npm&label=npm)](https://www.npmjs.com/package/bse-code)
[![CI](https://github.com/BibleSocietyEg/bse-code/actions/workflows/ci.yml/badge.svg)](https://github.com/BibleSocietyEg/bse-code/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)

---

## ✨ Why BSE-Code?

| | |
|---|---|
| 🌐 **Any LLM, anywhere** | OpenRouter, OpenAI, Anthropic, Google AI, Ollama, LM Studio, Local AI Foundry, or any OpenAI-compatible endpoint |
| 🆓 **Start for free** | OpenRouter's free tier gives you Gemini 2.5 Pro, Llama 4, DeepSeek R1 — no credit card |
| 🏠 **Fully local** | Ollama or LM Studio — no API key, no data leaving your machine |
| 🧠 **Context-aware** | Project memory, skills, and MCP tools are all injected automatically into every session |
| 💾 **Session persistence** | Save and resume conversations per project — never lose context again |
| 🎨 **Beautiful terminal UI** | 6 built-in themes, interactive slash picker, history navigation, full cursor editing |
| ⚡ **Instant shell access** | `!git status`, `!npm run build` — run any command without leaving the chat |
| 📂 **File injection** | `@src/auth.ts` — drop any file or directory straight into your prompt |

---

## 🚀 Install

### via .NET tool (NuGet)

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet tool install --global bse-code
```

Update to the latest version:
```sh
dotnet tool update --global bse-code
```

### via npm

Requires [Node.js 18+](https://nodejs.org). No .NET SDK needed — the binary is bundled.

```sh
npm install -g bse-code
```

---

## 🌐 Supported Providers

| # | Provider | Models | API Key |
|---|----------|--------|---------|
| 1 | 🔀 **OpenRouter** | 100+ models, free tier available | Yes (free at openrouter.ai) |
| 2 | 🟢 **OpenAI** | GPT-4o, o3, o1, GPT-3.5 | Yes |
| 3 | 🟣 **Anthropic** | Claude 3.7/3.5 Sonnet, Haiku, Opus | Yes |
| 4 | 🔵 **Google AI** | Gemini 2.5 Pro/Flash, 2.0, 1.5 | Yes (free tier) |
| 5 | 🦙 **Ollama** | llama3, mistral, qwen, deepseek… | ❌ No (local) |
| 6 | 🖥️ **LM Studio** | Any model loaded in LM Studio | ❌ No (local) |
| 7 | 🏭 **Local AI Foundry** | Phi-4, Phi-3.5 Mini, and more | ❌ No (local) |
| 8 | ⚙️ **Custom** | Any OpenAI-compatible endpoint | Optional |

---

## 🧙 First-run Setup

On first run, an interactive wizard walks you through everything:

1. 🎯 Pick a **provider**
2. 🔗 Set the **base URL** (pre-filled for known providers)
3. 🔑 Enter your **API key** (skipped for local providers)
4. 🤖 Browse **available models** and pick one
5. 💾 Everything saved to `~/.bse-code/config.json`

Re-run the wizard any time:
```sh
bse-code --config
```

### ⚡ Quick-start by provider

**🔀 OpenRouter — free models, no credit card**
```sh
bse-code --config
# Select [1] OpenRouter
# Get a free key at: https://openrouter.ai/keys
# Pick Gemini 2.5 Pro, Llama 4, DeepSeek R1 — all free!
```

**🦙 Ollama — fully local, zero cost**
```sh
ollama pull llama3.2
bse-code --config
# Select [5] Ollama → accept default URL → pick your model
```

**🟢 OpenAI**
```sh
bse-code --config
# Select [2] OpenAI → https://platform.openai.com/api-keys
```

**🟣 Anthropic**
```sh
bse-code --config
# Select [3] Anthropic → https://console.anthropic.com/settings/keys
```

**🔵 Google AI (Gemini)**
```sh
bse-code --config
# Select [4] Google AI → https://aistudio.google.com/app/apikey
```

**🖥️ LM Studio**
```sh
# 1. Open LM Studio, load a model, start the local server
bse-code --config
# Select [6] LM Studio → accept default URL (http://localhost:1234/v1)
```

**🏭 Local AI Foundry**
```sh
bse-code --config
# Select [7] Local AI Foundry → accept default URL (http://localhost:5272/v1)
```

**⚙️ Custom endpoint**
```sh
bse-code --config
# Select [8] Custom → enter your URL, key, and model name
```

---

## 💻 Usage

### 🔁 Interactive REPL (recommended)

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
  model   : google/gemini-2.5-pro-exp-03-25:free
  theme   : default
  cwd     : my-project
  🧠 skills : 2 loaded
  🔌 mcp    : 5 tools from 1 server(s)
  💾 memory : 1 BSE.md file(s) loaded
  type /help for commands · /exit to quit 🚀

 my-project (main) ❯
```

### ⚡ One-shot mode

```sh
bse-code -p "explain the auth flow in src/auth/"
bse-code -p "list all TODO comments" --output-format json
```

### 🏳️ All CLI flags

```sh
bse-code                              # 🔁 Interactive REPL
bse-code -p "<prompt>"                # ⚡ One-shot prompt
bse-code --model <model-id>           # 🤖 Override model for this session
bse-code --theme <name>               # 🎨 Set color theme for this session
bse-code --output-format json|text    # 📄 Output format (one-shot only)
bse-code --config                     # ⚙️  Re-run the setup wizard
bse-code --version, -v                # 🔢 Show version
bse-code --help, -h                   # ❓ Show help
```

---

## 🪄 Special Input Prefixes

Two power-user shortcuts that make BSE-Code feel like a real dev tool:

### `@` — File & directory injection
Drop any file or folder straight into your prompt. Tab-completes paths as you type.

```
@src/auth.ts explain this file
@src/auth/ summarize all files in this folder
@README.md what's missing from this doc?
```

Directories inject up to 20 files automatically — perfect for asking about a whole module at once.

### `!` — Shell passthrough
Run any shell command instantly, no AI involved, output right in your terminal.

```
!git status
!dotnet build
!npm run test
!ls -la src/
```

---

## ⌨️ REPL Slash Commands

### 🔧 Core
| Command | Description |
|---------|-------------|
| `/clear` | 🧹 Wipe conversation history — fresh start |
| `/model [id]` | 🤖 Show current model or switch to a new one mid-session |
| `/compact [hint]` | 🗜️ Ask the AI to summarize history and trim tokens |
| `/stats` | 📊 Show session stats (duration, turns, tool calls, messages, model, provider, theme, skills, MCP tools) |
| `/tools` | 🔧 List all available built-in and MCP tools |
| `/help` | ❓ Show all commands |
| `/exit` or `/quit` | 👋 Quit |

### 🎨 Appearance
| Command | Description |
|---------|-------------|
| `/theme` | 🎨 List all available themes with active marker |
| `/theme <name>` | 🎨 Switch theme — persisted to config |

### 🧠 Skills
| Command | Description |
|---------|-------------|
| `/skills` | 📋 List all loaded skills (user + project level) |
| `/<skill-name>` | ▶️ Invoke a skill |
| `/<skill-name> @file.ts` | ▶️ Invoke a skill with a file argument |

### 🔌 MCP
| Command | Description |
|---------|-------------|
| `/mcp` | 🔌 List all connected MCP servers and their tools |
| `/mcp reload` | 🔄 Hot-reload MCP servers without restarting |

### 💾 Memory
| Command | Description |
|---------|-------------|
| `/memory` | 💾 Show all loaded BSE.md files |
| `/memory add <text>` | ✏️ Append a note to `./BSE.md` instantly |
| `/memory refresh` | 🔄 Reload all BSE.md files and refresh the system prompt |
| `/init` | 🎉 Scaffold a `BSE.md` in the current directory |

### 📁 Sessions
| Command | Description |
|---------|-------------|
| `/save <tag>` | 💾 Save the current conversation with a tag |
| `/resume` | 📂 List all saved sessions for this project |
| `/resume <tag>` | ▶️ Restore a saved session and pick up where you left off |

---

## 🎮 Interactive Input — Feels Like a Real Shell

The REPL has a fully interactive input reader. No more typing blind.

### `/` — Slash command picker
Type `/` and an inline menu pops up instantly:

```
  /  ↑↓ navigate · Enter select · Esc cancel
  ▶ /clear                🧹 clear conversation history
    /model                🤖 show or switch model
    /compact              🗜️  summarize history to save tokens
    /theme                🎨 list or set color theme
    /skills               🧠 list loaded skills
    /mcp                  🔌 list MCP servers and tools
    /memory               💾 show loaded BSE.md files
    /save                 💾 save conversation
    /resume               ▶️  list or resume a saved session
    …
```

- ⬆️⬇️ **Arrow keys** navigate the list
- ⌨️ **Type more characters** to filter live — `/th` narrows to `/theme`
- ↩️ **Enter** selects, **Esc** cancels and lets you type manually
- ⇥ **Tab** completes the top match
- 🧠 **Your skills** appear right alongside built-in commands

### 📜 History navigation
- ⬆️⬇️ arrows cycle through previous inputs — just like your shell
- Your current draft is preserved when you browse back

### ✏️ Full cursor editing
- ⬅️➡️ move the cursor anywhere in the line
- **Home / End** jump to start or end instantly
- **Backspace / Delete** work at any cursor position

### ⇥ Tab completion
- On `/<cmd>` — completes or opens the slash picker
- On `@<path>` — completes file and directory paths from the filesystem

---

## 🧠 Skills — Reusable AI Workflows

Skills are markdown files that give the AI reusable instructions or workflows. Write once, invoke from any project.

**📂 Locations (both are loaded and merged):**
- `~/.bse-code/skills/` — user-level, available in every project
- `.bse-code/skills/` — project-level, scoped to this repo

**Example skill** (`.bse-code/skills/review.md`):
```markdown
# Code Review

Review the provided code for:
- Correctness and logic errors
- Performance issues
- Security vulnerabilities
- Code style and readability

Provide specific, actionable feedback with line references.
```

Invoke it:
```
/review
/review @src/PaymentService.cs
```

Skills are also injected into the system prompt automatically — the AI always knows what skills are available. 🚀

---

## 💾 Project Memory (BSE.md)

BSE.md files are loaded automatically at startup and injected into every session's system prompt. Teach the AI about your project once — it remembers forever. Similar to Claude's `CLAUDE.md` and Gemini's `GEMINI.md`.

**🏗️ Hierarchy — all three are merged:**

| File | Scope |
|------|-------|
| `~/.bse-code/BSE.md` | 🌍 Global — your personal preferences across all projects |
| `./BSE.md` | 📁 Project — tech stack, commands, coding standards |
| `./BSE.local.md` | 🔒 Local overrides — add to `.gitignore` |

**Scaffold one instantly:**
```sh
bse-code
/init
```

This creates a `BSE.md` template with sections for project overview, tech stack, dev commands, and coding standards — ready to fill in.

**Add notes on the fly:**
```
/memory add always use async/await, never .Result or .Wait()
/memory add run `dotnet test` before committing
```

---

## 🔌 MCP (Model Context Protocol)

Connect any external tool or service to BSE-Code via MCP servers. GitHub, databases, Slack, custom APIs — if it speaks MCP, it works here. Tools are discovered automatically and made available to the AI.

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

- MCP tools are available to the AI as `mcp__serverName__toolName`
- Hot-reload without restarting: `/mcp reload` 🔄
- Inspect what's connected: `/mcp`
- Disable a server without removing it: `"disabled": true`

---

## 🔨 Built-in AI Tools

The AI can use these tools autonomously to get things done:

| Tool | What it does |
|------|-------------|
| 📖 `read_file` | Read any file's contents |
| ✏️ `Write` | Write or create a file (auto-creates parent directories) |
| 🖥️ `Bash` | Execute shell commands — cross-platform (cmd.exe on Windows, bash on Unix) |
| 📂 `list_dir` | List files and subdirectories at a path |
| 🔍 `glob` | Find files matching a glob pattern (e.g. `src/**/*.cs`) |
| 🔎 `grep` | Search files with a regex pattern (up to 200 matches, recursive by default) |
| 🔌 `mcp__*__*` | Any tool from your connected MCP servers |

Tool calls are shown inline as the AI works — you see exactly what it's doing in real time. ✓ or ✗ per call.

---

## 💾 Session Management

Never lose a good conversation. Save any session with a tag and resume it later — even across restarts.

```
/save auth-refactor
```

```
/resume
# shows all saved sessions for this project:
#   auth-refactor   2025-04-24 14:32   18 messages   [gpt-4o]
#   bug-hunt        2025-04-23 09:15   31 messages   [claude-3-5-sonnet]

/resume auth-refactor
# ▶️  Resumed session 'auth-refactor' (18 messages) — welcome back!
```

Sessions are stored per-project in `~/.bse-code/sessions/` using a SHA-256 hash of the project path — no collisions, no mess. Each session records the tag, model, timestamp, working directory, and full message history.

---

## 📊 Session Statistics

See exactly what's happening in your session:

```
/stats
```

```
  Session stats 📊
    ⏱  duration   : 00:23:41
    💬 turns      : 12
    🔧 tool calls : 34
    📨 messages   : 47
    🤖 model      : google/gemini-2.5-pro-exp-03-25:free
    🌐 provider   : OpenRouter
    🎨 theme      : dracula
    🧠 skills     : 3
    🔌 mcp tools  : 8
```

---

## 🗜️ Conversation Compaction

Running low on context? Compact the conversation into a tight summary without losing the important bits.

```
/compact
/compact focus on the auth changes we made
```

The AI summarizes the conversation, the history is trimmed, and you keep going — same context, way fewer tokens. 🎯

---

## 🎨 Themes

Six beautiful built-in themes. Switch any time, persisted automatically.

| Theme | Accent | Vibe |
|-------|--------|------|
| `default` | 🩵 Cyan | Classic terminal |
| `dracula` | 💜 Magenta/Purple | Dark and moody |
| `monokai` | 💛 Yellow | Warm and punchy |
| `ocean` | 💙 Blue | Cool and calm |
| `forest` | 💚 Green | Fresh and focused |
| `light` | 🩵 Dark on light | For light terminals |

Each theme customizes accent, prompt, response, tool calls, success/error states, skills, MCP, and git branch colors.

```
/theme dracula          # switch and persist
bse-code --theme ocean  # one session only
```

---

## ⚙️ Configuration

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

For local providers — no API key needed:
```json
{
  "provider": "Ollama",
  "api_key": "local",
  "model": "llama3.2",
  "base_url": "http://localhost:11434/v1",
  "theme": "forest"
}
```

### 🌍 Environment variables

Environment variables always override the config file — great for CI/CD or switching contexts fast.

| Variable | Description |
|----------|-------------|
| `BSE_PROVIDER` | Provider name (`OpenRouter`, `OpenAI`, `Anthropic`, `Google`, `Ollama`, `LmStudio`, `LocalAiFoundry`, `Custom`) |
| `BSE_API_KEY` | API key for the selected provider |
| `BSE_MODEL` | Model ID to use |
| `BSE_BASE_URL` | Override the API base URL |

> 🔄 Legacy variables `OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `OPENROUTER_BASE_URL` are still accepted for backwards compatibility.

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

## 📁 File Structure

```
~/.bse-code/
├── config.json          # ⚙️  Provider, API key, model, base URL, theme
├── mcp.json             # 🔌 MCP server definitions
├── BSE.md               # 🌍 Global memory (injected into every session)
├── skills/
│   └── *.md             # 🧠 User-level skills (available in all projects)
└── sessions/
    └── <project-hash>/  # 💾 Saved conversations, isolated per project
        └── *.json

.bse-code/               # Project-level (commit this to your repo)
├── BSE.md               # 📁 Project memory
└── skills/
    └── *.md             # 🧠 Project-level skills

./BSE.md                 # 📁 Project memory (root level, same as above)
./BSE.local.md           # 🔒 Local overrides — add to .gitignore
```

---

## 📦 Dependencies

| Package | Version |
|---------|---------|
| [OpenAI](https://www.nuget.org/packages/OpenAI) | 2.10.0 |

---

## 📄 License

MIT
