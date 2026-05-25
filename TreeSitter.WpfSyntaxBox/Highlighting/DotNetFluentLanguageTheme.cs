namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Light syntax theme with familiar .NET editor colors.
/// </summary>
public sealed class DotNetFluentLanguageTheme : LanguageTheme
{
    /// <summary>
    /// Initializes brush mappings for all normalized highlight kinds used by the library.
    /// </summary>
    public DotNetFluentLanguageTheme()
    {
        var keyword = Brush("#0000FF");
        var stringLiteral = Brush("#A31515");
        var comment = Brush("#008000");
        var type = Brush("#2B91AF");
        var number = Brush("#098658");
        var punctuation = Brush("#393A34");
        var function = Brush("#795E26");
        var variable = Brush("#001080");
        var constant = Brush("#0070C1");

        this[HighlightKind.Keyword] = keyword;
        this[HighlightKind.String] = stringLiteral;
        this[HighlightKind.Number] = number;
        this[HighlightKind.Comment] = comment;
        this[HighlightKind.DocumentationComment] = Brush("#008080");
        this[HighlightKind.Type] = type;
        this[HighlightKind.BuiltinType] = type;
        this[HighlightKind.Bracket] = punctuation;
        this[HighlightKind.Delimiter] = punctuation;
        this[HighlightKind.Operator] = punctuation;
        this[HighlightKind.Function] = function;
        this[HighlightKind.Method] = function;
        this[HighlightKind.Variable] = variable;
        this[HighlightKind.Property] = variable;
        this[HighlightKind.Constant] = constant;
        this[HighlightKind.Module] = type;
        this[HighlightKind.Attribute] = Brush("#2B91AF");
        this[HighlightKind.Tag] = Brush("#800000");
        this[HighlightKind.Boolean] = keyword;
        this[HighlightKind.StringEscape] = Brush("#EE0000");
        this[HighlightKind.StringSpecial] = stringLiteral;
        this[HighlightKind.StringRegex] = stringLiteral;
        this[HighlightKind.FunctionBuiltin] = function;
        this[HighlightKind.FunctionMacro] = function;
        this[HighlightKind.VariableBuiltin] = variable;
        this[HighlightKind.Parameter] = variable;
        this[HighlightKind.Label] = Brush("#8F08C4");
        this[HighlightKind.Error] = Brush("#CD3131");
        this[HighlightKind.CommentTodo] = Brush("#0000FF");
        this[HighlightKind.CommentNote] = Brush("#008080");
        this[HighlightKind.CommentWarning] = Brush("#B05A00");
        this[HighlightKind.CommentError] = Brush("#CD3131");
        this[HighlightKind.MarkupHeading] = keyword;
        this[HighlightKind.MarkupEmphasis] = type;
        this[HighlightKind.MarkupLink] = Brush("#0451A5");
        this[HighlightKind.MarkupList] = punctuation;
        this[HighlightKind.MarkupQuote] = comment;
        this[HighlightKind.MarkupRaw] = stringLiteral;
        this[HighlightKind.PunctuationSpecial] = punctuation;
        this[HighlightKind.TagAttribute] = Brush("#E50000");
        this[HighlightKind.TagDelimiter] = punctuation;
    }
}
