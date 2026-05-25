using System;
using TreeSitterLanguage = global::TreeSitter.Language;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Normalizes user-facing language names into parser ids supported by the vendored tree-sitter bindings.
/// </summary>
internal static class TreeSitterLanguageFactory
{
    /// <summary>
    /// Creates a tree-sitter language for a supported language name.
    /// </summary>
    public static TreeSitterLanguage Create(string languageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageName);

        if (!TryGetSupportedLanguageId(languageName, out var id))
        {
            throw new NotSupportedException($"TreeSitter.DotNet does not include a parser for '{languageName}'.");
        }

        return new TreeSitterLanguage(id);
    }

    /// <summary>
    /// Returns the normalized tree-sitter language id or throws when the language is unsupported.
    /// </summary>
    public static string GetSupportedLanguageId(string languageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageName);

        return TryGetSupportedLanguageId(languageName, out var id)
            ? id
            : throw new NotSupportedException($"TreeSitter.DotNet does not include a parser for '{languageName}'.");
    }

    /// <summary>
    /// Attempts to normalize a language name into a supported tree-sitter language id.
    /// </summary>
    public static bool TryGetSupportedLanguageId(string languageName, out string languageId)
    {
        languageId = GetTreeSitterId(languageName) ?? string.Empty;
        return languageId.Length > 0;
    }

    /// <summary>
    /// Maps aliases and display names to tree-sitter package ids.
    /// </summary>
    private static string? GetTreeSitterId(string languageName) => languageName.Trim().ToLowerInvariant().Replace('_', '-') switch
    {
        "c#" or "cs" or "c-sharp" or "csharp" => "c-sharp",
        "c++" or "cpp" => "cpp",
        "embedded-template" or "embeddedtemplate" => "embedded-template",
        "markdown-inline" or "markdowninline" => "markdown-inline",
        "ocaml-interface" or "ocamlinterface" => "ocaml-interface",
        "query" or "tsq" => "query",
        "systemverilog" or "verilog" => "verilog",
        "typescript" or "ts" => "typescript",
        "tsx" => "tsx",
        "javascript" or "js" => "javascript",
        "json" => "json",
        "html" => "html",
        "css" => "css",
        "bash" or "sh" or "shell" => "bash",
        "lua" => "lua",
        "agda" => "agda",
        "c" => "c",
        "go" => "go",
        "haskell" => "haskell",
        "java" => "java",
        "jsdoc" => "jsdoc",
        "julia" => "julia",
        "markdown" => "markdown",
        "ocaml" => "ocaml",
        "php" => "php",
        "python" or "py" => "python",
        "ql" => "ql",
        "razor" => "razor",
        "ruby" => "ruby",
        "rust" => "rust",
        "scala" => "scala",
        "swift" => "swift",
        "toml" => "toml",
        _ => null,
    };
}
