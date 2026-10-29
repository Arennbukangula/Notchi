using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WindowsNotch.Conversion
{
    internal static class DocxSanitizer
    {
        private static readonly Regex DecimalWordAttribute = new(
            "(?<prefix>\\bw:[A-Za-z0-9_]+=\")-?(?<value>\\d+)\\.\\d+(?=\")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly XNamespace WordNamespace =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        public static bool TryCreateSanitizedCopy(
            string inputPath,
            string destinationPath,
            out bool changed)
        {
            changed = false;
            bool anyChanges = false;

            try
            {
                using var source = ZipFile.OpenRead(inputPath);
                using var destination = ZipFile.Open(
                    destinationPath,
                    ZipArchiveMode.Create);

                foreach (var entry in source.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var target = destination.CreateEntry(
                        entry.FullName,
                        CompressionLevel.Fastest);

                    try
                    {
                        target.LastWriteTime = entry.LastWriteTime;
                    }
                    catch
                    {
                    }

                    using var input = entry.Open();

                    if (entry.FullName.EndsWith(
                            ".xml",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        using var reader = new StreamReader(
                            input,
                            Encoding.UTF8,
                            detectEncodingFromByteOrderMarks: true);
                        string text = reader.ReadToEnd();
                        string sanitized = text
                            .Replace('\u2028', ' ')
                            .Replace('\u2029', ' ');

                        if (!string.Equals(sanitized, text,
                                StringComparison.Ordinal))
                        {
                            anyChanges = true;
                        }

                        sanitized = DecimalWordAttribute.Replace(
                            sanitized,
                            match =>
                            {
                                anyChanges = true;
                                return match.Groups["prefix"].Value +
                                    match.Groups["value"].Value;
                            });

                        sanitized = NormalizeBookmarkAnchors(
                            sanitized,
                            out bool anchorsChanged);

                        if (anchorsChanged)
                        {
                            anyChanges = true;
                        }

                        using var writer = new StreamWriter(
                            target.Open(),
                            new UTF8Encoding(false));
                        writer.Write(sanitized);
                    }
                    else
                    {
                        using var output = target.Open();
                        input.CopyTo(output);
                    }
                }

                changed = anyChanges;
                return true;
            }
            catch
            {
                TryDelete(destinationPath);
                return false;
            }
        }

        private static string NormalizeBookmarkAnchors(
            string xml,
            out bool changed)
        {
            changed = false;

            if (!xml.Contains("bookmarkStart", StringComparison.Ordinal) ||
                !xml.Contains("hyperlink", StringComparison.Ordinal))
            {
                return xml;
            }

            XDocument document;

            try
            {
                document = XDocument.Parse(
                    xml,
                    LoadOptions.PreserveWhitespace);
            }
            catch
            {
                return xml;
            }

            List<XElement> bookmarkStarts = document
                .Descendants(WordNamespace + "bookmarkStart")
                .Where(element =>
                    element.Attribute(WordNamespace + "name") != null)
                .ToList();

            if (bookmarkStarts.Count == 0)
            {
                return xml;
            }

            var retainedNames = new Dictionary<XElement, string>();
            var aliases = new Dictionary<string, string>(
                StringComparer.Ordinal);

            foreach (XElement bookmarkStart in bookmarkStarts)
            {
                XElement? paragraph = bookmarkStart
                    .Ancestors(WordNamespace + "p")
                    .FirstOrDefault();

                if (paragraph == null) continue;

                string name = bookmarkStart
                    .Attribute(WordNamespace + "name")!
                    .Value;

                if (string.IsNullOrEmpty(name)) continue;

                if (!retainedNames.TryGetValue(paragraph, out string? retained))
                {
                    retainedNames.Add(paragraph, name);
                }
                else if (!string.Equals(
                        retained,
                        name,
                        StringComparison.Ordinal))
                {
                    aliases[name] = retained;
                }
            }

            if (aliases.Count == 0)
            {
                return xml;
            }

            foreach (XElement hyperlink in document
                .Descendants(WordNamespace + "hyperlink"))
            {
                XAttribute? anchor = hyperlink.Attribute(
                    WordNamespace + "anchor");

                if (anchor == null) continue;

                if (aliases.TryGetValue(anchor.Value, out string? retained))
                {
                    anchor.Value = retained;
                    changed = true;
                }
            }

            if (!changed)
            {
                return xml;
            }

            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
