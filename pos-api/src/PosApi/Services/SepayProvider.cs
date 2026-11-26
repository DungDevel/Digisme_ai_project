using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PosApi.Data;

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
    private readonly AppDbContext? _context;
    private readonly ILogger<SepayProvider> _log;

    public SepayProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, AppDbContext context, ILogger<SepayProvider> log)
    {
        _httpClient = httpClientFactory.CreateClient("SePay");
        _configuration = configuration;
        _context = context;
        _log = log;
    }

    public SepayProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<SepayProvider> log)
    {
        _httpClient = httpClientFactory.CreateClient("SePay");
        _configuration = configuration;
        _context = null;
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

        var baseUrl = sepayCfg["BaseUrl"] ?? "https://userapi.sepay.vn";
        var url = $"{baseUrl.TrimEnd('/')}/v2/transactions";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SePay API request failed.");
            return Array.Empty<IncomingBankTransaction>();
        }

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("SePay API returned error status {StatusCode}", (int)response.StatusCode);
            return Array.Empty<IncomingBankTransaction>();
        }

        string json;
        try
        {
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to read SePay response content.");
            return Array.Empty<IncomingBankTransaction>();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to parse SePay JSON response.");
            return Array.Empty<IncomingBankTransaction>();
        }

        using (doc)
        {
            var root = doc.RootElement;
            string? status = null;
            if (root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
            {
                status = statusProp.GetString();
            }

            if (string.IsNullOrEmpty(status) || !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogError("SePay API returned invalid or non-success status: {Status}", status);
                return Array.Empty<IncomingBankTransaction>();
            }

            // Calculate cut-off (Mốc cắt)
            DateTime cutoff = sinceUtc;
            if (_context != null)
            {
                try
                {
                    var latestTx = await _context.BankTransactions
                        .Where(b => b.Provider == "SePay")
                        .OrderByDescending(b => b.TransactionDate)
                        .FirstOrDefaultAsync(ct);

                    if (latestTx != null)
                    {
                        var txDate = latestTx.TransactionDate;
                        if (txDate.Kind != DateTimeKind.Utc)
                        {
                            txDate = DateTime.SpecifyKind(txDate, DateTimeKind.Utc);
                        }
                        cutoff = txDate.AddHours(-1);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Could not query latest BankTransaction for SePay cutoff. Using sinceUtc.");
                }
            }

            if (cutoff.Kind != DateTimeKind.Utc)
            {
                cutoff = DateTime.SpecifyKind(cutoff, DateTimeKind.Utc);
            }

            JsonElement txArray = default;
            if (root.TryGetProperty("data", out var dataEl))
            {
                txArray = dataEl;
            }

            bool hasMore = false;
            if (root.TryGetProperty("meta", out var metaEl) && metaEl.TryGetProperty("pagination", out var pagEl))
            {
                if (pagEl.TryGetProperty("has_more", out var hasMoreEl) && hasMoreEl.ValueKind == JsonValueKind.True)
                {
                    hasMore = true;
                }
            }

            // Giả định chưa kiểm chứng: thời gian trả về từ SePay transaction_date là giờ Việt Nam (UTC+7)
            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                }
                catch
                {
                    tz = TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
                }
            }

            var list = new List<IncomingBankTransaction>();
            DateTime? oldestOnPage = null;
            int totalParsedDates = 0;

            if (txArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in txArray.EnumerateArray())
                {
                    string externalId = string.Empty;
                    if (el.TryGetProperty("id", out var idProp))
                    {
                        externalId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64().ToString() : (idProp.GetString() ?? string.Empty);
                    }
                    if (string.IsNullOrEmpty(externalId) && el.TryGetProperty("reference_number", out var refProp))
                    {
                        externalId = refProp.GetString() ?? string.Empty;
                    }

                    if (!el.TryGetProperty("transaction_date", out var dateProp))
                    {
                        _log.LogWarning("Giao dịch SePay thiếu transaction_date. ExternalId={ExternalId}", externalId);
                        continue;
                    }

                    var dateStr = dateProp.GetString();
                    if (string.IsNullOrEmpty(dateStr) || !DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        _log.LogWarning("Giao dịch SePay không parse được transaction_date. ExternalId={ExternalId}", externalId);
                        continue;
                    }

                    DateTime txDateUtc;
                    try
                    {
                        var localDt = DateTime.SpecifyKind(parsedDate, DateTimeKind.Unspecified);
                        txDateUtc = TimeZoneInfo.ConvertTimeToUtc(localDt, tz);
                    }
                    catch
                    {
                        txDateUtc = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
                    }

                    if (txDateUtc.Kind != DateTimeKind.Utc)
                    {
                        txDateUtc = DateTime.SpecifyKind(txDateUtc, DateTimeKind.Utc);
                    }

                    totalParsedDates++;
                    if (oldestOnPage == null || txDateUtc < oldestOnPage.Value)
                    {
                        oldestOnPage = txDateUtc;
                    }

                    if (!el.TryGetProperty("transfer_type", out var ttProp) || ttProp.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    var transferType = ttProp.GetString();
                    if (string.IsNullOrEmpty(transferType) || !string.Equals(transferType, "in", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    decimal amountIn = 0m;
                    if (el.TryGetProperty("amount_in", out var amtInProp))
                    {
                        if (amtInProp.ValueKind == JsonValueKind.Number) amountIn = amtInProp.GetDecimal();
                        else if (amtInProp.ValueKind == JsonValueKind.String && decimal.TryParse(amtInProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) amountIn = d;
                    }

                    if (amountIn <= 0)
                    {
                        continue;
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

                    string? refCode = null;
                    if (el.TryGetProperty("reference_number", out var refNumProp))
                    {
                        refCode = refNumProp.GetString();
                    }

                    if (txDateUtc < cutoff)
                    {
                        continue;
                    }

                    list.Add(new IncomingBankTransaction(
                        ExternalId: externalId,
                        AccountNumber: accNo,
                        Amount: amountIn,
                        Content: content,
                        ReferenceCode: refCode,
                        TransactionDateUtc: txDateUtc,
                        AmountIn: amountIn,
                        AmountOut: 0m
                    ));
                }

                if (hasMore && oldestOnPage.HasValue && oldestOnPage.Value >= cutoff)
                {
                    _log.LogWarning("có thể bỏ sót giao dịch: has_more=true, tổng {Count} giao dịch, mốc cutoff={Cutoff}, oldestOnPage={Oldest}", totalParsedDates, cutoff, oldestOnPage.Value);
                }
            }

            return list;
        }
    }
}
