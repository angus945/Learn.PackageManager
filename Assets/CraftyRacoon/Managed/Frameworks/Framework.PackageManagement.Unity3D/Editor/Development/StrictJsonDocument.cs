using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal enum StrictJsonValueKind
    {
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null
    }

    internal sealed class StrictJsonValue
    {
        internal StrictJsonValue(StrictJsonValueKind kind)
        {
            Kind = kind;
        }

        internal StrictJsonValueKind Kind { get; }
        internal Dictionary<string, StrictJsonValue> Members { get; } = new Dictionary<string, StrictJsonValue>(StringComparer.Ordinal);
        internal List<StrictJsonValue> Items { get; } = new List<StrictJsonValue>();
        internal string Text { get; set; } = string.Empty;
    }

    internal sealed class StrictJsonDocument
    {
        private readonly string json;
        private int offset;

        private StrictJsonDocument(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            this.json = json;
        }

        internal static StrictJsonValue Parse(string json)
        {
            StrictJsonDocument reader = new StrictJsonDocument(json);
            StrictJsonValue value = reader.ReadValue();
            reader.SkipWhitespace();
            if (reader.offset != reader.json.Length) throw reader.Error("Unexpected content after the JSON value.");
            return value;
        }

        private StrictJsonValue ReadValue()
        {
            SkipWhitespace();
            if (offset >= json.Length) throw Error("Expected a JSON value.");
            char current = json[offset];
            if (current == '{') return ReadObject();
            if (current == '[') return ReadArray();
            if (current == '"') return ReadStringValue();
            bool numberToken = current == '-' || char.IsDigit(current);
            if (numberToken) return ReadNumber();
            if (current == 't') return ReadLiteral("true", StrictJsonValueKind.Boolean);
            if (current == 'f') return ReadLiteral("false", StrictJsonValueKind.Boolean);
            if (current == 'n') return ReadLiteral("null", StrictJsonValueKind.Null);
            throw Error("Unexpected JSON token.");
        }

        private StrictJsonValue ReadObject()
        {
            StrictJsonValue value = new StrictJsonValue(StrictJsonValueKind.Object);
            offset++;
            SkipWhitespace();
            if (Consume('}')) return value;
            while (true)
            {
                SkipWhitespace();
                if (offset >= json.Length || json[offset] != '"') throw Error("Object member names must be JSON strings.");
                string name = ReadString();
                bool duplicateMember = value.Members.ContainsKey(name);
                if (duplicateMember)
                {
                    string message = "Duplicate object member: " + name;
                    throw Error(message);
                }
                SkipWhitespace();
                Require(':');
                StrictJsonValue member = ReadValue();
                value.Members.Add(name, member);
                SkipWhitespace();
                if (Consume('}')) return value;
                Require(',');
            }
        }

        private StrictJsonValue ReadArray()
        {
            StrictJsonValue value = new StrictJsonValue(StrictJsonValueKind.Array);
            offset++;
            SkipWhitespace();
            if (Consume(']')) return value;
            while (true)
            {
                StrictJsonValue item = ReadValue();
                value.Items.Add(item);
                SkipWhitespace();
                if (Consume(']')) return value;
                Require(',');
            }
        }

        private StrictJsonValue ReadStringValue()
        {
            StrictJsonValue value = new StrictJsonValue(StrictJsonValueKind.String);
            value.Text = ReadString();
            return value;
        }

        private string ReadString()
        {
            Require('"');
            StringBuilder text = new StringBuilder();
            while (offset < json.Length)
            {
                char current = json[offset++];
                if (current == '"') return text.ToString();
                if (current < ' ') throw Error("Control characters are not permitted in JSON strings.");
                if (current != '\\')
                {
                    text.Append(current);
                    continue;
                }
                if (offset >= json.Length) throw Error("Incomplete JSON escape sequence.");
                char escaped = json[offset++];
                if (escaped == '"' || escaped == '\\' || escaped == '/') text.Append(escaped);
                else if (escaped == 'b') text.Append('\b');
                else if (escaped == 'f') text.Append('\f');
                else if (escaped == 'n') text.Append('\n');
                else if (escaped == 'r') text.Append('\r');
                else if (escaped == 't') text.Append('\t');
                else if (escaped == 'u')
                {
                    char unicodeCharacter = ReadUnicodeEscape();
                    text.Append(unicodeCharacter);
                }
                else throw Error("Invalid JSON escape sequence.");
            }
            throw Error("Unterminated JSON string.");
        }

        private char ReadUnicodeEscape()
        {
            if (offset + 4 > json.Length) throw Error("Incomplete Unicode escape sequence.");
            string digits = json.Substring(offset, 4);
            offset += 4;
            int codePoint;
            bool validCodePoint = int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out codePoint);
            if (!validCodePoint) throw Error("Invalid Unicode escape sequence.");
            return (char)codePoint;
        }

        private StrictJsonValue ReadNumber()
        {
            int start = offset;
            bool negative = Consume('-');
            if (negative && offset >= json.Length) throw Error("Incomplete JSON number.");
            if (Consume('0'))
            {
                bool hasNextCharacter = offset < json.Length;
                bool nextCharacterIsDigit = hasNextCharacter && char.IsDigit(json[offset]);
                if (nextCharacterIsDigit) throw Error("JSON numbers cannot contain a leading zero.");
            }
            else
            {
                ReadDigits();
            }
            bool decimalPoint = Consume('.');
            if (decimalPoint) ReadDigits();
            if (offset < json.Length && (json[offset] == 'e' || json[offset] == 'E'))
            {
                offset++;
                if (offset < json.Length && (json[offset] == '+' || json[offset] == '-')) offset++;
                ReadDigits();
            }
            StrictJsonValue value = new StrictJsonValue(StrictJsonValueKind.Number);
            value.Text = json.Substring(start, offset - start);
            return value;
        }

        private void ReadDigits()
        {
            int start = offset;
            while (offset < json.Length && char.IsDigit(json[offset])) offset++;
            if (start == offset) throw Error("Expected a JSON number digit.");
        }

        private StrictJsonValue ReadLiteral(string literal, StrictJsonValueKind kind)
        {
            bool literalFits = offset + literal.Length <= json.Length;
            string candidate = literalFits ? json.Substring(offset, literal.Length) : string.Empty;
            bool literalMatches = candidate.Equals(literal, StringComparison.Ordinal);
            if (!literalFits || !literalMatches) throw Error("Invalid JSON literal.");
            offset += literal.Length;
            StrictJsonValue value = new StrictJsonValue(kind);
            value.Text = literal;
            return value;
        }

        private void SkipWhitespace()
        {
            while (offset < json.Length)
            {
                char current = json[offset];
                if (current != ' ' && current != '\t' && current != '\r' && current != '\n') return;
                offset++;
            }
        }

        private bool Consume(char expected)
        {
            if (offset >= json.Length || json[offset] != expected) return false;
            offset++;
            return true;
        }

        private void Require(char expected)
        {
            bool consumed = Consume(expected);
            if (!consumed) throw Error("Expected '" + expected + "'.");
        }

        private FormatException Error(string message)
        {
            return new FormatException(message + " Offset " + offset + ".");
        }
    }
}
