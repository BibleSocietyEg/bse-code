# BSE_Code

An AI coding assistant CLI powered by an LLM via OpenRouter. It understands your prompts and can read files, write files, and run shell commands on your behalf.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An [OpenRouter](https://openrouter.ai) API key

---

## Setup

**1. Clone the repo**
```sh
git clone <repo-url>
cd <repo-folder>
```

**2. Pack**
```sh
dotnet pack BSE_Code.csproj -c Release -o ./nupkg
```

**3. Install globally**
```sh
dotnet tool install --global --add-source ./nupkg BSE_Code
```

To update after a code change: bump `<Version>` in the csproj, repack, then:
```sh
dotnet tool update --global --add-source ./nupkg BSE_Code
```

---

## First-run Configuration

On the first run, `bse-code` will launch an interactive setup wizard:

1. Prompts for your **OpenRouter API key** (get one free at https://openrouter.ai/keys)
2. Fetches the **live model list** from OpenRouter and displays them grouped by **Free** and **Paid**
3. Lets you pick a model by number
4. Saves everything to `~/.bse-code/config.json`

To re-run the wizard at any time:
```sh
bse-code --config
```

Config is stored at:
- **Windows**: `%USERPROFILE%\.bse-code\config.json`
- **Linux/macOS**: `~/.bse-code/config.json`

---

## Usage

Once installed, run from anywhere:
```sh
bse-code -p "<your prompt>"
```

---

## Examples

Ask a question:
```sh
bse-code -p "Explain what this codebase does"
```

Read a file:
```sh
bse-code -p "Read src/Program.cs and summarize it"
```

Write a file:
```sh
bse-code -p "Create a hello world C# file at hello.cs"
```

Run a shell command:
```sh
bse-code -p "List all .cs files in the project"
```

---

## Available Tools

| Tool        | Description                        |
|-------------|------------------------------------|
| `read_file` | Reads the contents of a file       |
| `Write`     | Writes content to a file           |
| `Bash`      | Executes a shell command           |

---

## Configuration

Environment variables override the saved config file:

| Environment Variable   | Required | Default                          | Description               |
|------------------------|----------|----------------------------------|---------------------------|
| `OPENROUTER_API_KEY`   | No*      | _(set via wizard)_               | Your OpenRouter API key   |
| `OPENROUTER_MODEL`     | No       | _(set via wizard)_               | Model ID to use           |
| `OPENROUTER_BASE_URL`  | No       | `https://openrouter.ai/api/v1`   | Override the API base URL |

\* Required only if config file doesn't exist yet.

**Persist via environment (PowerShell):**
```powershell
[System.Environment]::SetEnvironmentVariable('OPENROUTER_API_KEY', 'your-key', 'User')
[System.Environment]::SetEnvironmentVariable('OPENROUTER_MODEL',   'deepseek/deepseek-r1:free', 'User')
```

**Persist via environment (bash):**
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
