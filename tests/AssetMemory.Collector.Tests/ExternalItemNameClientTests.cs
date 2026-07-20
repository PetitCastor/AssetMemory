using System.Net;
using System.Net.Http;
using AssetMemory.Collector;

namespace AssetMemory.Collector.Tests;

public class ExternalItemNameClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated offline");
    }

    [Fact]
    public async Task TryResolveAsync_returns_the_name_on_a_200_match()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"class_name":"grin_multitool_resource_salvage_repair_01_filled","name":"Cambio-Lite SRT Canister"}}"""),
        });
        using var client = new ExternalItemNameClient(handler);

        var name = await client.TryResolveAsync("grin_multitool_resource_salvage_repair_01_filled");

        Assert.Equal("Cambio-Lite SRT Canister", name);
    }

    [Fact]
    public async Task TryResolveAsync_returns_null_on_a_404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"message":"No matching entity found."}"""),
        });
        using var client = new ExternalItemNameClient(handler);

        var name = await client.TryResolveAsync("not_a_real_item_class");

        Assert.Null(name);
    }

    [Fact]
    public async Task TryResolveAsync_returns_null_when_the_request_throws()
    {
        using var client = new ExternalItemNameClient(new ThrowingHandler());

        var name = await client.TryResolveAsync("anything");

        Assert.Null(name);
    }
}
