using System.Text.RegularExpressions;
using TreeSitter.WpfSyntaxBox;

namespace TreeSitter.WpfSyntaxBox.Tests;

/// <summary>
/// Covers multi-keyword search behavior used by keyword syntax rules.
/// </summary>
public sealed class AhoCorasickSearchTests
{
    /// <summary>
    /// Verifies the matcher returns the longest keyword for common prefixes.
    /// </summary>
    [Fact]
    public void FindAll_PrefersLongestCommonPrefixMatch()
    {
        var matcher = new AhoCorasickSearch(["if", "ifa", "ifb"]);

        var actual = matcher.FindAll(" ifa ").ToList();

        Assert.Single(actual);
        Assert.Equal(1, actual[0].Position);
        Assert.Equal(3, actual[0].Length);
        Assert.Equal("ifa", actual[0].Value);
    }

    /// <summary>
    /// Verifies whole-word matching returns multiple dictionary entries in one pass.
    /// </summary>
    [Fact]
    public void FindAll_FindsMultipleWholeWordMatches()
    {
        var matcher = new AhoCorasickSearch(["if", "ifa", "ifb"]);

        var actual = matcher.FindAll("Matches 'ifa', 'ifb' and 'if' in text, Keywords: if, ifa, ifb").ToList();

        Assert.Equal(["ifa", "ifb", "if", "if", "ifa", "ifb"], actual.Select(match => match.Value).ToArray());
    }

    /// <summary>
    /// Verifies overlapping mode returns every matching dictionary output.
    /// </summary>
    [Fact]
    public void FindAll_CanReturnOverlappingMatches()
    {
        var matcher = new AhoCorasickSearch(["if", "ifa", "ifb"], matchWholeWords: false, overlappingMatches: true);

        var actual = matcher.FindAll("xifax").ToList();

        Assert.Equal(2, actual.Count);
        Assert.Equal("if", actual[0].Value);
        Assert.Equal("ifa", actual[1].Value);
    }

    /// <summary>
    /// Verifies keyword matching supports non-ASCII input.
    /// </summary>
    [Fact]
    public void FindAll_SupportsUnicodeInput()
    {
        var matcher = new AhoCorasickSearch(["città", "λ"]);

        var actual = matcher.FindAll("local città = λ").ToList();

        Assert.Equal(["città", "λ"], actual.Select(match => match.Value).ToArray());
    }

    /// <summary>
    /// Verifies keyword-rule offsets match equivalent regular-expression offsets.
    /// </summary>
    [Fact]
    public void KeywordRule_MatchesEquivalentRegexPositions()
    {
        const string keywords = "abstract,as,base,bool,break,byte,case,catch,char,checked,class,const,continue,decimal,default,delegate,do,double,else,enum,event,explicit,extern,false,finally,fixed,float,for,foreach,goto,if,implicit,in,int,interface,internal,is,lock,long,namespace,new,null,object,operator,out,override,params,private,protected,public,readonly,ref,return,sbyte,sealed,short,sizeof,stackalloc,static,string,struct,switch,this,throw,true,try,typeof,uint,ulong,unchecked,unsafe,ushort,using,virtual,void,volatile,while,get,set,yield,var";
        const string input = "public sealed class Demo { private string? value; public void Run() { if (value is null) return; } }";
        var keywordRule = new KeywordRule { Keywords = keywords };
        var regex = new Regex(@"\b(" + string.Join('|', keywords.Split(',')) + @")\b");

        var keywordMatches = keywordRule.Match(input).Select(match => (match.FromChar, match.Length)).ToArray();
        var regexMatches = regex.Matches(input).Select(match => (match.Index, match.Length)).ToArray();

        Assert.Equal(regexMatches, keywordMatches);
    }
}
