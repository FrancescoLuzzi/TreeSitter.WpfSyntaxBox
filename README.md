# TreeSitter.net

TreeSitter.net is a WPF syntax-highlighting library built on the vendored `TreeSitter` .NET bindings under `vendor/tree-sitter-dotnet-bindings`. It provides an owned `SyntaxBox` layer for `TextBox` controls, parses source text with Tree-sitter, and incrementally updates the parse tree from WPF `TextChanged` edit ranges.

It is inspired by [`UI.SyntaxBox`](https://github.com/FLindqvist/UI.SyntaxBox) and vendors [`tree-sitter-dotnet-bindings`](https://github.com/mariusgreuel/tree-sitter-dotnet-bindings) for the managed Tree-sitter API and native grammar projects.

```xml
<syntax:SyntaxLanguage Language="CSharp" />
```

## Project Layout

```text
TreeSitter.net/
├── vendor/tree-sitter-dotnet-bindings/  # Vendored Tree-sitter .NET bindings and native grammars
├── TreeSitter.WpfSyntaxBox/              # WPF SyntaxBox implementation and Tree-sitter driver
├── TreeSitter.WpfSyntaxBox.Tests/        # SyntaxBox and parser integration tests
├── TreeSitter.WpfSyntaxBox.Demo/         # WPF demo application
├── justfile                             # Local automation
└── .github/workflows/ci.yml             # CI build/test pipeline
```

## Requirements

- Windows for building the WPF SyntaxBox implementation and demo.
- .NET SDK `10.0.x`.
- Optional: `just` for the provided command runner.
- Optional: `git-cliff` for local changelog generation.

## Installation

The package is published to GitHub Packages. Add the authenticated package source first, replacing `OWNER` and `YOUR_GITHUB_TOKEN` with values that can read packages from this repository.

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/OWNER/index.json" --name github --username OWNER --password YOUR_GITHUB_TOKEN --store-password-in-clear-text
```

Install the package into a project:

```powershell
dotnet add package TreeSitter.WpfSyntaxBox --source github
```

To install a specific version:

```powershell
dotnet add package TreeSitter.WpfSyntaxBox --version 1.2.3 --source github
```

## Build And Test

Recommended local workflow:

```powershell
just ci
```

Equivalent direct commands:

```powershell
dotnet restore TreeSitter.slnx
dotnet build TreeSitter.slnx --configuration Release --no-restore
dotnet test TreeSitter.slnx --configuration Release --no-build
```

## Publishing

Local package publishing uses the `publish` just recipe. Set `GITHUB_TOKEN` to a token that can write packages and `GITHUB_REPOSITORY_OWNER` to the GitHub package owner.

```powershell
$env:GITHUB_REPOSITORY_OWNER = "OWNER"
$env:GITHUB_TOKEN = "YOUR_GITHUB_TOKEN"
just publish 1.2.3
```

## Benchmarks

The benchmark project uses C# generated source because it exercises the more expensive grammar and highlighting path.

```powershell
dotnet run --project TreeSitter.WpfSyntaxBox.Benchmarks --configuration Release
```

## Architecture

The implementation has two layers.

`TreeSitter.WpfSyntaxBox` contains the WPF `SyntaxBox` attached behavior, renderer, template resources, rule/config types, key-sequence binding support, themes, and the Tree-sitter-backed `SyntaxLanguage` class intended for XAML.

`TreeSitterDocument` owns a `TreeSitter.Parser` and the active `TreeSitter.Tree`. Full parses are used for initial source loads. Text changes from WPF are applied with `Tree.Edit(Edit)` and then reparsed with `Parser.Parse(source, oldTree)` so unchanged subtrees can be reused.

## Supported Languages

Language support comes from the vendored Tree-sitter grammar libraries. The WPF driver currently maps these language names:

- `csharp`, `c#`, `cs`, `c_sharp`, `c-sharp`
- `lua`

## WPF SyntaxBox Usage

Add the TreeSitter namespace to your XAML, enable `SyntaxBox`, then set the syntax driver to `SyntaxLanguage`.

```xml
<TextBox
    xmlns:syntax="clr-namespace:TreeSitter.WpfSyntaxBox;assembly=TreeSitter.WpfSyntaxBox"
    syntax:SyntaxBox.Enable="True">
    <syntax:SyntaxBox.SyntaxDriver>
        <syntax:SyntaxLanguage Language="CSharp" />
    </syntax:SyntaxBox.SyntaxDriver>
</TextBox>
```

This replaces verbose rule-based syntax configuration such as manual `KeywordRule` and `RegexRule` declarations.

## Incremental Parsing

The renderer listens to WPF `TextChanged` events and passes the `TextChange` collection to incremental drivers. `SyntaxLanguage` forwards those edits to `TreeSitterDocument`, which translates WPF UTF-16 offsets into `TreeSitter.Edit` positions and reparses against the edited previous tree.

## Acknowledgments

- [Ui.SyntaxBox](https://github.com/FLindqvist/UI.SyntaxBox)
- [TreeSitter.DotNet](https://github.com/mariusgreuel/tree-sitter-dotnet-bindings)
- [nvim-treesitter](https://github.com/nvim-treesitter/nvim-treesitter)