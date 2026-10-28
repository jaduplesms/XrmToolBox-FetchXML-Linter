using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using FetchXmlLinter.Core;

namespace FetchXmlLinter.XrmToolBox
{
    internal sealed class XmlEditor : RichTextBox
    {
        internal XmlEditor(string name)
        {
            Dock = DockStyle.Fill;
            WordWrap = false;
            AcceptsTab = true;
            DetectUrls = false;
            MaxLength = Linter.MaximumXmlCharacters;
            Font = new Font("Consolas", 10);
            BackColor = Color.White;
            ForeColor = Color.FromArgb(31, 41, 55);
            AccessibleName = name;
            AccessibleDescription = "Local XML editor. Loaded XML is indented and syntax-coloured. Use Format XML after pasting or editing.";
        }

        internal string LoadXml(string source)
        {
            try
            {
                SetFormattedXml(Format(source));
                ClearUndo();
                return "";
            }
            catch (XmlException ex)
            {
                Text = source;
                SelectAll();
                SelectionColor = ForeColor;
                Select(0, 0);
                ClearUndo();
                return AccessibleName + " could not be formatted; the original XML is retained. " + ex.Message;
            }
        }

        internal void SetFormattedXml(string formatted)
        {
            // Replace through the selection so an explicit format remains one undoable edit.
            SelectAll();
            SelectedRtf = Colourise(formatted);
            Select(0, 0);
            ScrollToCaret();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0302) // WM_PASTE: never import clipboard RTF or embedded objects.
                Paste(DataFormats.GetFormat(DataFormats.UnicodeText));
            else
                base.WndProc(ref message);
        }

        internal static string Format(string source)
        {
            if (source.Length > Linter.MaximumXmlCharacters)
                throw new XmlException("XML exceeds the 1 MiB character limit.");
            if (string.IsNullOrWhiteSpace(source)) return source;
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = Linter.MaximumXmlCharacters
            };
            using (var reader = XmlReader.Create(new StringReader(source), settings))
                while (reader.Read())
                    if (reader.Depth > 64)
                        throw new XmlException("XML is too deeply nested to format (maximum depth 64).");

            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(source), settings))
                document.Load(reader);
            foreach (var whitespace in document.ChildNodes.OfType<XmlWhitespace>().ToArray())
                document.RemoveChild(whitespace);
            var indentationCharacters = 0;
            Indent(document.DocumentElement!, 0, false, ref indentationCharacters);
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings
            {
                OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize, NewLineChars = "\n"
            }))
                document.Save(writer);
            var declaration = document.FirstChild as XmlDeclaration;
            var formatted = (declaration == null ? "" : declaration.OuterXml + "\n") + output;
            if (formatted.Length > Linter.MaximumXmlCharacters)
                throw new XmlException("Indented XML exceeds the 1 MiB character limit.");
            return formatted;
        }

        private static void Indent(XmlElement element, int depth, bool preserve, ref int indentationCharacters)
        {
            var space = element.GetAttribute("space", "http://www.w3.org/XML/1998/namespace");
            if (space.Length > 0) preserve = space == "preserve";
            var children = element.ChildNodes.OfType<XmlElement>().ToArray();
            if (!preserve && children.Length > 0 && !element.ChildNodes.Cast<XmlNode>().Any(
                node => node is XmlText || node is XmlCDataSection || node is XmlSignificantWhitespace))
            {
                foreach (var whitespace in element.ChildNodes.OfType<XmlWhitespace>().ToArray())
                    element.RemoveChild(whitespace);
                var indentation = "\n" + new string(' ', (depth + 1) * 2);
                var closing = "\n" + new string(' ', depth * 2);
                indentationCharacters += indentation.Length * element.ChildNodes.Count + closing.Length;
                if (indentationCharacters > Linter.MaximumXmlCharacters)
                    throw new XmlException("Indented XML exceeds the 1 MiB character limit.");
                foreach (var node in element.ChildNodes.Cast<XmlNode>().ToArray())
                    element.InsertBefore(element.OwnerDocument.CreateWhitespace(indentation), node);
                element.AppendChild(element.OwnerDocument.CreateWhitespace(closing));
            }
            foreach (var child in children)
                Indent(child, depth + 1, preserve, ref indentationCharacters);
        }

        private static string Colourise(string source)
        {
            var rtf = new StringBuilder(@"{\rtf1\ansi\deff0\uc1{\fonttbl{\f0 Consolas;}}" +
                @"{\colortbl;\red31\green41\blue55;\red100\green116\blue139;" +
                @"\red15\green108\blue189;\red121\green39\blue158;\red163\green21\blue21;" +
                @"\red0\green128\blue0;}\viewkind4\pard\f0\fs20 ");
            var position = 0;
            while (position < source.Length)
            {
                var start = position;
                if (source[position] != '<')
                {
                    while (position < source.Length && source[position] != '<') position++;
                    Append(rtf, source, start, position, 1);
                }
                else if (StartsAt(source, position, "<!--") || StartsAt(source, position, "<![CDATA["))
                {
                    var isCdata = source[position + 2] == '[';
                    var end = isCdata ? "]]>" : "-->";
                    position = source.IndexOf(end, position, StringComparison.Ordinal) + end.Length;
                    Append(rtf, source, start, position, isCdata ? 1 : 6);
                }
                else
                {
                    position++;
                    Append(rtf, source, start, position, 2);
                    var firstName = true;
                    while (position < source.Length)
                    {
                        start = position;
                        var character = source[position++];
                        var colour = 2;
                        if (character == '"' || character == '\'')
                        {
                            position = source.IndexOf(character, position) + 1;
                            colour = 5;
                        }
                        else if (!char.IsWhiteSpace(character) && "/?>=".IndexOf(character) < 0)
                        {
                            while (position < source.Length && !char.IsWhiteSpace(source[position]) &&
                                "/?>=".IndexOf(source[position]) < 0) position++;
                            colour = firstName ? 3 : 4;
                            firstName = false;
                        }
                        Append(rtf, source, start, position, colour);
                        if (character == '>') break;
                    }
                }
            }
            return rtf.Append('}').ToString();
        }

        private static bool StartsAt(string source, int position, string value) =>
            source.Length - position >= value.Length && string.CompareOrdinal(source, position, value, 0, value.Length) == 0;

        private static void Append(StringBuilder rtf, string source, int start, int end, int colour)
        {
            rtf.Append(@"\cf").Append(colour).Append(' ');
            for (var index = start; index < end; index++)
            {
                var character = source[index];
                if (character == '\\' || character == '{' || character == '}') rtf.Append('\\').Append(character);
                else if (character == '\n') rtf.Append(@"\line ");
                else if (character == '\r')
                {
                    if (index + 1 == end || source[index + 1] != '\n') rtf.Append(@"\line ");
                }
                else if (character == '\t') rtf.Append(@"\tab ");
                else if (character > 127) rtf.Append(@"\u").Append((short)character).Append('?');
                else rtf.Append(character);
            }
        }
    }
}
