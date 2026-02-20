using System;

public static class ParameterizedPathParser
{
    public struct ParsedPath
    {
        public string CanonicalBaseName;
        public string MatchedBaseName;
        public string ParameterId;
        public string Suffix;
        public bool UsedDottedNotation;

        public bool IsCanonical
        {
            get
            {
                return !UsedDottedNotation
                    && string.Equals(
                        CanonicalBaseName,
                        MatchedBaseName,
                        StringComparison.OrdinalIgnoreCase
                    );
            }
        }

        public string CanonicalPath => BuildCanonicalPath(CanonicalBaseName, ParameterId, Suffix);
    }

    public static bool TryParseModifierParameterizedPath(string raw, out ParsedPath parsed)
    {
        if (TryParse(raw, "nodeOutput", out parsed, "node.outputMultiplier"))
            return true;
        if (TryParse(raw, "resourceGain", out parsed))
            return true;
        if (TryParse(raw, "nodeCapacity", out parsed))
            return true;
        if (TryParse(raw, "lifetimeEarnings", out parsed))
            return true;
        if (TryParse(raw, "resource", out parsed))
            return true;

        parsed = default;
        return false;
    }

    public static bool TryParseFormulaParameterizedPath(string raw, out ParsedPath parsed)
    {
        if (TryParse(raw, "lifetimeEarnings", out parsed))
            return true;
        if (TryParse(raw, "resource", out parsed))
            return true;

        parsed = default;
        return false;
    }

    public static bool TryCanonicalizeModifierParameterizedPath(
        string raw,
        out string canonicalPath,
        out bool usedLegacyFormat
    )
    {
        if (TryParseModifierParameterizedPath(raw, out var parsed))
        {
            canonicalPath = parsed.CanonicalPath;
            usedLegacyFormat = !parsed.IsCanonical;
            return true;
        }

        canonicalPath = (raw ?? string.Empty).Trim();
        usedLegacyFormat = false;
        return false;
    }

    public static bool TryCanonicalizeFormulaParameterizedPath(
        string raw,
        out string canonicalPath,
        out bool usedLegacyFormat
    )
    {
        if (TryParseFormulaParameterizedPath(raw, out var parsed))
        {
            canonicalPath = parsed.CanonicalPath;
            usedLegacyFormat = !parsed.IsCanonical;
            return true;
        }

        canonicalPath = (raw ?? string.Empty).Trim();
        usedLegacyFormat = false;
        return false;
    }

    public static bool TryParse(
        string raw,
        string canonicalBaseName,
        out ParsedPath parsed,
        params string[] aliases
    )
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(canonicalBaseName))
        {
            parsed = default;
            return false;
        }

        var value = raw.Trim();
        if (
            TryParseAgainstBase(
                value,
                canonicalBaseName,
                out var parameterId,
                out var suffix,
                out var usedDotted
            )
        )
        {
            parsed = new ParsedPath
            {
                CanonicalBaseName = canonicalBaseName.Trim(),
                MatchedBaseName = canonicalBaseName.Trim(),
                ParameterId = parameterId,
                Suffix = suffix,
                UsedDottedNotation = usedDotted,
            };
            return true;
        }

        if (aliases != null)
        {
            for (int i = 0; i < aliases.Length; i++)
            {
                var alias = (aliases[i] ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(alias))
                    continue;

                if (!TryParseAgainstBase(value, alias, out parameterId, out suffix, out usedDotted))
                    continue;

                parsed = new ParsedPath
                {
                    CanonicalBaseName = canonicalBaseName.Trim(),
                    MatchedBaseName = alias,
                    ParameterId = parameterId,
                    Suffix = suffix,
                    UsedDottedNotation = usedDotted,
                };
                return true;
            }
        }

        parsed = default;
        return false;
    }

    public static string BuildCanonicalPath(
        string canonicalBaseName,
        string parameterId,
        string suffix = null
    )
    {
        return
            $"{(canonicalBaseName ?? string.Empty).Trim()}[{(parameterId ?? string.Empty).Trim()}]{(suffix ?? string.Empty).Trim()}";
    }

    private static bool TryParseAgainstBase(
        string value,
        string baseName,
        out string parameterId,
        out string suffix,
        out bool usedDottedNotation
    )
    {
        parameterId = string.Empty;
        suffix = string.Empty;
        usedDottedNotation = false;

        var key = (baseName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(key))
            return false;

        // Canonical form: base[param]
        var bracketPrefix = key + "[";
        if (value.StartsWith(bracketPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var close = value.IndexOf(']', bracketPrefix.Length);
            if (close < bracketPrefix.Length)
                return false;

            var inner = value.Substring(bracketPrefix.Length, close - bracketPrefix.Length);
            var id = inner.Trim();
            if (string.IsNullOrEmpty(id))
                return false;

            parameterId = id;
            suffix = close < value.Length - 1 ? value.Substring(close + 1) : string.Empty;
            return true;
        }

        // Backward-compatible legacy form: base.param
        var dottedPrefix = key + ".";
        if (value.StartsWith(dottedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = value.Substring(dottedPrefix.Length);
            var split = remainder.IndexOf('.');
            var id = split >= 0 ? remainder.Substring(0, split).Trim() : remainder.Trim();
            if (string.IsNullOrEmpty(id))
                return false;

            parameterId = id;
            suffix = split >= 0 ? remainder.Substring(split) : string.Empty;
            usedDottedNotation = true;
            return true;
        }

        return false;
    }
}
