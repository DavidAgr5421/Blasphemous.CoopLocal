# Project Guidelines

## Engram (Persistent Memory)
- At the start of tasks, check `mem_context` or `mem_search` for previous decisions and project context.
- Proactively call `mem_save` for any architectural decisions, bug fixes, or key discoveries.
- Call `mem_session_summary` before wrapping up sessions.

## CodeGraph
- This project is indexed with CodeGraph (`.codegraph/`). Always use `codegraph_explore` (MCP) or `codegraph explore` (CLI) as the primary tool to search, explore symbols, and trace call graphs before using grep or raw file scans.
