# bse-code

An AI coding assistant CLI that works with **any LLM provider** — OpenRouter, OpenAI, Anthropic, Google AI, Ollama, LM Studio, Local AI Foundry, or any OpenAI-compatible endpoint. Understands natural language, reads and writes files, runs shell commands, supports MCP servers, skills, project memory, themes, and session management — right from your terminal.

## Install

Requires [Node.js 18+](https://nodejs.org).

```sh
npm install -g bse-code
```

## Supported Providers

| Provider | API Key Required |
|----------|-----------------|
| **OpenRouter** — 100+ models, free tier | Yes (free at openrouter.ai/keys) |
| **OpenAI** — GPT-4o, o3, o1 | Yes |
| **Anthropic** — Claude 3.7/3.5 | Yes |
| **Google AI** — Gemini 2.5 Pro/Flash | Yes (free tier) |
| **Ollama** — local models | No |
| **LM Studio** — local models | No |
| **Local AI Foundry** — local models | No |
| **Custom** — any OpenAI-compatible URL | Optional |

## First-run Setup

On first run, an interactive wizard will:

1. Ask you to **pick a provider**
2. Prompt for the **base URL** (pre-filled for known providers)
3. Ask for an **API key** (skipped for local providers)
4. Show **available models** and let you pick one
5. Save everything to `~/.bse-code/config.json`

Re-run the wizard any time:

```sh
bse-code --config
```

## Usage

```sh
bse-code                        # Interactive REPL
bse-code -p "<prompt>"          # One-shot prompt
bse-code --model <model-id>     # Override model for this session
bse-code --theme <name>         # Set color theme for this session
bse-code --output-format json   # JSON output (one-shot only)
bse-code --config               # Re-run setup wizard
bse-code --version              # Show version
bse-code --help                 # Show help
```

## Features

- **Any LLM provider** — cloud or fully local, no lock-in
- **Interactive REPL** with slash command picker, history navigation, and cursor editing
- **File injection** — prefix with `@` to inject file/directory contents into your prompt
- **Shell passthrough** — prefix with `!` to run shell commands directly
- **Skills** — reusable markdown instruction files in `~/.bse-code/skills/` or `.bse-code/skills/`
- **Project memory** — `BSE.md` files auto-loaded and injected into every session
- **MCP support** — connect external tools via `~/.bse-code/mcp.json`
- **Themes** — `default`, `dracula`, `monokai`, `ocean`, `forest`, `light`
- **Session management** — save and resume conversations

## Configuration

Config is stored at `~/.bse-code/config.json`:

```json
{
  "provider": "OpenRouter",
  "api_key": "sk-or-...",
  "model": "google/gemini-2.5-pro-exp-03-25:free",
  "base_url": "https://openrouter.ai/api/v1",
  "theme": "default"
}
```

For local providers (Ollama, LM Studio, etc.) no API key is needed:

```json
{
  "provider": "Ollama",
  "api_key": "local",
  "model": "llama3.2",
  "base_url": "http://localhost:11434/v1"
}
```

### Environment variables

| Variable | Description |
|----------|-------------|
| `BSE_PROVIDER` | Provider name (OpenRouter, OpenAI, Anthropic, Google, Ollama, LmStudio, LocalAiFoundry, Custom) |
| `BSE_API_KEY` | API key for the selected provider |
| `BSE_MODEL` | Model ID to use |
| `BSE_BASE_URL` | Override the API base URL |

## License

MIT
