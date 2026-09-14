using System.Text;
using CSharpApp.UnitTests.TestDoubles;

namespace CSharpApp.IntegrationTests.TestDoubles;

public sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Authorization, string? Body);

/// <summary>Logins the fake keeps pending until released; <see cref="Started"/> completes when the first one arrives.</summary>
public sealed class LoginHold(Task started, Action release)
{
    public Task Started { get; } = started;

    public void Release() => release();
}

/// <summary>
/// The upstream API in memory. Answers the routes the service uses with the sandbox's real shapes, including its
/// quirks (400 for an unknown id, 400 for an unknown category on create), and records what it was asked, so tests
/// assert on the wire without a network. Failures are scripted per call and forgotten by <see cref="ResetScript"/>.
/// </summary>
public sealed class FakePlatziHandler : HttpMessageHandler
{
    public const string ApiPrefix = "/api/v1";
    public const int UnknownId = 999999;
    public const int UnknownCategoryId = 999;
    public const int CreatedProductId = 42;
    public const int CreatedCategoryId = 2;
    public const string RejectedCategoryMessage = "Could not find any entity of type Category matching: {\"where\":{\"id\":999}}";
    public const string UpstreamFailureBody = "upstream-internal-detail-that-must-not-leak";
    public const string RejectedLoginBody = "upstream-login-refusal-that-must-not-leak";

    private const string LoginPath = ApiPrefix + "/auth/login";
    private const string ProductsPath = ApiPrefix + "/products";
    private const string CategoriesPath = ApiPrefix + "/categories";

    private static readonly object FakeCategory = new { id = 1, name = "Fake", slug = "fake", image = "https://img.example/c.png" };
    private static readonly object FakeProduct = new
    {
        id = 1, title = "Fake product", slug = "fake-product", price = 10, description = "d",
        category = FakeCategory, images = new[] { "https://img.example/1.png" },
    };

    private readonly Lock _lock = new();
    private readonly List<RecordedRequest> _storeRequests = [];
    private readonly List<RecordedRequest> _logins = [];
    private int _loginCount;
    private int _pendingFailures;
    private HttpStatusCode _pendingFailureStatus;
    private string _pendingFailureBody = UpstreamFailureBody;
    private Exception? _pendingException;
    private TaskCompletionSource? _loginGate;
    private TaskCompletionSource? _loginStarted;

    /// <summary>Login attempts, successful or not, counted on arrival.</summary>
    public int LoginCount => Volatile.Read(ref _loginCount);

    public bool FailLogin { get; set; }

    /// <summary>Every request except logins, in order: the store calls and the health probe alike.</summary>
    public IReadOnlyList<RecordedRequest> StoreRequests
    {
        get { lock (_lock) { return [.. _storeRequests]; } }
    }

    public RecordedRequest LastStoreRequest => StoreRequests[^1];

    public RecordedRequest LastLogin
    {
        get { lock (_lock) { return _logins[^1]; } }
    }

    /// <summary>The next <paramref name="count"/> non-login calls answer <paramref name="status"/> with <paramref name="body"/>.</summary>
    public void FailNextStoreCalls(int count, HttpStatusCode status, string body = UpstreamFailureBody)
    {
        lock (_lock)
        {
            _pendingFailures = count;
            _pendingFailureStatus = status;
            _pendingFailureBody = body;
        }
    }

    /// <summary>The next non-login call fails the way a socket would: with an exception instead of a response.</summary>
    public void ThrowOnNextStoreCall(Exception exception)
    {
        lock (_lock)
        {
            _pendingException = exception;
        }
    }

    /// <summary>Keeps every login pending until the hold is released, so a test can look at the world while one is in flight.</summary>
    public LoginHold HoldLogins()
    {
        lock (_lock)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _loginGate = gate;
            _loginStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new LoginHold(_loginStarted.Task, () => gate.TrySetResult());
        }
    }

    /// <summary>Forgets every scripted failure and hold, so a test that failed half-way cannot leak into the next one.</summary>
    public void ResetScript()
    {
        lock (_lock)
        {
            _pendingFailures = 0;
            _pendingException = null;
            FailLogin = false;
            _loginGate?.TrySetResult();
            _loginGate = null;
            _loginStarted = null;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body);

        if (recorded.PathAndQuery == LoginPath)
        {
            return await LogInAsync(recorded);
        }

        lock (_lock)
        {
            _storeRequests.Add(recorded);
        }

        return TakeScriptedFailure() ?? Answer(request.Method.Method, request.RequestUri.AbsolutePath, body);
    }

    private async Task<HttpResponseMessage> LogInAsync(RecordedRequest login)
    {
        Task? gate;
        lock (_lock)
        {
            _logins.Add(login);
            gate = _loginGate?.Task;
            _loginStarted?.TrySetResult();
        }

        var number = Interlocked.Increment(ref _loginCount);
        if (gate is not null)
        {
            await gate;
        }

        if (FailLogin)
        {
            return Json(JsonSerializer.Serialize(new { message = RejectedLoginBody, statusCode = 401 }), HttpStatusCode.Unauthorized);
        }

        // A distinct token per login, so a test can tell a refreshed token from the stale one.
        var token = TestJwt.WithPayload(new { sub = number, exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        return Json(JsonSerializer.Serialize(new { access_token = token, refresh_token = $"refresh-{number}" }), HttpStatusCode.Created);
    }

    private HttpResponseMessage? TakeScriptedFailure()
    {
        lock (_lock)
        {
            if (_pendingException is { } exception)
            {
                _pendingException = null;
                throw exception;
            }

            if (_pendingFailures == 0)
            {
                return null;
            }

            _pendingFailures--;
            return Json(_pendingFailureBody, _pendingFailureStatus);
        }
    }

    private static HttpResponseMessage Answer(string method, string path, string? body) => (method, path) switch
    {
        ("GET", ProductsPath) => Json(JsonSerializer.Serialize(new[] { FakeProduct, FakeProduct })),
        ("GET", ProductsPath + "/1") => Json(JsonSerializer.Serialize(FakeProduct)),
        ("GET", _) when path.StartsWith(ProductsPath + "/") => EntityNotFound("Product"),
        ("POST", ProductsPath) => CreateProduct(body!),
        ("GET", CategoriesPath) => Json(JsonSerializer.Serialize(new[] { FakeCategory })),
        ("GET", CategoriesPath + "/1") => Json(JsonSerializer.Serialize(FakeCategory)),
        ("GET", _) when path.StartsWith(CategoriesPath + "/") => EntityNotFound("Category"),
        ("POST", CategoriesPath) => CreateCategory(body!),
        _ => Json($$"""{"message":"Cannot {{method}} {{path}}","error":"Not Found","statusCode":404}""", HttpStatusCode.NotFound),
    };

    private static HttpResponseMessage CreateProduct(string body)
    {
        using var request = JsonDocument.Parse(body);
        var root = request.RootElement;
        var categoryId = root.TryGetProperty("categoryId", out var category) ? category.GetInt32() : 0;
        if (categoryId == UnknownCategoryId)
        {
            return Json(JsonSerializer.Serialize(new { message = new[] { RejectedCategoryMessage }, error = "Bad Request", statusCode = 400 }), HttpStatusCode.BadRequest);
        }

        var created = new
        {
            id = CreatedProductId,
            title = root.GetProperty("title").GetString(),
            price = root.GetProperty("price").GetDecimal(),
            description = root.GetProperty("description").GetString(),
            category = new { id = categoryId, name = "Fake", slug = "fake", image = "https://img.example/c.png" },
            images = root.GetProperty("images").EnumerateArray().Select(image => image.GetString()).ToArray(),
        };
        return Json(JsonSerializer.Serialize(created), HttpStatusCode.Created);
    }

    private static HttpResponseMessage CreateCategory(string body)
    {
        using var request = JsonDocument.Parse(body);
        var created = new
        {
            id = CreatedCategoryId,
            name = request.RootElement.GetProperty("name").GetString(),
            slug = "books",
            image = request.RootElement.GetProperty("image").GetString(),
        };
        return Json(JsonSerializer.Serialize(created), HttpStatusCode.Created);
    }

    // The sandbox answers 400, not 404, when an id does not exist.
    private static HttpResponseMessage EntityNotFound(string entity)
        => Json(JsonSerializer.Serialize(new { name = "EntityNotFoundError", message = $"Could not find any entity of type {entity}", statusCode = 400 }), HttpStatusCode.BadRequest);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
