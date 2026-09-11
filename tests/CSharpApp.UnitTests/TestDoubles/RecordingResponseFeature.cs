using Microsoft.AspNetCore.Http.Features;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>
/// The default response feature ignores OnStarting callbacks; a real server runs them when the first byte is
/// written. This one records them so a test can start the response and observe what the callbacks did.
/// Kestrel runs its callbacks last-registered-first and stops at the first that throws; with one callback the
/// difference does not matter here.
/// </summary>
public sealed class RecordingResponseFeature : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

    public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

    public async Task StartAsync()
    {
        foreach (var (callback, state) in _onStarting)
        {
            await callback(state);
        }
    }
}
