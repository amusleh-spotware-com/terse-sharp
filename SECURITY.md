# Security Policy

## Reporting a vulnerability

Report privately through GitHub Security Advisories:
**[Report a vulnerability](https://github.com/amusleh-spotware-com/terse-sharp/security/advisories/new)**.
Please do not open a public issue for a security problem.

Expect an acknowledgement within 5 working days.

## Threat model

TerseSharp runs **locally, as you**, and is driven by an AI agent. It makes no network calls, needs
no API key, and sends no telemetry. The security-relevant surface is therefore the file system and
the processes it starts.

| Control | Behaviour |
|---|---|
| Path containment | Every path argument is resolved and must sit inside the loaded workspace root, compared by whole path segment. `C:\repo` does **not** contain `C:\repoEvil`. |
| Read-only mode | `terse serve --read-only` makes every mutating tool refuse with `ERROR ReadOnly` and touch nothing. The tools are still listed; hiding them from `tools/list` is planned. |
| Edit safety | Mutations support `dryRun`, return diffs rather than files, and are rolled back when they introduce a new compile error. |
| Process execution | Only `dotnet build` / `dotnet test` against the loaded workspace, with a 10 minute deadline and a kill on timeout. There is no arbitrary-command tool. |
| Client config | `terse install` writes only the `terse-sharp` entry into an MCP client config, preserving everything else, via a temp file and an atomic rename. `terse uninstall` removes exactly that entry. |
| Secrets | None are read, stored or logged. Logs go to stderr, never stdout, which carries the MCP transport. |
| Publishing | Releases use NuGet trusted publishing (OIDC). No long-lived API key exists in the repository or in Actions secrets, so there is none to leak. |

## What TerseSharp does not protect you from

The agent driving it decides what to edit. `--read-only` is the control to use when you want
navigation without any possibility of a write. Anything the tool can reach, the agent can reach — so
run it on a workspace you are willing to have modified, and review diffs before committing.
