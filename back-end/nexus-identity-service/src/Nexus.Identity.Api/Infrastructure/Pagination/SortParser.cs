namespace Nexus.Identity.Api.Infrastructure.Pagination;

/// <summary>
/// Parses simple <c>sort</c> query strings of the form <c>field</c> or <c>-field</c>
/// (leading <c>-</c> indicates descending) into structured directives.
/// </summary>
public static class SortParser
{
    /// <summary>
    /// Parse a comma-separated sort expression.
    /// </summary>
    /// <param name="expression">Raw sort expression, e.g. <c>username,-createdAt</c>.</param>
    /// <param name="allowedFields">Whitelist of permitted field names.</param>
    /// <returns>List of <see cref="SortDirective"/>; empty if <paramref name="expression"/> is null/empty.</returns>
    /// <exception cref="ArgumentException">Thrown if a field outside the whitelist is referenced.</exception>
    public static IReadOnlyList<SortDirective> Parse(string? expression, ISet<string> allowedFields)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Array.Empty<SortDirective>();
        }

        var directives = new List<SortDirective>();
        foreach (var raw in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var descending = raw.StartsWith('-');
            var field = descending ? raw[1..] : raw;

            if (!allowedFields.Contains(field))
            {
                throw new ArgumentException($"Sort field '{field}' is not allowed.", nameof(expression));
            }

            directives.Add(new SortDirective(field, descending));
        }

        return directives;
    }
}

/// <summary>Single sort directive.</summary>
/// <param name="Field">Field name.</param>
/// <param name="Descending"><c>true</c> for descending order.</param>
public sealed record SortDirective(string Field, bool Descending);
