# Security Policy

## Security-Sensitive Capabilities

BSE-Code exposes several capabilities that carry inherent security risk. Users should be aware of the following before running the tool:

- **BashTool — arbitrary shell execution**: The `BashTool` executes shell commands constructed by the LLM without any input sanitization or sandboxing. Any command the model requests will be run in the user's shell with the user's full privileges. Always review tool calls before approving them.

- **ReadFileTool / WriteFileTool — arbitrary file access**: These tools can read from and write to any path accessible to the current user. The LLM determines the target paths. Approving a write operation may overwrite or corrupt files.

- **MCP server subprocess spawning**: `McpManager` spawns external MCP server processes as subprocesses. A malicious or misconfigured MCP server entry in `~/.bse-code/config.json` could execute arbitrary code at startup.

## Supported Versions

Security fixes are applied to the **latest published release** only.

| Channel | Supported |
|---------|-----------|
| Latest NuGet release (`dotnet tool install bse-code`) | ✅ Yes |
| Latest npm release (`npm install -g bse-code`) | ✅ Yes |
| Older pinned versions | ❌ No — please upgrade |

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities privately via the GitHub Security Advisory form:

👉 **[Open a private security advisory](https://github.com/BibleSocietyEg/bse-code/security/advisories/new)**

You can expect:

- **Acknowledgement within 7 days** of submission.
- **A fix or mitigation within 90 days** for issues rated Critical or High severity.
- Credit in the release notes (unless you prefer to remain anonymous).

## Scope of Security Guarantees

BSE-Code is a **local developer tool**. It is designed to run on a developer's own machine and intentionally executes commands that the LLM requests on the user's behalf.

The tool does **not** provide:

- Sandboxing or isolation of LLM-generated commands.
- Content filtering or sanitization of tool arguments.
- Network egress controls for MCP servers.

**Users are responsible for reviewing every tool call before approving it.** The confirmation prompt shown before each tool execution is the primary security control. Do not run BSE-Code in an automated or unattended context where tool calls cannot be reviewed.
