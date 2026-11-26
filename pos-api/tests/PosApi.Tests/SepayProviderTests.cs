using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;
using Moq;

namespace PosApi.Tests;

public class SepayProviderTests
{
    private readonly DatabaseFixture _fixture = new();

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
        var json = @"{ ""status"": ""success"", ""data"": [] }";
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
                {"SePay:BaseUrl", "https://userapi.sepay.vn"}
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
    public async Task GetIncomingAsync_CallsV2TransactionsUrl()
    {
        // 1. Default BaseUrl
        {
            var json = @"{ ""status"": ""success"", ""data"": [] }";
            var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);
            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"}, {"SePay:BaseUrl", "https://userapi.sepay.vn"} })
                .Build();

            var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
            await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None);

            Assert.NotNull(handler.LastRequest);
            Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
            Assert.Equal("https://userapi.sepay.vn/v2/transactions", handler.LastRequest.RequestUri?.AbsoluteUri);
        }

        // 2. Custom BaseUrl with trailing slash
        {
            var json = @"{ ""status"": ""success"", ""data"": [] }";
            var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);
            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"}, {"SePay:BaseUrl", "https://example.test/"} })
                .Build();

            var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
            await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None);

            Assert.NotNull(handler.LastRequest);
            Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
            Assert.Equal("https://example.test/v2/transactions", handler.LastRequest.RequestUri?.AbsoluteUri);
        }
    }

    [Fact]
    public async Task GetIncomingAsync_NonSuccessStatus_ReturnsEmpty()
    {
        // Case 1: "status": "error"
        {
            var json = @"{ ""status"": ""error"", ""data"": [{ ""id"": ""1"", ""transfer_type"": ""in"", ""amount_in"": 1000, ""transaction_date"": ""2026-11-26 10:00:00"" }] }";
            var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);
            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
                .Build();

            var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
            var list = await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-10), null, CancellationToken.None);
            Assert.Empty(list);
        }

        // Case 2: Missing status field
        {
            var json = @"{ ""data"": [{ ""id"": ""2"", ""transfer_type"": ""in"", ""amount_in"": 1000, ""transaction_date"": ""2026-11-26 10:00:00"" }] }";
            var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);
            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
                .Build();

            var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
            var list = await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-10), null, CancellationToken.None);
            Assert.Empty(list);
        }

        // Case 3: "status": "SUCCESS" (uppercase) -> still received
        {
            var json = @"{ ""status"": ""SUCCESS"", ""data"": [{ ""id"": ""3"", ""transfer_type"": ""in"", ""amount_in"": 1000, ""transaction_date"": ""2026-11-26 10:00:00"" }] }";
            var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);
            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
                .Build();

            var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
            var list = await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-10), null, CancellationToken.None);
            Assert.Single(list);
        }
    }

    [Fact]
    public async Task GetIncomingAsync_OutTransferWithAmountIn_IsIgnored()
    {
        var json = @"{
            ""status"": ""success"",
            ""data"": [
                {
                    ""id"": ""out-1"",
                    ""transfer_type"": ""out"",
                    ""amount_in"": 50000,
                    ""transaction_date"": ""2026-11-26 10:00:00""
                },
                {
                    ""id"": ""missing-tt"",
                    ""amount_in"": 50000,
                    ""transaction_date"": ""2026-11-26 10:00:00""
                },
                {
                    ""id"": ""in-upper"",
                    ""transfer_type"": ""IN"",
                    ""amount_in"": 50000,
                    ""transaction_date"": ""2026-11-26 10:00:00""
                }
            ]
        }";

        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        var handler = new FakeHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
            .Build();

        var provider = new SepayProvider(factoryMock.Object, config, NullLogger<SepayProvider>.Instance);
        var list = await provider.GetIncomingAsync(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("in-upper", list[0].ExternalId);
    }

    [Fact]
    public async Task GetIncomingAsync_MissingOrBadDate_IsDropped_WithWarning()
    {
        var json = @"{
            ""status"": ""success"",
            ""data"": [
                {
                    ""id"": ""missing-date"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 1000
                },
                {
                    ""id"": ""bad-date"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 1000,
                    ""transaction_date"": ""khong-phai-ngay""
                },
                {
                    ""id"": ""good-date"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 1000,
                    ""transaction_date"": ""2026-11-26 10:00:00""
                }
            ]
        }";

        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        var handler = new FakeHttpMessageHandler(responseMsg);
        var client = new HttpClient(handler);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
            .Build();

        var logMessages = new List<string>();
        var loggerMock = new Mock<ILogger<SepayProvider>>();
        loggerMock.Setup(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => true),
            It.IsAny<Exception?>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, eventId, state, ex, formatter) =>
            {
                if (level == LogLevel.Warning)
                {
                    logMessages.Add(state.ToString() ?? "");
                }
            });

        var provider = new SepayProvider(loggerMock.Object == null ? null! : factoryMock.Object, config, loggerMock.Object);
        var list = await provider.GetIncomingAsync(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("good-date", list[0].ExternalId);

        Assert.Contains(logMessages, m => m.Contains("missing-date"));
        Assert.Contains(logMessages, m => m.Contains("bad-date"));
    }

    [Fact]
    public async Task ParseV2_Success_And_Filters_Correctly()
    {
        var json = @"{
            ""status"": ""success"",
            ""data"": [
                {
                    ""id"": ""uuid-1001"",
                    ""account_number"": ""00001234567"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 150000.0,
                    ""amount_out"": 0.0,
                    ""transaction_content"": ""INV-20261125-0001"",
                    ""reference_number"": ""FT12345"",
                    ""transaction_date"": ""2026-11-25 01:08:34""
                },
                {
                    ""id"": ""uuid-1002"",
                    ""account_number"": ""00001234567"",
                    ""transfer_type"": ""out"",
                    ""amount_in"": 0.0,
                    ""amount_out"": 50000.0,
                    ""transaction_content"": ""Tien ra"",
                    ""reference_number"": ""FT12346"",
                    ""transaction_date"": ""2026-11-25 01:09:00""
                },
                {
                    ""id"": ""uuid-1003"",
                    ""account_number"": ""00001234567"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 0,
                    ""amount_out"": 0.0,
                    ""transaction_content"": ""Amount zero"",
                    ""reference_number"": ""FT12347"",
                    ""transaction_date"": ""2026-11-25 01:10:00""
                },
                {
                    ""id"": ""uuid-1004"",
                    ""account_number"": ""00001234567"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": ""200000"",
                    ""amount_out"": 0.0,
                    ""transaction_content"": null,
                    ""reference_number"": ""FT12348"",
                    ""transaction_date"": ""2026-11-25 01:11:00""
                }
            ],
            ""meta"": {
                ""pagination"": {
                    ""total"": 4,
                    ""per_page"": 20,
                    ""current_page"": 1,
                    ""last_page"": 1,
                    ""has_more"": false
                }
            }
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
                {"SePay:ApiToken", "token"},
                {"SePay:BaseUrl", "https://userapi.sepay.vn"}
            })
            .Build();

        var logger = new Mock<ILogger<SepayProvider>>().Object;
        var provider = new SepayProvider(httpClientFactoryMock.Object, config, logger);

        var list = await provider.GetIncomingAsync(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

        Assert.Equal(2, list.Count);

        Assert.Equal("uuid-1001", list[0].ExternalId);
        Assert.Equal("00001234567", list[0].AccountNumber);
        Assert.Equal(150000m, list[0].Amount);
        Assert.Equal("INV-20261125-0001", list[0].Content);
        // "2026-11-25 01:08:34" in UTC+7 -> 2026-11-24 18:08:34 UTC
        Assert.Equal(new DateTime(2026, 11, 24, 18, 8, 34, DateTimeKind.Utc), list[0].TransactionDateUtc);

        Assert.Equal("uuid-1004", list[1].ExternalId);
        Assert.Equal(200000m, list[1].Amount);
        Assert.Null(list[1].Content);
    }

    [Theory]
    [InlineData("INV-20261126-0001", "INV-20261126-0001")]
    [InlineData("INV 20261126 0001", "INV-20261126-0001")]
    [InlineData("INV20261126-0001", "INV-20261126-0001")]
    [InlineData("INV202611260001", "INV-20261126-0001")]
    [InlineData("INV202611260001CT tu a", "INV-20261126-0001")]
    [InlineData("INV2026112600012345", null)]
    [InlineData("POS692232", null)]
    [InlineData("INV20261340001", null)]
    [InlineData("INV-20261340-0001", null)] // Month 13
    [InlineData("INV-20260230-0001", null)] // Feb 30
    [InlineData("123456789012", null)]
    [InlineData("INV-20261126-0001 and INV-20261126-0002", null)]
    [InlineData("INV-20261126-0001 and INV-20261126-0001", "INV-20261126-0001")]
    public void ExtractOrderCode_TestVariants(string input, string? expected)
    {
        var result = BankReconciliationService.ExtractOrderCode(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetIncomingAsync_HttpErrors_ReturnEmpty_WithoutTokensOrBodies()
    {
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            var responseMsg = new HttpResponseMessage(code)
            {
                Content = new StringContent("secret-token-body-xyz", Encoding.UTF8, "text/plain")
            };
            var handler = new FakeHttpMessageHandler(responseMsg);
            var client = new HttpClient(handler);

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(f => f.CreateClient("SePay")).Returns(client);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"SePay:ApiToken", "super-secret-token-123"},
                    {"SePay:BaseUrl", "https://userapi.sepay.vn"}
                })
                .Build();

            var logMessages = new List<string>();
            var loggerMock = new Mock<ILogger<SepayProvider>>();
            loggerMock.Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception?>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
                .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, eventId, state, ex, formatter) =>
                {
                    logMessages.Add(state.ToString() ?? "");
                });

            var provider = new SepayProvider(httpClientFactoryMock.Object, config, loggerMock.Object);
            var list = await provider.GetIncomingAsync(DateTime.UtcNow.AddDays(-1), null, CancellationToken.None);

            Assert.Empty(list);
            foreach (var msg in logMessages)
            {
                Assert.DoesNotContain("super-secret-token-123", msg);
                Assert.DoesNotContain("secret-token-body-xyz", msg);
            }
        }
    }

    [Fact]
    public async Task GetIncomingAsync_Cutoff_DbFixture()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var baseTime = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
            context.BankTransactions.Add(new BankTransaction
            {
                Provider = "SePay",
                ExternalId = "ext-latest",
                AccountNumber = "00001234567",
                Amount = 1000m,
                TransactionDate = baseTime,
                Status = BankTransactionStatus.Matched
            });
            await context.SaveChangesAsync();

            var json = @"{
                ""status"": ""success"",
                ""data"": [
                    {
                        ""id"": ""old-tx"",
                        ""account_number"": ""00001234567"",
                        ""transfer_type"": ""in"",
                        ""amount_in"": 100000,
                        ""amount_out"": 0,
                        ""transaction_content"": ""INV-20261126-0001"",
                        ""transaction_date"": ""2026-11-26 15:30:00""
                    },
                    {
                        ""id"": ""new-tx"",
                        ""account_number"": ""00001234567"",
                        ""transfer_type"": ""in"",
                        ""amount_in"": 200000,
                        ""amount_out"": 0,
                        ""transaction_content"": ""INV-20261126-0002"",
                        ""transaction_date"": ""2026-11-26 16:30:00""
                    }
                ],
                ""meta"": { ""pagination"": { ""has_more"": false } }
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
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"}, {"SePay:BaseUrl", "https://userapi.sepay.vn"} })
                .Build();

            var provider = new SepayProvider(httpClientFactoryMock.Object, config, context, NullLogger<SepayProvider>.Instance);
            var list = await provider.GetIncomingAsync(baseTime.AddDays(-10), null, CancellationToken.None);

            Assert.Single(list);
            Assert.Equal("new-tx", list[0].ExternalId);
        }
    }

    [Fact]
    public async Task GetIncomingAsync_HasMore_Warnings()
    {
        var json = @"{
            ""status"": ""success"",
            ""data"": [
                {
                    ""id"": ""tx-1"",
                    ""account_number"": ""00001234567"",
                    ""transfer_type"": ""in"",
                    ""amount_in"": 50000,
                    ""transaction_date"": ""2026-11-26 12:00:00""
                }
            ],
            ""meta"": {
                ""pagination"": {
                    ""has_more"": true
                }
            }
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
            .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"}, {"SePay:BaseUrl", "https://userapi.sepay.vn"} })
            .Build();

        var logMessages = new List<string>();
        var loggerMock = new Mock<ILogger<SepayProvider>>();
        loggerMock.Setup(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => true),
            It.IsAny<Exception?>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, eventId, state, ex, formatter) =>
            {
                if (level == LogLevel.Warning)
                {
                    logMessages.Add(state.ToString() ?? "");
                }
            });

        var provider = new SepayProvider(httpClientFactoryMock.Object, config, loggerMock.Object);
        await provider.GetIncomingAsync(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

        Assert.Contains(logMessages, m => m.Contains("có thể bỏ sót giao dịch"));
    }

    [Fact]
    public async Task ProcessAsync_Integration_SePayTransaction_MatchesOrder()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = new User { Username = "u_b5c", FullName = "U", Email = "u@test.com", PasswordHash = "h", Role = UserRole.Cashier };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-20261126-0001",
                CashierId = user.Id,
                ShiftId = shift.Id,
                Subtotal = 100000m,
                Total = 100000m,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = OrderStatus.Pending
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var json = @"{
                ""status"": ""success"",
                ""data"": [
                    {
                        ""id"": ""ext-int-1"",
                        ""account_number"": ""00001234567"",
                        ""transfer_type"": ""in"",
                        ""amount_in"": 100000,
                        ""amount_out"": 0,
                        ""transaction_content"": ""MBVCB.1.2.INV202611260001.CT tu a"",
                        ""transaction_date"": ""2026-11-26 10:00:00""
                    },
                    {
                        ""id"": ""ext-int-out"",
                        ""account_number"": ""00001234567"",
                        ""transfer_type"": ""out"",
                        ""amount_in"": 0,
                        ""amount_out"": 20000,
                        ""transaction_content"": ""Tien ra"",
                        ""transaction_date"": ""2026-11-26 10:05:00""
                    }
                ],
                ""meta"": { ""pagination"": { ""has_more"": false } }
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
                .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"}, {"SePay:BaseUrl", "https://userapi.sepay.vn"} })
                .Build();

            var provider = new SepayProvider(httpClientFactoryMock.Object, config, context, NullLogger<SepayProvider>.Instance);
            var incoming = await provider.GetIncomingAsync(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

            var reconciliation = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await reconciliation.ProcessAsync(incoming);

            var completedOrder = await context.Orders.Include(o => o.Payments).FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Completed, completedOrder.Status);
            Assert.Single(completedOrder.Payments);

            var matchedTx = await context.BankTransactions.FirstOrDefaultAsync(b => b.ExternalId == "ext-int-1");
            Assert.NotNull(matchedTx);
            Assert.Equal(BankTransactionStatus.Matched, matchedTx.Status);

            var outTx = await context.BankTransactions.FirstOrDefaultAsync(b => b.ExternalId == "ext-int-out");
            Assert.Null(outTx);
        }
    }

    [Fact]
    public void DI_ValidateScopes_ResolvesProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { {"SePay:ApiToken", "t"} })
            .Build());
        services.AddHttpClient("SePay");
        services.AddScoped<AppDbContext>(sp => _fixture.CreateRealContext());
        services.AddScoped<IBankTransactionProvider, SepayProvider>();

        var sp = services.BuildServiceProvider(validateScopes: true);
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IBankTransactionProvider>();
        Assert.NotNull(provider);
    }
}
