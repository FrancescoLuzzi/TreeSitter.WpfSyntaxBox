using System.Text;
using TreeSitter.WpfSyntaxBox;

namespace TreeSitter.WpfSyntaxBox.Tests;

/// <summary>
/// Exercises randomized C# edits and rendering slices to protect incremental parsing and virtualization behavior.
/// </summary>
public sealed class CSharpFuzzTests
{
    /// <summary>
    /// Applies random source edits and verifies incremental parsing keeps highlight spans valid.
    /// </summary>
    [Fact]
    public void TreeSitterDocument_HandlesRandomCSharpEditsAndSlices()
    {
        var random = new Random(0x5EED_C5);
        var source = CreateCSharpSource();

        using var document = new TreeSitterDocument("CSharp");
        document.SetSource(source);

        for (var iteration = 0; iteration < 120; iteration++)
        {
            var start = random.Next(source.Length + 1);
            var removedLength = random.Next(Math.Min(48, source.Length - start) + 1);
            var insertedText = CSharpFragments[random.Next(CSharpFragments.Length)];
            source = source[..start] + insertedText + source[(start + removedLength)..];

            document.ApplyEdit(start, removedLength, insertedText);
            Assert.Equal(source, document.Source);

            var sliceStart = random.Next(source.Length + 1);
            var sliceLength = random.Next(Math.Min(1_000, source.Length - sliceStart) + 1);
            AssertValidSpans(document.HighlightSlice(sliceStart, sliceStart + sliceLength), source.Length);

            if (iteration % 40 == 0)
            {
                AssertValidSpans(document.Highlight(), source.Length);
            }
        }

        WpfScrollSimulation.SimulateCSharpUserScroll(source, steps: 16);
    }

    /// <summary>
    /// Verifies a generated C# document can be scrolled through a syntax-enabled text box.
    /// </summary>
    [Fact]
    public void SyntaxBox_SimulatesUserScrollingGeneratedCSharpTextBox()
    {
        WpfScrollSimulation.SimulateCSharpUserScroll(CreateCSharpSource());
    }

    /// <summary>
    /// Verifies highlight spans stay within source bounds and maintain byte-offset consistency.
    /// </summary>
    private static void AssertValidSpans(IReadOnlyList<HighlightSpan> spans, int sourceLength)
    {
        foreach (var span in spans)
        {
            Assert.InRange(span.Start, 0, sourceLength);
            Assert.InRange(span.End, span.Start + 1, sourceLength);
            Assert.Equal(span.Start * 2, span.StartByte);
            Assert.Equal(span.End * 2, span.EndByte);
        }
    }

    /// <summary>
    /// Generates deterministic C# source for fuzzing incremental parser edits.
    /// </summary>
    private static string CreateCSharpSource()
    {
        var builder = new StringBuilder(96_000);
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine("namespace TreeSitter.Tests.Fuzzing;");
        builder.AppendLine();

        for (var type = 0; type < 48; type++)
        {
            builder.AppendLine($"public sealed class FuzzProcessor{type:000}<TValue> where TValue : notnull");
            builder.AppendLine("{");
            builder.AppendLine($"    private readonly Dictionary<string, int> cache = new(StringComparer.Ordinal) {{ [\"seed\"] = {type} }};");
            builder.AppendLine();

            for (var method = 0; method < 3; method++)
            {
                builder.AppendLine($"    public int Compute{method}(string input, TValue value, int repeat)");
                builder.AppendLine("    {");
                builder.AppendLine($"        var total = input.Length + value.GetHashCode() + repeat + {type + method};");
                builder.AppendLine("        for (var i = 0; i < repeat; i++)");
                builder.AppendLine("        {");
                builder.AppendLine("            total += input.Contains(\"tree\", StringComparison.OrdinalIgnoreCase) ? i : -i;");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        return cache.TryGetValue(input, out var cached) ? cached : total;");
                builder.AppendLine("    }");
                builder.AppendLine();
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static readonly string[] CSharpFragments =
    [
        "public ",
        "private ",
        "class ",
        "record ",
        "struct ",
        "string ",
        "int ",
        "var ",
        "return ",
        "if (value is not null) ",
        "for (var j = 0; j < 3; j++) ",
        "{",
        "}",
        "(",
        ")",
        "[Obsolete]",
        "// fuzz comment\n",
        "/* fuzz block comment */",
        "\"fuzz string\"",
        "@$\"raw-ish {value}\"",
        "=>",
        "?.",
        "??",
        "nameof(value)",
        "Dictionary<string, List<int>>",
        "where TValue : notnull",
        "\n",
        "    ",
        string.Empty,
    ];
}
