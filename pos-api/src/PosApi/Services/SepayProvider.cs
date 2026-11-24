using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PosApi.Services;

public interface IBankTransactionProvider
{
    Task<IReadOnlyList<IncomingBankTransaction>> GetIncomingAsync(DateTime sinceUtc, string? accountNumber, CancellationToken ct);
}

public record IncomingBankTransaction(
    string ExternalId,
    string AccountNumber,
    decimal Amount,
    string? Content,
    string? ReferenceCode,
    DateTime TransactionDateUtc,
    decimal AmountIn = 0m,
    decimal AmountOut = 0m
);

public class SepayProvider : IBankTransactionProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SepayProvider> _log;

    public SepayProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<SepayProvider> log)
    {
        _httpClient = httpClientFactory.CreateClient("SePay");
        _configuration = configuration;
        _log = log;
    }

    public async Task<IReadOnlyList<IncomingBankTransaction>> GetIncomingAsync(DateTime sinceUtc, string? accountNumber, CancellationToken ct)
    {
        var sepayCfg = _configuration.GetSection("SePay");
        var token = sepayCfg["ApiToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogWarning("SePay ApiToken is not configured. Skipping polling.");
            return Array.Empty<IncomingBankTransaction>();
        }

        var baseUrl = sepayCfg["BaseUrl"] ?? "https://my.sepay.co";
        var url = $"{baseUrl.TrimEnd('/')}/userapi/transactions/list?since_date={sinceUtc:yyyy-MM-dd HH:mm:ss}";
        if (!string.IsNullOrWhiteSpace(accountNumber))
        {
            url += $"&account_number={Uri.EscapeDataString(accountNumber)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SePay API request failed: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"SePay API returned error status {(int)response.StatusCode}: {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var list = new List<IncomingBankTransaction>();
        JsonElement txArray = default;
        if (root.TryGetProperty("transactions", out var txs))
        {
            txArray = txs;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            txArray = root;
        }

        if (txArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in txArray.EnumerateArray())
            {
                decimal amountIn = 0m;
                if (el.TryGetProperty("amount_in", out var amtInProp))
                {
                    if (amtInProp.ValueKind == JsonValueKind.Number) amountIn = amtInProp.GetDecimal();
                    else if (amtInProp.ValueKind == JsonValueKind.String && decimal.TryParse(amtInProp.GetString(), out var d)) amountIn = d;
                }

                decimal amountOut = 0m;
                if (el.TryGetProperty("amount_out", out var amtOutProp))
                {
                    if (amtOutProp.ValueKind == JsonValueKind.Number) amountOut = amtOutProp.GetDecimal();
                    else if (amtOutProp.ValueKind == JsonValueKind.String && decimal.TryParse(amtOutProp.GetString(), out var d)) amountOut = d;
                }

                // bỏ giao dịch tiền ra (amount_out > 0 / amount_in = 0)
                if (amountOut > 0 || amountIn <= 0)
                    continue;

                string externalId = string.Empty;
                if (el.TryGetProperty("id", out var idProp))
                {
                    externalId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64().ToString() : (idProp.GetString() ?? string.Empty);
                }
                if (string.IsNullOrEmpty(externalId) && el.TryGetProperty("reference_number", out var refProp))
                {
                    externalId = refProp.GetString() ?? string.Empty;
                }

                string accNo = string.Empty;
                if (el.TryGetProperty("account_number", out var accProp))
                {
                    accNo = accProp.GetString() ?? string.Empty;
                }

                string? content = null;
                if (el.TryGetProperty("transaction_content", out var contentProp))
                {
                    content = contentProp.GetString();
                }
                else if (el.TryGetProperty("description", out var descProp))
                {
                    content = descProp.GetString();
                }

                string? refCode = null;
                if (el.TryGetProperty("reference_number", out var refNumProp))
                {
                    refCode = refNumProp.GetString();
                }
                else if (el.TryGetProperty("code", out var codeProp))
                {
                    refCode = codeProp.GetString();
                }

                DateTime txDate = DateTime.UtcNow;
                if (el.TryGetProperty("transaction_date", out var dateProp))
                {
                    var dateStr = dateProp.GetString();
                    if (DateTime.TryParse(dateStr, out var parsedDate))
                    {
                        txDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                    }
                }

                list.Add(new IncomingBankTransaction(
                    ExternalId: externalId,
                    AccountNumber: accNo,
                    Amount: amountIn,
                    Content: content,
                    ReferenceCode: refCode,
                    TransactionDateUtc: txDate,
                    AmountIn: amountIn,
                    AmountOut: amountOut
                ));
            }
        }

        return list;
    }
}
