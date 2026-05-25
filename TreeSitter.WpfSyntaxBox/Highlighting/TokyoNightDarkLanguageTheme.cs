namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Dark syntax theme using Tokyo Night-inspired colors.
/// </summary>
public sealed class TokyoNightDarkLanguageTheme : LanguageTheme
{
    /// <summary>
    /// Initializes brush mappings for all normalized highlight kinds used by the library.
    /// </summary>
    public TokyoNightDarkLanguageTheme()
    {
        var keyword = Brush("#BB9AF7");
        var stringLiteral = Brush("#9ECE6A");
        var comment = Brush("#565F89");
        var type = Brush("#2AC3DE");
        var number = Brush("#FF9E64");
        var punctuation = Brush("#89DDFF");
        var function = Brush("#7AA2F7");
        var variable = Brush("#C0CAF5");
        var constant = Brush("#FF9E64");

        this[HighlightKind.Keyword] = keyword;
        this[HighlightKind.String] = stringLiteral;
        this[HighlightKind.Number] = number;
        this[HighlightKind.Comment] = comment;
        this[HighlightKind.DocumentationComment] = comment;
        this[HighlightKind.Type] = type;
        this[HighlightKind.BuiltinType] = type;
        this[HighlightKind.Bracket] = punctuation;
        this[HighlightKind.Delimiter] = punctuation;
        this[HighlightKind.Operator] = punctuation;
        this[HighlightKind.Function] = function;
        this[HighlightKind.Method] = function;
        this[HighlightKind.Variable] = variable;
        this[HighlightKind.Property] = Brush("#7DCFFF");
        this[HighlightKind.Constant] = constant;
        this[HighlightKind.Module] = type;
        this[HighlightKind.Attribute] = Brush("#BB9AF7");
        this[HighlightKind.Tag] = Brush("#F7768E");
        this[HighlightKind.Boolean] = keyword;
        this[HighlightKind.StringEscape] = Brush("#BB9AF7");
        this[HighlightKind.StringSpecial] = Brush("#E0AF68");
        this[HighlightKind.StringRegex] = Brush("#B4F9F8");
        this[HighlightKind.FunctionBuiltin] = function;
        this[HighlightKind.FunctionMacro] = function;
        this[HighlightKind.VariableBuiltin] = variable;
        this[HighlightKind.Parameter] = Brush("#E0AF68");
        this[HighlightKind.Label] = Brush("#FF9E64");
        this[HighlightKind.Error] = Brush("#F7768E");
        this[HighlightKind.CommentTodo] = Brush("#7AA2F7");
        this[HighlightKind.CommentNote] = Brush("#0DB9D7");
        this[HighlightKind.CommentWarning] = Brush("#E0AF68");
        this[HighlightKind.CommentError] = Brush("#F7768E");
        this[HighlightKind.MarkupHeading] = keyword;
        this[HighlightKind.MarkupEmphasis] = type;
        this[HighlightKind.MarkupLink] = Brush("#73DACA");
        this[HighlightKind.MarkupList] = punctuation;
        this[HighlightKind.MarkupQuote] = comment;
        this[HighlightKind.MarkupRaw] = stringLiteral;
        this[HighlightKind.PunctuationSpecial] = punctuation;
        this[HighlightKind.TagAttribute] = Brush("#E0AF68");
        this[HighlightKind.TagDelimiter] = punctuation;
    }
}
