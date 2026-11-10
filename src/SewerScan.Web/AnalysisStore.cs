using SewerScan.Application.DTO;

namespace SewerScan.Web;

/// <summary>
/// Ephemeral, in-process results used for a single browser session.
/// Never stores uploaded PDF content and expires results automatically.
/// </summary>
internal sealed class AnalysisStore
{
    private sealed record Entry(ParsedProject Project, DateTimeOffset CreatedAt);

    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(30);
    private const int MaximumEntries = 10;

    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly object _sync = new();

    public Guid Save(ParsedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        lock (_sync)
        {
            EvictExpired();
            while (_entries.Count >= MaximumEntries)
            {
                var oldest = _entries.MinBy(item => item.Value.CreatedAt);
                _entries.Remove(oldest.Key);
            }

            var id = Guid.NewGuid();
            _entries.Add(id, new Entry(project, DateTimeOffset.UtcNow));
            return id;
        }
    }

    public ParsedProject? Get(Guid id)
    {
        lock (_sync)
        {
            EvictExpired();
            return _entries.TryGetValue(id, out var entry) ? entry.Project : null;
        }
    }

    private void EvictExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in _entries.Where(item => now - item.Value.CreatedAt > TimeToLive)
                     .Select(item => item.Key).ToArray())
        {
            _entries.Remove(id);
        }
    }
}
