using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using TreeSitter.WpfSyntaxBox;

BenchmarkSwitcher.FromAssembly(typeof(CSharpTreeSitterBenchmarks).Assembly).Run(args);

/// <summary>
/// Benchmarks tree-sitter parsing, visible-slice highlighting, incremental edits, and WPF scroll rendering for generated C#.
/// </summary>
[MemoryDiagnoser]
public class CSharpTreeSitterBenchmarks
{
    private string source = string.Empty;
    private TreeSitterDocument document = null!;
    private int editOffset;
    private int editLength;
    private string editText = string.Empty;
    private int sliceStart;
    private int sliceEnd;
    private WpfTextBoxScrollHarness scrollHarness = null!;

    /// <summary>
    /// Gets or sets the number of generated C# types included in the benchmark source.
    /// </summary>
    [Params(250, 1_000)]
    public int TypeCount { get; set; }

    /// <summary>
    /// Builds benchmark inputs, initializes the parsed document, and prepares the WPF scroll harness.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        source = BenchmarkSourceFactory.CreateCSharpSource(TypeCount);
        document = new TreeSitterDocument("CSharp");
        document.SetSource(source);

        editText = "value += input.StartsWith(\"benchmark\", StringComparison.Ordinal) ? 17 : -17;";
        editOffset = source.IndexOf("return cache.TryGetValue", StringComparison.Ordinal);
        editLength = 0;

        var middle = source.Length / 2;
        sliceStart = Math.Max(0, middle - 8_000);
        sliceEnd = Math.Min(source.Length, middle + 8_000);
        scrollHarness = new WpfTextBoxScrollHarness(source);
    }

    /// <summary>
    /// Releases native parser state and the WPF harness window.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        document.Dispose();
        scrollHarness.Dispose();
    }

    /// <summary>
    /// Measures highlighting the entire parsed document.
    /// </summary>
    [Benchmark]
    public int FullHighlight()
    {
        return document.Highlight().Count;
    }

    /// <summary>
    /// Measures highlighting only a viewport-sized source slice.
    /// </summary>
    [Benchmark]
    public int VisibleSliceHighlight()
    {
        return document.HighlightSlice(sliceStart, sliceEnd).Count;
    }

    /// <summary>
    /// Measures an incremental tree edit followed by visible-slice highlighting.
    /// </summary>
    [Benchmark]
    public int IncrementalEditThenVisibleSliceHighlight()
    {
        using var edited = new TreeSitterDocument("CSharp");
        edited.SetSource(source);
        edited.ApplyEdit(editOffset, editLength, editText);
        return edited.HighlightSlice(sliceStart, sliceEnd + editText.Length).Count;
    }

    /// <summary>
    /// Measures scrolling through a syntax-enabled WPF text box.
    /// </summary>
    [Benchmark]
    public double UserScrollsSyntaxTextBox()
    {
        return scrollHarness.ScrollThroughDocument();
    }

}

/// <summary>
/// Selects which part of a generated document is used by position-sensitive benchmarks.
/// </summary>
public enum DocumentPosition
{
    /// <summary>
    /// Uses a range near the start of the source.
    /// </summary>
    Top,

    /// <summary>
    /// Uses a range near the middle of the source.
    /// </summary>
    Middle,

    /// <summary>
    /// Uses a range near the end of the source.
    /// </summary>
    Bottom,
}

/// <summary>
/// Benchmarks range highlighting at different document depths to expose viewport lookup costs.
/// </summary>
[MemoryDiagnoser]
public class CSharpVisibleSlicePositionBenchmarks
{
    private string source = string.Empty;
    private TreeSitterDocument document = null!;
    private int sliceStart;
    private int sliceEnd;

    /// <summary>
    /// Gets or sets the number of generated C# types included in the benchmark source.
    /// </summary>
    [Params(250, 1_000)]
    public int TypeCount { get; set; }

    /// <summary>
    /// Gets or sets the document region highlighted by the benchmark.
    /// </summary>
    [Params(DocumentPosition.Top, DocumentPosition.Middle, DocumentPosition.Bottom)]
    public DocumentPosition Position { get; set; }

    /// <summary>
    /// Builds a parsed document and selects the requested slice.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        source = BenchmarkSourceFactory.CreateCSharpSource(TypeCount);
        document = new TreeSitterDocument("CSharp");
        document.SetSource(source);
        (sliceStart, sliceEnd) = BenchmarkSourceFactory.GetSlice(source, Position, width: 16_000);
    }

    /// <summary>
    /// Releases native tree-sitter resources.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        document.Dispose();
    }

    /// <summary>
    /// Measures highlighting a viewport-sized range at a selected document position.
    /// </summary>
    [Benchmark]
    public int VisibleSliceHighlightByPosition()
    {
        return document.HighlightSlice(sliceStart, sliceEnd).Count;
    }
}

/// <summary>
/// Benchmarks steady-state incremental edit application at different document depths.
/// </summary>
[MemoryDiagnoser]
public class CSharpIncrementalEditPositionBenchmarks
{
    private string source = string.Empty;
    private string currentText = string.Empty;
    private IReadOnlyList<TextChange> textChanges = [];
    private TreeSitterDocument document = null!;
    private int editOffset;
    private const int EditLength = 0;
    private const string EditText = "value += input.StartsWith(\"benchmark\", StringComparison.Ordinal) ? 17 : -17;";

    /// <summary>
    /// Gets or sets the number of generated C# types included in the benchmark source.
    /// </summary>
    [Params(250, 1_000)]
    public int TypeCount { get; set; }

    /// <summary>
    /// Gets or sets the document region edited by the benchmark.
    /// </summary>
    [Params(DocumentPosition.Top, DocumentPosition.Middle, DocumentPosition.Bottom)]
    public DocumentPosition Position { get; set; }

    /// <summary>
    /// Builds immutable benchmark input shared by edit iterations.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        source = BenchmarkSourceFactory.CreateCSharpSource(TypeCount);
        editOffset = BenchmarkSourceFactory.GetEditOffset(source, Position);
        textChanges = BenchmarkSourceFactory.CreateInsertionChange(source, editOffset, EditText, out currentText);
    }

    /// <summary>
    /// Creates a freshly parsed document before each measured edit.
    /// </summary>
    [IterationSetup]
    public void IterationSetup()
    {
        document = new TreeSitterDocument("CSharp");
        document.SetSource(source);
    }

    /// <summary>
    /// Releases the document edited during the iteration.
    /// </summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        document.Dispose();
    }

    /// <summary>
    /// Measures the public edit API used by direct callers.
    /// </summary>
    [Benchmark]
    public int ApplyEditByPosition()
    {
        document.ApplyEdit(editOffset, EditLength, EditText);
        return document.Source.Length;
    }

    /// <summary>
    /// Measures the WPF text-change API used by the syntax renderer after typing.
    /// </summary>
    [Benchmark]
    public int ApplyTextChangesByPosition()
    {
        document.ApplyTextChanges(textChanges, currentText);
        return document.Source.Length;
    }
}

/// <summary>
/// Benchmarks injection-heavy highlighting where comment tokens and URLs are common.
/// </summary>
[MemoryDiagnoser]
public class InjectionHighlightBenchmarks
{
    private string source = string.Empty;
    private TreeSitterDocument document = null!;
    private int sliceStart;
    private int sliceEnd;

    /// <summary>
    /// Builds an injection-heavy parsed C# source.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        source = BenchmarkSourceFactory.CreateInjectionHeavyCSharpSource(500);
        document = new TreeSitterDocument("CSharp");
        document.SetSource(source);
        (sliceStart, sliceEnd) = BenchmarkSourceFactory.GetSlice(source, DocumentPosition.Middle, width: 20_000);
    }

    /// <summary>
    /// Releases native tree-sitter resources.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        document.Dispose();
    }

    /// <summary>
    /// Measures query highlighting for a slice with many comment injections and URL captures.
    /// </summary>
    [Benchmark]
    public int HighlightInjectionHeavySlice()
    {
        return document.HighlightSlice(sliceStart, sliceEnd).Count;
    }
}

/// <summary>
/// Benchmarks the simple keyword rule path independently from tree-sitter.
/// </summary>
[MemoryDiagnoser]
public class KeywordRuleBenchmarks
{
    private readonly KeywordRule rule = new()
    {
        Keywords = "abstract,as,base,bool,break,byte,case,catch,char,checked,class,const,continue,decimal,default,delegate,do,double,else,enum,event,explicit,extern,false,finally,fixed,float,for,foreach,goto,if,implicit,in,int,interface,internal,is,lock,long,namespace,new,null,object,operator,out,override,params,private,protected,public,readonly,ref,return,sbyte,sealed,short,sizeof,stackalloc,static,string,struct,switch,this,throw,true,try,typeof,uint,ulong,unchecked,unsafe,ushort,using,virtual,void,volatile,while,get,set,yield,var",
    };

    private string input = string.Empty;

    /// <summary>
    /// Builds repeated visible-line-like input for keyword scanning.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        input = BenchmarkSourceFactory.CreateKeywordInput(500);
    }

    /// <summary>
    /// Measures keyword matching allocations and throughput.
    /// </summary>
    [Benchmark]
    public int MatchKeywords()
    {
        return rule.Match(input).Count();
    }
}

/// <summary>
/// Benchmarks line-index operations used by virtualized rendering.
/// </summary>
[MemoryDiagnoser]
public class TextLineIndexBenchmarks
{
    private string source = string.Empty;
    private TextLineIndex index = null!;
    private string editedSource = string.Empty;
    private int editOffset;
    private int editLength;
    private int firstLine;
    private int lastLine;

    /// <summary>
    /// Gets or sets the generated document size.
    /// </summary>
    [Params(10_000, 100_000)]
    public int LineCount { get; set; }

    /// <summary>
    /// Builds a large line-oriented source and line index.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        source = BenchmarkSourceFactory.CreateLineSource(LineCount);
        index = TextLineIndex.Build(source);
        editOffset = source.Length / 2;
        const string inserted = "inserted alpha\ninserted beta\n";
        editLength = inserted.Length;
        editedSource = source.Insert(editOffset, inserted);
        firstLine = Math.Max(0, LineCount - 260);
        lastLine = Math.Min(LineCount - 1, firstLine + 240);
    }

    /// <summary>
    /// Measures the current renderer-style retrieval of bottom document line descriptors.
    /// </summary>
    [Benchmark]
    public int GetBottomLineRange()
    {
        var lines = index.GetLineRange(source, firstLine, lastLine);
        var length = 0;
        foreach (var line in lines)
        {
            length += line.Text.Length;
        }

        return length;
    }

    /// <summary>
    /// Measures renderer-style retrieval of bottom document line spans without substring allocation.
    /// </summary>
    [Benchmark]
    public int GetBottomLineSpans()
    {
        var lines = index.GetLineSpans(source, firstLine, lastLine);
        var length = 0;
        foreach (var line in lines)
        {
            length += line.Length;
        }

        return length;
    }

    /// <summary>
    /// Measures retrieving context highlight bounds without allocating line descriptors.
    /// </summary>
    [Benchmark]
    public int GetBottomRangeBounds()
    {
        var (start, end) = index.GetRangeBounds(firstLine, lastLine, source.Length);
        return end - start;
    }

    /// <summary>
    /// Measures full line-index rebuilding after a text change.
    /// </summary>
    [Benchmark]
    public int RebuildLineIndex()
    {
        return TextLineIndex.Build(source).Count;
    }

    /// <summary>
    /// Measures incremental line-index update for one inserted text change.
    /// </summary>
    [Benchmark]
    public int ApplySingleLineIndexChange()
    {
        return index.ApplyChange(editedSource, editOffset, removedLength: 0, addedLength: editLength).Count;
    }
}

/// <summary>
/// Creates deterministic source inputs shared by benchmark classes.
/// </summary>
internal static class BenchmarkSourceFactory
{
    /// <summary>
    /// Generates large deterministic C# input for parser and renderer benchmarks.
    /// </summary>
    public static string CreateCSharpSource(int typeCount)
    {
        var builder = new StringBuilder(typeCount * 1_500);
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine("namespace TreeSitter.Benchmarks.Generated;");
        builder.AppendLine();

        for (var type = 0; type < typeCount; type++)
        {
            builder.AppendLine($"[Serializable]");
            builder.AppendLine($"public sealed class GeneratedProcessor{type:0000}<TValue> where TValue : notnull");
            builder.AppendLine("{");
            builder.AppendLine($"    private readonly Dictionary<string, int> cache = new(StringComparer.Ordinal) {{ [\"seed\"] = {type} }};");
            builder.AppendLine();

            for (var method = 0; method < 4; method++)
            {
                builder.AppendLine($"    public int Compute{method}(string input, TValue value, int repeat)");
                builder.AppendLine("    {");
                builder.AppendLine($"        var total = input.Length + value.GetHashCode() + repeat + {type + method};");
                builder.AppendLine("        for (var i = 0; i < repeat; i++)");
                builder.AppendLine("        {");
                builder.AppendLine("            total += input.Contains(\"tree\", StringComparison.OrdinalIgnoreCase) ? i : -i;");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        if (total % 3 == 0)");
                builder.AppendLine("        {");
                builder.AppendLine($"            cache[input] = total + {method};");
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

    /// <summary>
    /// Generates C# with many comments, TODO-like tokens, issue numbers, and URLs for injection benchmarks.
    /// </summary>
    public static string CreateInjectionHeavyCSharpSource(int blockCount)
    {
        var builder = new StringBuilder(blockCount * 400);
        builder.AppendLine("namespace TreeSitter.Benchmarks.Injections;");
        builder.AppendLine();

        for (var block = 0; block < blockCount; block++)
        {
            builder.AppendLine($"// TODO: benchmark block #{block} https://example.com/issues/{block}");
            builder.AppendLine($"public sealed class InjectionCase{block:0000}");
            builder.AppendLine("{");
            builder.AppendLine($"    // PERF: generated URL https://example.com/perf/{block} #123");
            builder.AppendLine($"    public string Value => \"https://example.com/value/{block}\";");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Generates repeated C#-like lines for keyword-rule benchmarks.
    /// </summary>
    public static string CreateKeywordInput(int lineCount)
    {
        var builder = new StringBuilder(lineCount * 100);
        for (var i = 0; i < lineCount; i++)
        {
            builder.AppendLine($"public sealed class Demo{i:0000} {{ private string? value; public void Run() {{ if (value is null) return; }} }}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Generates fixed-width line text for line-index benchmarks.
    /// </summary>
    public static string CreateLineSource(int lineCount)
    {
        var builder = new StringBuilder(lineCount * 32);
        for (var i = 0; i < lineCount; i++)
        {
            builder.AppendLine($"line {i:000000} value value value");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns a viewport-sized slice centered around a selected document position.
    /// </summary>
    public static (int Start, int End) GetSlice(string source, DocumentPosition position, int width)
    {
        var center = position switch
        {
            DocumentPosition.Top => Math.Min(source.Length, width / 2),
            DocumentPosition.Middle => source.Length / 2,
            DocumentPosition.Bottom => Math.Max(0, source.Length - (width / 2)),
            _ => source.Length / 2,
        };

        var start = Math.Max(0, center - (width / 2));
        var end = Math.Min(source.Length, start + width);
        return (start, end);
    }

    /// <summary>
    /// Finds an edit offset near the selected position using generated method return statements as anchors.
    /// </summary>
    public static int GetEditOffset(string source, DocumentPosition position)
    {
        const string anchor = "return cache.TryGetValue";
        var searchStart = position switch
        {
            DocumentPosition.Top => 0,
            DocumentPosition.Middle => source.Length / 2,
            DocumentPosition.Bottom => Math.Max(0, source.Length - 80_000),
            _ => 0,
        };

        var offset = source.IndexOf(anchor, searchStart, StringComparison.Ordinal);
        if (offset >= 0)
        {
            return offset;
        }

        offset = source.LastIndexOf(anchor, Math.Min(searchStart, source.Length - 1), StringComparison.Ordinal);
        return offset >= 0 ? offset : source.Length;
    }

    /// <summary>
    /// Creates WPF text-change data for an insertion by applying it to a real text box on an STA thread.
    /// </summary>
    public static IReadOnlyList<TextChange> CreateInsertionChange(string source, int offset, string insertion, out string currentText)
    {
        IReadOnlyList<TextChange> changes = [];
        var updatedText = string.Empty;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new TextBox
                {
                    AcceptsReturn = true,
                    Text = source,
                };
                editor.TextChanged += (_, e) => changes = e.Changes.ToList();
                editor.Select(offset, 0);
                editor.SelectedText = insertion;
                updatedText = editor.Text;
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }

        currentText = updatedText;
        return changes;
    }
}

/// <summary>
/// Hosts a syntax-enabled text box on an STA thread so scrolling can be benchmarked outside the main process thread.
/// </summary>
internal sealed class WpfTextBoxScrollHarness : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim initialized = new();
    private Dispatcher dispatcher = null!;
    private Window window = null!;
    private TextBox editor = null!;
    private Exception? initializationException;

    /// <summary>
    /// Starts the WPF thread and initializes the off-screen benchmark window.
    /// </summary>
    public WpfTextBoxScrollHarness(string source)
    {
        thread = new Thread(() => RunWpfThread(source));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        initialized.Wait();

        if (initializationException is not null)
        {
            throw initializationException;
        }
    }

    /// <summary>
    /// Scrolls through representative document positions and returns a value that prevents benchmark elimination.
    /// </summary>
    public double ScrollThroughDocument()
    {
        return dispatcher.Invoke(() =>
        {
            var lastLine = Math.Max(0, editor.LineCount - 1);
            double offsetTotal = 0;
            for (var step = 0; step < 32; step++)
            {
                var line = (int)Math.Round(lastLine * (step / 31.0));
                editor.ScrollToLine(line);
                editor.CaretIndex = Math.Clamp(editor.GetCharacterIndexFromLineIndex(line), 0, editor.Text.Length);
                PumpLayout();
                offsetTotal += editor.VerticalOffset;
            }

            editor.ScrollToHome();
            PumpLayout();
            editor.ScrollToEnd();
            PumpLayout();
            return offsetTotal + editor.VerticalOffset;
        });
    }

    /// <summary>
    /// Closes the WPF window and shuts down the dispatcher thread.
    /// </summary>
    public void Dispose()
    {
        dispatcher.Invoke(() =>
        {
            window.Close();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        });
        thread.Join();
        initialized.Dispose();
    }

    /// <summary>
    /// Creates the editor, attaches syntax highlighting, shows the hidden window, and starts the dispatcher loop.
    /// </summary>
    private void RunWpfThread(string source)
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Height = 480,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4),
                Text = source,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Width = 900,
            };

            SyntaxBox.SetSyntaxDriver(editor, new SyntaxLanguage { Language = "CSharp" });
            SyntaxBox.SetEnable(editor, true);

            window = new Window
            {
                Content = editor,
                Height = 520,
                Left = -20_000,
                ShowActivated = false,
                ShowInTaskbar = false,
                Top = -20_000,
                Width = 940,
                WindowStyle = WindowStyle.None,
            };

            window.Show();
            PumpLayout();
        }
        catch (Exception ex)
        {
            initializationException = ex;
        }
        finally
        {
            initialized.Set();
        }

        Dispatcher.Run();
    }

    /// <summary>
    /// Flushes WPF layout and dispatcher work so scroll measurements include rendering effects.
    /// </summary>
    private void PumpLayout()
    {
        window.UpdateLayout();
        editor.UpdateLayout();
        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }
}
