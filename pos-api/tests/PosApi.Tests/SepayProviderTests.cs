using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PosApi.Services;
using Xunit;
using Moq;

namespace PosApi.Tests;

public class SepayProviderTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }

        public FakeHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    [Fact]
    public async Task GetIncomingAsync_SendsBearerHeader()
    {
        var json = @"{ ""status"": 200, ""transactions"": [] }";
        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"SePay:ApiToken", "my-secret-token"},
                {"SePay:BaseUrl", "https://my.sepay.co"}
            })
            .Build();

        var logger = new Mock<ILogger<SepayProvider>>().Object;
        var provider = new SepayProvider(httpClientFactoryMock.Object, config, logger);

        await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("my-secret-token", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetIncomingAsync_SkipsOutgoingTransactions()
    {
        var json = @"{
            ""status"": 200,
            ""transactions"": [
                {
                    ""id"": 1001,
                    ""account_number"": ""0987654321"",
                    ""amount_in"": 150000.0,
                    ""amount_out"": 0.0,
                    ""transaction_content"": ""INV-20261124-0001"",
                    ""reference_number"": ""FT12345"",
                    ""transaction_date"": ""2026-11-24 10:00:00""
                },
                {
                    ""id"": 1002,
                    ""account_number"": ""0987654321"",
                    ""amount_in"": 0.0,
                    ""amount_out"": 50000.0,
                    ""transaction_content"": ""Tien ra"",
                    ""reference_number"": ""FT12346"",
                    ""transaction_date"": ""2026-11-24 10:05:00""
                }
            ]
        }";

        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"SePay:ApiToken", "my-secret-token"},
                {"SePay:BaseUrl", "https://my.sepay.co"}
            })
            .Build();

        var logger = new Mock<ILogger<SepayProvider>>().Object;
        var provider = new SepayProvider(httpClientFactoryMock.Object, config, logger);

        var list = await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("1001", list[0].ExternalId);
        Assert.Equal(150000m, list[0].Amount);
    }

    [Fact]
    public async Task GetIncomingAsync_401_ThrowsException()
    {
        var responseMsg = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Unauthorized", Encoding.UTF8, "text/plain")
        };
        var handler = new FakeHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"SePay:ApiToken", "invalid-token"},
                {"SePay:BaseUrl", "https://my.sepay.co"}
            })
            .Build();

        var logger = new Mock<ILogger<SepayProvider>>().Object;
        var provider = new SepayProvider(httpClientFactoryMock.Object, config, logger);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None));
    }
}
