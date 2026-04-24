# bse-code

An AI coding assistant CLI powered by [OpenRouter](https://openrouter.ai). Understands natural language, reads and writes files, runs shell commands, supports MCP servers, skills, project memory, themes, and session management — right from your terminal.

## Install

Requires [Node.js 18+](https://nodejs.org).

```sh
npm install -g bse-code
```

## Prerequisites

An [OpenRouter](https://openrouter.ai) API key (free tier available).

## First-run Setup

On first run, an interactive wizard will ask for your API key, let you pick a model, and save everything to `~/.bse-code/config.json`.

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

- **Interactive REPL** with slash command picker, history navigation, and cursor editing
- **File injection** — prefix with `@` to inject file/directory contents into your prompt
- **Shell passthrough** — prefix with `!` to run shell commands directly
- **Skills** — reusable markdown instruction files in `~/.bse-code/skills/` or `.bse-code/skills/`
- **Project memory** — `BSE.md` files auto-loaded and injected into every session
- **MCP support** — connect external tools via `~/.bse-code/mcp.json`
- **Themes** — `default`, `dracula`, `monokai`, `ocean`, `forest`, `light`

## Configuration

Config is stored at `~/.bse-code/config.json`. You can also use environment variables:

| Variable | Description |
|----------|-------------|
| `OPENROUTER_API_KEY` | Your OpenRouter API key |
| `OPENROUTER_MODEL` | Model ID to use |
| `OPENROUTER_BASE_URL` | Override the API base URL |

## License

MIT
