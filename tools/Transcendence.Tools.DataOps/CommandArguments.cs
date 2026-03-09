using System.Globalization;

namespace Transcendence.Tools.DataOps;

internal sealed class CommandArguments
{
    private readonly Dictionary<string, string?> _options;

    private CommandArguments(string? command, Dictionary<string, string?> options)
    {
        Command = command;
        _options = options;
    }

    public string? Command { get; }

    public static CommandArguments Parse(string[] args)
    {
        if (args.Length == 0)
            return new CommandArguments(null, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

        var command = args[0];
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{token}'.");

            var key = token[2..];
            string? value = null;

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[index + 1];
                index++;
            }

            options[key] = value;
        }

        return new CommandArguments(command, options);
    }

    public bool HasFlag(string key) => _options.ContainsKey(key);

    public string? GetString(string key, string? fallback = null)
    {
        return _options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value!.Trim()
            : fallback;
    }

    public int GetInt(string key, int fallback)
    {
        var raw = GetString(key);
        return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public double GetDouble(string key, double fallback)
    {
        var raw = GetString(key);
        return raw != null &&
               double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : fallback;
    }

    public string[] GetCsv(string key, IReadOnlyList<string> fallback)
    {
        var raw = GetString(key);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
