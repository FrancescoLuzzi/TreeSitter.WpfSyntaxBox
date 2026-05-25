using System;
using System.Collections.Generic;
using System.Linq;

namespace TreeSitter.WpfSyntaxBox;

/// <summary>
/// Searches for many keywords in one pass using an Aho-Corasick automaton.
/// </summary>
public sealed class AhoCorasickSearch
{
    private readonly List<Node> nodes = [new Node()];
    private readonly List<string> dictionary;
    private readonly bool matchWholeWords;
    private readonly bool overlappingMatches;

    /// <summary>
    /// Builds a search automaton for a keyword dictionary.
    /// </summary>
    public AhoCorasickSearch(List<string> dictionary, bool matchWholeWords = true, bool overlappingMatches = false)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        this.dictionary = dictionary
            .Where(word => !string.IsNullOrEmpty(word))
            .OrderByDescending(word => word.Length)
            .ToList();
        this.matchWholeWords = matchWholeWords;
        this.overlappingMatches = overlappingMatches;

        BuildTrie();
        BuildFailures();
        SortOutputs();
    }

    /// <summary>
    /// Streams matches from the input text without allocating per character.
    /// </summary>
    public IEnumerable<Substring> FindAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Substring? previousMatch = null;
        var state = 0;

        for (var i = 0; i < text.Length; i++)
        {
            state = NextState(state, text[i]);
            if (nodes[state].Outputs.Count == 0)
            {
                continue;
            }

            foreach (var dictionaryIndex in nodes[state].Outputs)
            {
                var value = dictionary[dictionaryIndex];
                var firstChar = i - value.Length + 1;
                if (matchWholeWords && (!text.IsStartWordBoundary(firstChar) || !text.IsEndWordBoundary(i)))
                {
                    continue;
                }

                var substring = new Substring
                {
                    Position = firstChar,
                    Length = value.Length,
                    Value = value,
                };

                if (overlappingMatches)
                {
                    yield return substring;
                    continue;
                }

                if (previousMatch is not null && previousMatch.Value.Position != substring.Position)
                {
                    yield return previousMatch.Value;
                }

                previousMatch = substring;
                break;
            }
        }

        if (previousMatch is not null)
        {
            yield return previousMatch.Value;
        }
    }

    /// <summary>
    /// Builds trie transitions for every dictionary word.
    /// </summary>
    private void BuildTrie()
    {
        for (var dictionaryIndex = 0; dictionaryIndex < dictionary.Count; dictionaryIndex++)
        {
            var state = 0;
            foreach (var value in dictionary[dictionaryIndex])
            {
                if (!nodes[state].Next.TryGetValue(value, out var next))
                {
                    next = nodes.Count;
                    nodes[state].Next[value] = next;
                    nodes.Add(new Node());
                }

                state = next;
            }

            nodes[state].Outputs.Add(dictionaryIndex);
        }
    }

    /// <summary>
    /// Builds failure links so search can continue in linear time after partial mismatches.
    /// </summary>
    private void BuildFailures()
    {
        var queue = new Queue<int>();

        foreach (var child in nodes[0].Next.Values)
        {
            nodes[child].Failure = 0;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (var (value, target) in nodes[state].Next)
            {
                var failure = nodes[state].Failure;
                while (failure != 0 && !nodes[failure].Next.ContainsKey(value))
                {
                    failure = nodes[failure].Failure;
                }

                if (nodes[failure].Next.TryGetValue(value, out var fallback))
                {
                    nodes[target].Failure = fallback;
                    nodes[target].Outputs.AddRange(nodes[fallback].Outputs);
                }
                else
                {
                    nodes[target].Failure = 0;
                }

                queue.Enqueue(target);
            }
        }
    }

    /// <summary>
    /// Advances the automaton state for one character, following failure links as needed.
    /// </summary>
    private int NextState(int state, char value)
    {
        while (state != 0 && !nodes[state].Next.TryGetValue(value, out _))
        {
            state = nodes[state].Failure;
        }

        return nodes[state].Next.TryGetValue(value, out var next) ? next : 0;
    }

    /// <summary>
    /// Sorts output dictionary indices so longer pre-sorted keywords win consistently.
    /// </summary>
    private void SortOutputs()
    {
        foreach (var node in nodes)
        {
            if (node.Outputs.Count > 1)
            {
                node.Outputs.Sort();
            }
        }
    }

    /// <summary>
    /// Stores trie transitions, fallback state, and matched dictionary indices for one automaton state.
    /// </summary>
    private sealed class Node
    {
        public Dictionary<char, int> Next { get; } = new();

        public int Failure { get; set; }

        public List<int> Outputs { get; } = [];
    }
}
