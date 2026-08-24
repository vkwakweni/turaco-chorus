using System.Net;
using System.Text;

namespace TuracoChorus.Adapters.Claude.Tests;

/// <summary>Returns a fixed response for every request — no real HTTP call, no network access needed.</summary>
internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
