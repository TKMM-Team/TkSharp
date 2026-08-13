namespace TkSharp.Core.Models;

public class TkModContext(Ulid id = default, object? input = null, Stream? stream = null)
{
    public Ulid Id { get; private set; } = id;

    public object? Input { get; set; } = input;

    public Stream? Stream { get; set; } = stream;

    public Func<IReadOnlyList<string>, CancellationToken, ValueTask<string?>>? SelectEmbeddedTkcl { get; set; }

    public bool IsEmbeddedTkcl { get; set; }

    /// <summary>
    /// Attempt to set the context <see cref="Id"/> to a new <see cref="Ulid"/> if not already set.
    /// </summary>
    public void EnsureId() => EnsureId(Ulid.NewUlid());

    /// <summary>
    /// Attempt to set the context <see cref="Id"/> if not already set.
    /// </summary>
    /// <param name="id"></param>
    public void EnsureId(Ulid id)
    {
        if (Id == default) Id = id;
    }

    public ValueTask<string?> ResolveEmbeddedTkcl(IReadOnlyList<string> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0) {
            return ValueTask.FromResult<string?>(null);
        }

        if (candidates.Count == 1 || SelectEmbeddedTkcl is null) {
            return ValueTask.FromResult<string?>(candidates[0]);
        }

        return SelectEmbeddedTkcl(candidates, ct);
    }
}