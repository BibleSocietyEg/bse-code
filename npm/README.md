# 🤖 bse-code

> **The AI coding assistant that lives in your terminal — works with ANY LLM, zero lock-in, zero compromise.**

Chat with your codebase, read and write files, run shell commands, connect MCP servers, build reusable skills, persist project memory, and pick up right where you left off — all from a gorgeous interactive REPL.

[![npm](https://img.shields.io/npm/v/bse-code?style=flat-square&logo=npm&label=npm)](https://www.npmjs.com/package/bse-code)
[![NuGet](https://img.shields.io/nuget/v/bse-code?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/bse-code)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://github.com/BSE-Code/bse-code/blob/main/LICENSE)

---

## ✨ Why bse-code?

| | |
|---|---|
| 🌐 **Any LLM, anywhere** | OpenRouter, OpenAI, Anthropic, Google AI, Ollama, LM Studio, Local AI Foundry, or any OpenAI-compatible endpoint |
| 🆓 **Start for free** | OpenRouter's free tier gives you Gemini 2.5 Pro, Llama 4, DeepSeek R1 — no credit card |
| 🏠 **Fully local** | Ollama or LM Studio — no API key, no data leaving your machine |
| 🧠 **Context-aware** | Project memory, skills, and MCP tools injected automatically into every session |
| 💾 **Session persistence** | Save and resume conversations per project — never lose context again |
| 🎨 **Beautiful terminal UI** | 6 built-in themes, interactive slash picker, history navigation, full cursor editing |
| ⚡ **Instant shell access** | `!git status`, `!npm run build` — run any command without leaving the chat |
| 📂 **File injection** | `@src/auth.ts` — drop any file or directory straight into your prompt |

---

## 🚀 Install

Requires [Node.js 18+](https://nodejs.org). No .NET SDK needed — the binary is bundled.

```sh
npm install -g bse-code
```

> Also available as a .NET global tool: `dotnet tool install --global bse-code`

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
# Select [1] OpenRouter → get a free key at https://openrouter.ai/keys
# Pick Gemini 2.5 Pro, Llama 4, DeepSeek R1 — all free!
```

**🦙 Ollama — fully local, zero cost**
```sh
ollama pull llama3.2
bse-code --config
# Select [5] Ollama → accept default URL → pick your model
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

### `@` — File & directory injection
Drop any file or folder straight into your prompt. Tab-completes paths as you type.

```
@src/auth.ts explain this file
@src/auth/ summarize all files in this folder
@package.json what dependencies are outdated?
```

Directories inject up to 20 files automatically — perfect for asking about a whole module at once.

### `!` — Shell passthrough
Run any shell command instantly, no AI involved, output right in your terminal.

```
!git status
!npm run build
!ls -la src/
```

---

## ⌨️ REPL Slash Commands

### 🔧 Core
| Command | Description |
|---------|-------------|
| `/clear` | 🧹 Wipe conversation history — fresh start |
| `/model [id]` | 🤖 Show current model or switch mid-session |
| `/compact [hint]` | 🗜️ Summarize history and trim tokens |
| `/stats` | 📊 Session stats: duration, turns, tool calls, messages, model, provider, theme, skills, MCP tools |
| `/tools` | 🔧 List all available built-in and MCP tools |
| `/help` | ❓ Show all commands |
| `/exit` or `/quit` | 👋 Quit |

### 🎨 Appearance
| Command | Description |
|---------|-------------|
| `/theme` | 🎨 List all themes with active marker |
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
| `/memory refresh` | 🔄 Reload BSE.md files and refresh the system prompt |
| `/init` | 🎉 Scaffold a `BSE.md` in the current directory |

### 📁 Sessions
| Command | Description |
|---------|-------------|
| `/save <tag>` | 💾 Save the current conversation with a tag |
| `/resume` | 📂 List all saved sessions for this project |
| `/resume <tag>` | ▶️ Restore a saved session and pick up where you left off |

---

## 🎮 Interactive Input — Feels Like a Real Shell

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
- ↩️ **Enter** selects, **Esc** cancels
- ⇥ **Tab** completes the top match
- 🧠 **Your skills** appear right alongside built-in commands

### 📜 History & cursor editing
- ⬆️⬇️ arrows cycle through previous inputs — just like your shell
- ⬅️➡️ move the cursor anywhere in the line
- **Home / End** jump to start or end
- **Backspace / Delete** work at any cursor position
- ⇥ **Tab** on `@<path>` completes file and directory paths

---

## 🧠 Skills — Reusable AI Workflows

Skills are markdown files that give the AI reusable instructions or workflows. Write once, invoke from any project.

**📂 Locations (both loaded and merged):**
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
```

Invoke it:
```
/review
/review @src/PaymentService.ts
```

Skills are also injected into the system prompt automatically — the AI always knows what's available. 🚀

---

## 💾 Project Memory (BSE.md)

BSE.md files are loaded at startup and injected into every session's system prompt. Teach the AI about your project once — it remembers forever.

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

**Add notes on the fly:**
```
/memory add always use async/await, never .then() chains
/memory add run `npm test` before committing
```

---

## 🔌 MCP (Model Context Protocol)

Connect any external tool or service via MCP servers. GitHub, databases, Slack, custom APIs — if it speaks MCP, it works here.

**Config file:** `~/.bse-code/mcp.json`

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/dir"]
    },
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": { "GITHUB_PERSONAL_ACCESS_TOKEN": "your-token" }
    }
  }
}
```

- Tools available to the AI as `mcp__serverName__toolName`
- Hot-reload without restarting: `/mcp reload` 🔄
- Inspect what's connected: `/mcp`
- Disable without removing: `"disabled": true`

---

## 🔨 Built-in AI Tools

| Tool | What it does |
|------|-------------|
| 📖 `read_file` | Read any file's contents |
| ✏️ `Write` | Write or create a file (auto-creates parent directories) |
| 🖥️ `Bash` | Execute shell commands — cross-platform |
| 📂 `list_dir` | List files and subdirectories at a path |
| 🔍 `glob` | Find files matching a glob pattern (e.g. `src/**/*.ts`) |
| 🔎 `grep` | Search files with a regex pattern (up to 200 matches, recursive) |
| 🔌 `mcp__*__*` | Any tool from your connected MCP servers |

Tool calls are shown inline as the AI works — you see exactly what it's doing in real time. ✓ or ✗ per call.

---

## 💾 Session Management

Never lose a good conversation. Save any session with a tag and resume it later.

```
/save auth-refactor
```

```
/resume
#   auth-refactor   2025-04-24 14:32   18 messages   [gpt-4o]
#   bug-hunt        2025-04-23 09:15   31 messages   [claude-3-5-sonnet]

/resume auth-refactor
# ▶️  Resumed session 'auth-refactor' (18 messages) — welcome back!
```

Sessions are stored per-project in `~/.bse-code/sessions/` — isolated, no collisions.

---

## 📊 Session Statistics

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

The AI summarizes, history is trimmed, you keep going — same context, way fewer tokens. 🎯

---

## 🎨 Themes

| Theme | Accent | Vibe |
|-------|--------|------|
| `default` | 🩵 Cyan | Classic terminal |
| `dracula` | 💜 Magenta/Purple | Dark and moody |
| `monokai` | 💛 Yellow | Warm and punchy |
| `ocean` | 💙 Blue | Cool and calm |
| `forest` | 💚 Green | Fresh and focused |
| `light` | 🩵 Dark on light | For light terminals |

```sh
bse-code --theme dracula    # one session
# or inside the REPL:
/theme monokai              # persisted
```

---

## ⚙️ Configuration

**Config file:** `~/.bse-code/config.json`

```json
{
  "provider": "OpenRouter",
  "api_key": "sk-or-...",
  "model": "google/gemini-2.5-pro-exp-03-25:free",
  "base_url": "https://openrouter.ai/api/v1",
  "theme": "default"
}
```

### 🌍 Environment variables (always override config)

| Variable | Description |
|----------|-------------|
| `BSE_PROVIDER` | Provider name (`OpenRouter`, `OpenAI`, `Anthropic`, `Google`, `Ollama`, `LmStudio`, `LocalAiFoundry`, `Custom`) |
| `BSE_API_KEY` | API key for the selected provider |
| `BSE_MODEL` | Model ID to use |
| `BSE_BASE_URL` | Override the API base URL |

> 🔄 Legacy `OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `OPENROUTER_BASE_URL` still work as fallbacks.

---

## 📁 File Structure

```
~/.bse-code/
├── config.json          # ⚙️  Provider, API key, model, base URL, theme
├── mcp.json             # 🔌 MCP server definitions
├── BSE.md               # 🌍 Global memory
├── skills/
│   └── *.md             # 🧠 User-level skills
└── sessions/
    └── <project-hash>/  # 💾 Saved conversations per project

.bse-code/               # Project-level (commit to your repo)
├── BSE.md               # 📁 Project memory
└── skills/
    └── *.md             # 🧠 Project-level skills

./BSE.md                 # 📁 Project memory (root level)
./BSE.local.md           # 🔒 Local overrides — gitignore this
```

---

## 📄 License

MIT
