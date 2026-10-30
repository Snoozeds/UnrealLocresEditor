using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#nullable disable

namespace UnrealLocresEditor.Utils
{
    /*
     * Plain text translation file:
     *
     *   @@ Namespace/Key
     *   First line of the text
     *       Indented line
     *   @@ Other/Key
     *   ...
     *
     * Everything between two "@@ " lines is the text, kept as is
     * (spaces, tabs, empty lines). Keys are matched case-sensitively.
     * A line starting with "@@#" is a comment: it ends the current text
     * and is ignored (useful for section titles).
     */
    public static class TranslationText
    {
        public const string KeyMarker = "@@ ";
        public const string CommentMarker = "@@#";

        public class Entry
        {
            public string Key { get; set; }
            public string Text { get; set; }
            public int Line { get; set; }
        }

        public static List<Entry> Parse(string content, List<string> errors)
        {
            var entries = new List<Entry>();
            var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // A final newline at the end of the file is not part of the last text.
            int lineCount = lines.Length;
            if (lineCount > 0 && lines[lineCount - 1].Length == 0)
                lineCount--;

            Entry current = null;
            var textLines = new List<string>();

            for (int i = 0; i < lineCount; i++)
            {
                var line = lines[i];
                if (i == 0)
                    line = line.TrimStart('\uFEFF');

                if (line.StartsWith(CommentMarker, StringComparison.Ordinal))
                {
                    Close(current, textLines, entries);
                    current = null;
                    textLines.Clear();
                }
                else if (line.StartsWith(KeyMarker, StringComparison.Ordinal))
                {
                    Close(current, textLines, entries);
                    var key = line.Substring(KeyMarker.Length).TrimEnd();
                    if (key.Length == 0)
                        errors.Add($"Line {i + 1}: empty key.");
                    current = new Entry { Key = key, Line = i + 1 };
                    textLines.Clear();
                }
                else if (current == null)
                {
                    if (line.Trim().Length > 0)
                        errors.Add($"Line {i + 1}: text outside a \"{KeyMarker}key\" block.");
                }
                else
                {
                    textLines.Add(line);
                }
            }
            Close(current, textLines, entries);
            return entries;
        }

        // Finds the row of each entry. Full keys ("Namespace/Key") are matched first,
        // then keys without their namespace when only one namespace has them.
        public static List<(int Row, Entry Entry)> Match(List<Entry> entries, IList<string> rowKeys, List<string> errors)
        {
            var rowsByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowsByShortKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < rowKeys.Count; i++)
            {
                var key = rowKeys[i];
                rowsByKey[key] = i;

                int slash = key.IndexOf('/');
                if (slash >= 0)
                {
                    var shortKey = key.Substring(slash + 1);
                    if (!rowsByShortKey.TryGetValue(shortKey, out var list))
                        rowsByShortKey[shortKey] = list = new List<int>();
                    list.Add(i);
                }
            }

            var matches = new List<(int, Entry)>();
            foreach (var entry in entries.Where(e => e.Key.Length > 0))
            {
                if (rowsByKey.TryGetValue(entry.Key, out int row))
                {
                    matches.Add((row, entry));
                }
                else if (!rowsByShortKey.TryGetValue(entry.Key, out var candidates))
                {
                    errors.Add($"Line {entry.Line}: key \"{entry.Key}\" not found in the locres file.");
                }
                else if (candidates.Count == 1)
                {
                    matches.Add((candidates[0], entry));
                }
                else
                {
                    errors.Add(
                        $"Line {entry.Line}: key \"{entry.Key}\" exists in several namespaces, use the full key ({string.Join(", ", candidates.Select(c => rowKeys[c]))})."
                    );
                }
            }

            // Two entries for the same row: keep neither, the user has to choose.
            var conflicts = matches.GroupBy(m => m.Item1).Where(g => g.Count() > 1).ToList();
            foreach (var group in conflicts)
            {
                errors.Add(
                    $"Lines {string.Join(", ", group.Select(m => m.Item2.Line))} all target the key \"{rowKeys[group.Key]}\"."
                );
            }

            var conflictRows = conflicts.Select(g => g.Key).ToHashSet();
            return matches.Where(m => !conflictRows.Contains(m.Item1)).ToList();
        }

        private static void Close(Entry entry, List<string> textLines, List<Entry> entries)
        {
            if (entry == null)
                return;
            entry.Text = string.Join("\n", textLines);
            entries.Add(entry);
        }

        public static string Format(IEnumerable<KeyValuePair<string, string>> items, string newLine)
        {
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.Append(KeyMarker).Append(item.Key).Append(newLine);
                sb.Append(NormalizeNewLines(item.Value ?? string.Empty, newLine)).Append(newLine);
            }
            return sb.ToString();
        }

        public static string NormalizeNewLines(string text, string newLine)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newLine);
        }

        // The line break a text already uses, or null if it has none.
        public static string DetectNewLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            if (text.Contains("\r\n"))
                return "\r\n";
            if (text.Contains('\n'))
                return "\n";
            if (text.Contains('\r'))
                return "\r";
            return null;
        }

        // True when a line of the text would be read back as a key line.
        public static bool HasKeyMarkerLine(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Any(l =>
                    l.StartsWith(KeyMarker, StringComparison.Ordinal) || l.StartsWith(CommentMarker, StringComparison.Ordinal));
        }
    }
}
