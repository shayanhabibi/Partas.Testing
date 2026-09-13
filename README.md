# Partas.Testing

## Build CLI

Every repository task runs through `build.fsx`, a [Partas.Build](https://github.com/shayanhabibi/partas.build)
script, so the tasks are typed, composable and discoverable:

```shell
dotnet fsi build.fsx -- --help
```

| Command | What it does |
|---------|--------------|
| `build` | Restores and builds the source projects |
| `test` | Cleans, then runs the Expecto suite (`--skip-tests` to skip it) |
| `format` | Formats every source file with Fantomas (`--dry-format` checks instead) |
| `publish` | Builds, tests, packs and pushes to NuGet (`--api-key`, or the `NUGET_API_KEY` env var) |
| `bump` | Bumps the version of a project |
| `docs` | Builds the Nacara site from `docs/docs.fsproj` (`--watch` to serve it) |

Global flags: `--quick` skips restores and cleaning,
`--format` formats before building, `--dry-format` checks formatting before building,
`-c` picks the configuration (default `Release`).

## Layout

```
build.fsx                      the build CLI
src/Partas.TestingPlatform/    the Microsoft.Testing.Platform binding
src/Partas.Testing/            the test framework
tests/Partas.Testing.Tests/    the Expecto suite
docs/docs.fsproj               the Nacara site (net10.0)
docs/content/                  the pages
```

### Adding a project

The build CLI addresses the repository through `Partas.TypeProvider.BuildHelper`,
so projects are discovered at compile time: anything under `src/` is a source
project, anything under `tests/` is a test project. Add the project to the
solution and it is picked up by `build`, `test`
and `pack`
on the next run.

### Adding a step

A step is a stage of a command. A stage that needs a flag binds it in an
`input { }` block, which is also what puts the flag into `--help`:

```fsharp
let myStep = input {
    let! quick = Options.quick
    return stage "my step" {
        when' (not quick)
        run "dotnet ..."
    }
}
```

Add it to any `command "..." { }` block. Because the condition lives in the
stage, the command carries no flags of its own, and adding the stage to a
second command registers `--quick` there too.
