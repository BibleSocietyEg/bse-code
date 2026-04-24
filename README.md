# BSE-Code

An AI coding assistant CLI powered by OpenRouter. Understands natural language, reads and writes files, and runs shell commands — right from your terminal.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An [OpenRouter](https://openrouter.ai) API key (free tier available)

---

## Install

**1. Clone**
```sh
git clone <repo-url>
cd <repo-folder>
```

**2. Pack**
```sh
dotnet pack BSE_Code.csproj -c Release -o ./nupkg
```

**3. Install as a global tool**
```sh
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

Just run with no arguments to start a persistent multi-turn session:

```sh
bse-code
```

You'll get a prompt with your current folder and a conversation that keeps context across messages:

```
  ╭─────────────────────────────────────╮
  │         BSE-Code  ·  AI Coding      │
  ╰─────────────────────────────────────╯
  model  : google/gemini-2.5-pro-exp-03-25:free
  cwd    : my-project

  my-project ❯ explain what this repo does
```

### One-shot mode

Run a single prompt and exit:

```sh
bse-code -p "<prompt>"
```

### All flags

```sh
bse-code                        # Interactive REPL
bse-code -p "<prompt>"          # One-shot prompt
bse-code --model <model-id>     # Override model for this session
bse-code --config               # Re-run the setup wizard
bse-code --version, -v          # Show version
bse-code --help, -h             # Show help
```

---

## REPL Slash Commands

Inside the interactive session:

| Command  | Description                        |
|----------|------------------------------------|
| `/clear` | Clear conversation history         |
| `/model` | Show the current model             |
| `/help`  | List available slash commands      |
| `/exit`  | Quit                               |

---

## Examples

```sh
# Interactive session
bse-code

# One-shot prompts
bse-code -p "Explain what this codebase does"
bse-code -p "Read src/Program.cs and summarize it"
bse-code -p "Create a hello world C# file at hello.cs"
bse-code -p "List all .cs files in the project"

# Use a specific model just for this run
bse-code --model deepseek/deepseek-r1:free -p "Refactor this function"
```

---

## Available AI Tools

The model can invoke these tools on your machine:

| Tool        | Description                              |
|-------------|------------------------------------------|
| `read_file` | Read the contents of a file             |
| `Write`     | Write content to a file (creates dirs)  |
| `Bash`      | Execute a shell command                 |

Tool calls are shown inline with the file/command being used and a `✓`/`✗` result indicator.

---

## Configuration

Environment variables always override the saved config file:

| Variable               | Default                        | Description                  |
|------------------------|--------------------------------|------------------------------|
| `OPENROUTER_API_KEY`   | _(set via wizard)_             | Your OpenRouter API key      |
| `OPENROUTER_MODEL`     | _(set via wizard)_             | Model ID to use              |
| `OPENROUTER_BASE_URL`  | `https://openrouter.ai/api/v1` | Override the API base URL    |

**PowerShell (persist):**
```powershell
[System.Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'your-key', 'User')
[System.Environment]::SetEnvironmentVariable('OPENROUTER_MODEL',   'deepseek/deepseek-r1:free', 'User')
```

**Bash (persist):**
```sh
export OPENROUTER_API_KEY="your-key"
export OPENROUTER_MODEL="deepseek/deepseek-r1:free"
```

---

## Dependencies

| Package | Version |
|---------|---------|
| [OpenAI](https://www.nuget.org/packages/OpenAI) | 2.10.0 |

---

## License

MIT
