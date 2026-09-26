using System.Net;

namespace CocoNut.Core.Tests.Updates;

/// <summary>An <see cref="HttpMessageHandler"/> that returns a canned response (or throws) without any network I/O.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHttpMessageHandler WithJson(HttpStatusCode statusCode, string json) =>
        new(_ => new HttpResponseMessage(statusCode) { Content = new StringContent(json) });

    public static FakeHttpMessageHandler Throwing(Exception exception) => new(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_respond(request));
    }
}
