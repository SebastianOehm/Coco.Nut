using System.Text;

namespace CocoNut.Core.Nut;

/// <summary>
/// Pure, stateless helpers for the wire format of the NUT network protocol: tokenizing a response
/// line (respecting quoting and backslash escapes), mapping <c>ERR</c> codes to <see cref="NutErrorCode"/>,
/// quoting outgoing arguments, masking passwords for logs/exceptions, and picking apart the well-known
/// <c>VAR</c>/<c>DESC</c>/<c>UPS</c>/<c>CMD</c> line shapes used by <c>GET</c> and <c>LIST</c> replies.
/// </summary>
/// <remarks>
/// See the NUT network protocol reference: https://networkupstools.org/docs/developer-guide.chunked/net-protocol.html.
/// Internal (not part of the public <see cref="INutClient"/> contract) but visible to
/// <c>CocoNut.Core.Tests</c> via <c>InternalsVisibleTo</c> so it can be unit tested directly.
/// </remarks>
internal static class NutResponseParser
{
    /// <summary>
    /// Splits a NUT protocol line into tokens on whitespace, honouring double-quoted tokens that may
    /// contain escaped characters (<c>\"</c>, <c>\\</c>, ...). The surrounding quotes and the escaping
    /// backslashes are removed from the returned tokens - i.e. tokens come back already unescaped.
    /// </summary>
    /// <example><c>VAR ups1 battery.charge "100"</c> tokenizes to <c>["VAR", "ups1", "battery.charge", "100"]</c>.</example>
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool tokenStarted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\\' && i + 1 < line.Length)
            {
                current.Append(line[i + 1]);
                i++;
                tokenStarted = true;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (tokenStarted)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }

                continue;
            }

            current.Append(c);
            tokenStarted = true;
        }

        if (tokenStarted)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Maps an <c>ERR</c> code as sent on the wire (e.g. <c>DATA-STALE</c>) to a <see cref="NutErrorCode"/>
    /// by removing dashes and matching case-insensitively against the enum member names. Anything that
    /// does not match a known member (including an empty code) maps to <see cref="NutErrorCode.Unrecognized"/>.
    /// </summary>
    public static NutErrorCode MapErrorCode(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return NutErrorCode.Unrecognized;
        }

        string normalized = code.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<NutErrorCode>(normalized, ignoreCase: true, out NutErrorCode result)
            ? result
            : NutErrorCode.Unrecognized;
    }

    /// <summary>
    /// Builds the <see cref="NutException"/> for a raw <c>ERR ...</c> line, mapping its code with
    /// <see cref="MapErrorCode(string)"/> and recording the (password-masked) query that triggered it.
    /// </summary>
    public static NutException CreateError(string query, string rawErrorLine)
    {
        IReadOnlyList<string> tokens = Tokenize(rawErrorLine);
        string code = tokens.Count > 1 ? tokens[1] : string.Empty;
        return new NutException(MapErrorCode(code), MaskCommand(query), rawErrorLine);
    }

    /// <summary>
    /// Quotes <paramref name="value"/> for use as a single command argument if it contains whitespace,
    /// a double quote, a backslash, or is empty; otherwise returns it unchanged. Quoting escapes
    /// embedded <c>"</c> and <c>\</c> with a backslash, so the result always round-trips through
    /// <see cref="Tokenize(string)"/> back to the original value.
    /// </summary>
    public static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !NeedsQuoting(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char c in value)
        {
            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Masks a command's argument when it starts with <c>PASSWORD</c>, so credentials never end up in
    /// logs or in <see cref="NutException.Query"/>. Every other command is returned unchanged.
    /// </summary>
    public static string MaskCommand(string command)
    {
        return command.StartsWith("PASSWORD", StringComparison.Ordinal)
            && (command.Length == "PASSWORD".Length || command["PASSWORD".Length] == ' ')
            ? "PASSWORD ***"
            : command;
    }

    /// <summary>Parses a <c>VAR &lt;ups&gt; &lt;var&gt; "&lt;value&gt;"</c> line.</summary>
    public static bool TryParseVarLine(string line, out string name, out string value) =>
        TryParseThreeArgLine("VAR", line, out name, out value);

    /// <summary>Parses a <c>DESC &lt;ups&gt; &lt;var&gt; "&lt;description&gt;"</c> line.</summary>
    public static bool TryParseDescLine(string line, out string name, out string description) =>
        TryParseThreeArgLine("DESC", line, out name, out description);

    /// <summary>Parses a <c>UPS &lt;name&gt; "&lt;description&gt;"</c> line (from <c>LIST UPS</c>).</summary>
    public static bool TryParseUpsLine(string line, out string name, out string description)
    {
        IReadOnlyList<string> tokens = Tokenize(line);
        if (tokens.Count < 3 || !string.Equals(tokens[0], "UPS", StringComparison.Ordinal))
        {
            name = string.Empty;
            description = string.Empty;
            return false;
        }

        name = tokens[1];
        description = tokens[2];
        return true;
    }

    /// <summary>Parses a <c>CMD &lt;ups&gt; &lt;cmdname&gt;</c> line (from <c>LIST CMD</c>).</summary>
    public static bool TryParseCmdLine(string line, out string name)
    {
        IReadOnlyList<string> tokens = Tokenize(line);
        if (tokens.Count < 3 || !string.Equals(tokens[0], "CMD", StringComparison.Ordinal))
        {
            name = string.Empty;
            return false;
        }

        name = tokens[2];
        return true;
    }

    /// <summary>
    /// Parses the common <c>&lt;KEYWORD&gt; &lt;ups&gt; &lt;name&gt; "&lt;text&gt;"</c> shape shared by
    /// <c>VAR</c> and <c>DESC</c> lines: the ups name (token 1) is validated to be present but not
    /// returned, since callers already know which UPS they asked about.
    /// </summary>
    private static bool TryParseThreeArgLine(string keyword, string line, out string name, out string text)
    {
        IReadOnlyList<string> tokens = Tokenize(line);
        if (tokens.Count < 4 || !string.Equals(tokens[0], keyword, StringComparison.Ordinal))
        {
            name = string.Empty;
            text = string.Empty;
            return false;
        }

        name = tokens[2];
        text = tokens[3];
        return true;
    }

    private static bool NeedsQuoting(string value)
    {
        foreach (char c in value)
        {
            if (c is ' ' or '\t' or '"' or '\\')
            {
                return true;
            }
        }

        return false;
    }
}
