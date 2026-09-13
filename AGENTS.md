# AGENTS.md

## Agent Source Comments

Use the plugin `comment-hygiene@roboz0r` to check for common mistakes in agent source comments.
If it is not setup in your environment, ask your USER for permission to set it up.

## F# semantics — use `fslangmcp`, not grep

The repo ships an `fslangmcp` MCP server (`.mcp.json`, FsLangMCP over FSAC +
FSharp.Compiler.Service). It loads `Partas.Testing.slnx`, so it sees exactly the projects the
solution references.

Requires the `fslangmcp`, `fsautocomplete` and `fantomas` global tools; `fslangmcp
--bootstrap-tools` installs the pinned set.

If your environment doesn't have them, ask your USER for permission to set it up.

**Answer semantic questions about F# code with it. Reach for grep only for prose, JSON, `.mts`
tooling and other non-F# files.** Textual search in large source bases over-matches badly:
short binding names `create` can recur in thousands of members, and `find` resolves the real symbol instead.

- `find` — definitions and cross-project use sites, each tagged `definition`/`reference` with a
  coverage block. **Run `check` first.** When the workspace does not type-check, `find` returns
  `outcome="not_found"` *with* `coverage.complete: true` — a confidently wrong negative, not the
  indeterminate answer the coverage contract implies. Verified: `find "VirtualFileSystem"` matches
  on a clean tree and reports not_found on the same tree with unrelated compile errors. Never
  conclude "no usages" from a `find` taken while `check` says `errors`.
- `check` — fresh whole-workspace type-check verdict (`clean`/`errors`) with structured
  diagnostics, no build artifacts. Roughly 8s. An incremental `dotnet build Partas.Testing.slnx` is
  about as fast, so prefer `check` for the structured diagnostics, not for speed.
- `fcs_refactor_impact` — run this *before* changing any public signature. Returns blast radius,
  whether the symbol is public API (i.e. a breaking change), covering tests, and a verify list.
- `fcs_tests_for_symbol` — which tests cover a symbol, resolved to the enclosing Expecto
  `testCase` name. Only finds direct call sites; much of the suite exercises the library
  indirectly through the wire, so an empty result means "not called by name", not "untested".
- `fcs_public_api` — stable-ordered public surface, for diffing API before/after a change.
- `fcs_nuget_types` / `fcs_nuget_members` / `fcs_referenced_symbols` — inspect referenced
  assemblies without unpacking packages.

Pass `projectPath` explicitly on `fcs_*` calls when several agents run at once; caches are keyed
per resolved `.fsproj`. `fcs_dead_code` on some projects is dominated by generated-file
internals — treat its output as candidates to filter, not a work list.
