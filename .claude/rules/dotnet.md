---
paths:
  - "*.cs"
  - "*.csproj"
  - "*.props"
  - "*.razor"
  - "*.slnx"
  - "*.targets"
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.razor"
  - "**/*.slnx"
  - "**/*.targets"
---

# C# style

`.editorconfig` and `Directory.Build.props` carry everything a tool can check:
file-scoped namespaces, `using` placement and sorting, required braces,
accessibility modifiers, `readonly` fields, private-field naming, and IDE0005
and IDE0055 as errors. Warnings are errors repo-wide. This file is only what
those cannot express.

## Member order

StyleCop order: fields, properties, constructors, methods.

## Group comments

One line above each member group, naming the group exactly:

```csharp
// private static readonly Fields
// public Methods
```

Write them when the type has a constructor, or two or more groups, or several
methods. Do not write them when the type has a single member, or several members
in one non-method group — a model or POCO needs no signposting.

## Analyzers

`AnalysisLevel` is `latest-recommended` and warnings are errors, so an analyzer
ID in a suppression is a decision. Suppress at the narrowest scope that works —
a `[SuppressMessage]` on the one type, not a folder-wide `.editorconfig` entry —
and say why in the justification. A suppression whose cause nobody can name gets
widened by the next person who hits a symptom.

## Secrets

`dotnet user-secrets` for local development. Never `appsettings.json`.
