using CSharpApp.Core.Interfaces;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Hands out the given tokens in order and records how often it was asked and invalidated.</summary>
public sealed class FakeTokenProvider(params string[] tokens) : ITokenProvider
{
    private readonly Lock _stateLock = new();
    private int _index;

    public int InvalidateCount { get; private set; }
    public int TokenRequests { get; private set; }
    public List<string> InvalidatedTokens { get; } = [];

    public ValueTask<string> GetAccessTokenAsync(CancellationToken ct)
    {
        lock (_stateLock)
        {
            TokenRequests++;
            return ValueTask.FromResult(tokens[Math.Min(_index, tokens.Length - 1)]);
        }
    }

    public void Invalidate(string staleToken)
    {
        lock (_stateLock)
        {
            InvalidateCount++;
            InvalidatedTokens.Add(staleToken);
            _index++;
        }
    }
}
