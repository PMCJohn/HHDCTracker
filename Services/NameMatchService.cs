using HHDCTracker.Data;
using HHDCTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace HHDCTracker.Services;

/// <summary>
/// Matches an MSDE child name string to a Child record in the database.
/// Tries exact match first, then alias match, then fuzzy match.
/// </summary>
public class NameMatchService
{
    private readonly AppDbContext _db;

    public NameMatchService(AppDbContext db) => _db = db;

    public record MatchResult(Child? Child, string MatchType, int Score);

    public async Task<MatchResult> FindChildAsync(string msdeFullName, int locationId)
    {
        if (string.IsNullOrWhiteSpace(msdeFullName))
            return new MatchResult(null, "None", 0);

        var name = msdeFullName.Trim();

        // 1. Exact full name match
        var children = await _db.Children
            .Include(c => c.Aliases)
            .Where(c => c.LocationId == locationId && c.IsActive)
            .ToListAsync();

        var exact = children.FirstOrDefault(c =>
            string.Equals(c.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return new MatchResult(exact, "Exact", 100);

        // 2. Alias match
        var aliasMatch = children.FirstOrDefault(c =>
            c.Aliases.Any(a => string.Equals(a.AliasName, name, StringComparison.OrdinalIgnoreCase)));
        if (aliasMatch != null) return new MatchResult(aliasMatch, "Alias", 95);

        // 3. Fuzzy match using Levenshtein distance
        Child? bestChild = null;
        int bestScore = 0;

        foreach (var child in children)
        {
            int score = FuzzyScore(child.FullName, name);
            if (score > bestScore) { bestScore = score; bestChild = child; }

            foreach (var alias in child.Aliases)
            {
                score = FuzzyScore(alias.AliasName, name);
                if (score > bestScore) { bestScore = score; bestChild = child; }
            }
        }

        // Only accept fuzzy matches above 80% similarity
        if (bestScore >= 80 && bestChild != null)
            return new MatchResult(bestChild, "Fuzzy", bestScore);

        return new MatchResult(null, "None", 0);
    }

    /// <summary>
    /// Returns a 0-100 similarity score between two strings.
    /// </summary>
    private static int FuzzyScore(string s1, string s2)
    {
        s1 = s1.ToLower().Trim();
        s2 = s2.ToLower().Trim();
        if (s1 == s2) return 100;

        int maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0) return 100;

        int distance = LevenshteinDistance(s1, s2);
        return (int)((1.0 - (double)distance / maxLen) * 100);
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1));
        return d[n, m];
    }
}
