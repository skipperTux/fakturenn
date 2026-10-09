---
paths:
  - "*.cs"
  - "*.cshtml"
  - "*.razor"
  - "*.resx"
  - "*.sln"
  - "*.slnx"
  - "*.csproj"
  - "*.props"
  - "*.targets"
  - "*.sh"
  - "*.cmd"
  - "*.bat"
  - "*.ps1"
  - ".editorconfig"
  - "*.gitattributes"
  - "**/*.cs"
  - "**/*.cshtml"
  - "**/*.razor"
  - "**/*.resx"
  - "**/*.sln"
  - "**/*.slnx"
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.targets"
  - "**/*.sh"
  - "**/*.cmd"
  - "**/*.bat"
  - "**/*.ps1"
  - "**/.editorconfig"
  - "**/*.gitattributes"
---

# Encoding and line endings

Follows dotnet/aspnetcore, with Visual Studio's own files as the exceptions.
`.editorconfig` steers editors, `Project.gitattributes` sets line endings, and
CI's Format job fails on a byte-order mark that does not match the file type.
This section is the reason, so that nobody "fixes" it back.

- **UTF-8 without a BOM** by default: `[*] charset = utf-8`.
- **UTF-8 with a BOM** for `*.razor` and `*.cshtml`, as aspnetcore does, and for
  `*.resx` and `*.sln`, which are rooted in Visual Studio and written by it with
  one.
- **Line endings are Git's job.** `* text=auto` stores LF and checks out each
  platform's native endings — LF on Linux, CRLF on Windows. `.editorconfig` sets
  no global `end_of_line`.
- **Pinned only where a tool demands it:** `*.sln`, `*.cmd` and `*.bat` CRLF,
  `*.sh` LF. The generated C# and Visual Studio templates also pin `*.csproj`,
  `*.props`, `*.slnx` and `*.ps1` to CRLF; `Project.gitattributes` lifts those
  with `!eol`, because no tool needs them and aspnetcore pins none of them.
  `*.slnx` is XML the dotnet CLI writes as often as Visual Studio does.

Why this and not a rule from Microsoft: there is none. Microsoft's repositories
disagree on the BOM — roslyn keeps it on `.cs`, runtime and aspnetcore do not —
and aspnetcore is the one whose stack, C# and Razor, matches this repository. The
compiler does not care: Roslyn decodes a file without a BOM as strict UTF-8 and
falls back to the ANSI code page only on invalid bytes. On line endings runtime,
aspnetcore, roslyn, sdk and efcore all agree on `* text=auto`.

What this replaced: a global `end_of_line = lf` in `.editorconfig` fighting the
templates' CRLF pins, so every tool that rewrote `Directory.Packages.props` or
the solution file printed `LF will be replaced by CRLF the next time Git touches
it`.

Do not add a BOM to a new `.cs` file, set a global `end_of_line`, or force
`eol=lf` repo-wide. The commit that normalized the BOMs is listed in
`.git-blame-ignore-revs`. GitHub's blame view reads that file on its own; for
local `git blame`, run
`git config blame.ignoreRevsFile .git-blame-ignore-revs` once per clone.
