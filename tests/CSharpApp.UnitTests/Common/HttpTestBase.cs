using System.Net;
using System.Text;
using CSharpApp.UnitTests.TestDoubles;

namespace CSharpApp.UnitTests.Common;

/// <summary>Shared pieces for tests that send HTTP through a scripted handler.</summary>
public abstract class HttpTestBase
{
    protected const string BaseUrl = "https://api.escuelajs.co/api/v1/";

    protected static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    protected static (HttpClient Client, StubHttpMessageHandler Stub) StubbedHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHttpMessageHandler(responder);
        return (new HttpClient(stub) { BaseAddress = new Uri(BaseUrl) }, stub);
    }
}
