using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DonyshMobile.Core;

public sealed class MobileApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private readonly Func<HttpClient> _clientFactory;

    public MobileApiClient(Func<HttpClient>? clientFactory = null) =>
        _clientFactory = clientFactory ?? CreateClient;

    public static string? NormalizeServer(string? value)
    {
        if (!Uri.TryCreate(value?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task<PairResult?> PairAsync(string server, string code, CancellationToken cancellationToken = default)
    {
        server = NormalizeServer(server) ?? throw new ArgumentException("آدرس سرور باید یک نشانی HTTPS معتبر باشد.", nameof(server));
        var normalizedCode = new string(code.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalizedCode.Length != 32) throw new ArgumentException("کد اتصال باید ۳۲ نویسه هگزادسیمال باشد.", nameof(code));
        using var client = _clientFactory();
        using var response = await client.PostAsJsonAsync(server + "/api/mobile/pair", new PairRequest(normalizedCode), JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PairResult>(JsonOptions, cancellationToken);
        return result is { Token.Length: 64 } && result.Token.All(Uri.IsHexDigit)
            ? result
            : throw new InvalidDataException("پاسخ اتصال API معتبر نیست.");
    }

    public async Task<SyncRunResult> SendNextBatchAsync(
        SmsMonitorStore store,
        MobileConnection? connection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await SyncGate.WaitAsync(cancellationToken);
        try { return await SendNextBatchCoreAsync(store, connection, nowUtc, cancellationToken); }
        finally { SyncGate.Release(); }
    }

    private async Task<SyncRunResult> SendNextBatchCoreAsync(
        SmsMonitorStore store,
        MobileConnection? connection,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (connection is null) return SyncRunResult.ConnectionRequired;
        var server = NormalizeServer(connection.Server);
        if (server is null || connection.Token.Length != 64 || !connection.Token.All(Uri.IsHexDigit))
            return SyncRunResult.ConnectionRequired;
        var batch = await store.TakePendingBatchAsync(nowUtc, cancellationToken: cancellationToken);
        if (batch.Count == 0) return SyncRunResult.NothingToSend;

        try
        {
            using var client = _clientFactory();
            using var request = new HttpRequestMessage(HttpMethod.Post, server + "/api/mobile/sms")
            {
                Content = JsonContent.Create(new SmsBatch(batch
                    .Select(x => new SmsEnvelope(x.Sender, x.Body, x.ReceivedAtUnixMs)).ToList()), options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? response.Headers.RetryAfter?.Date - nowUtc;
                var message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "اتصال API معتبر نیست؛ کد اتصال جدید بسازید. پیام‌ها در صف محفوظ‌اند."
                    : response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "سرور فعلاً درخواست بیشتری نمی‌پذیرد؛ بعداً خودکار تلاش می‌شود."
                        : "ارسال کامل نشد؛ پیام‌ها محفوظ‌اند و بعداً دوباره تلاش می‌شود.";
                await store.RecordSyncFailureAsync(batch, nowUtc, message, retryAfter, cancellationToken);
                return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? SyncRunResult.ConnectionRequired
                    : SyncRunResult.RetryScheduled;
            }

            var result = await response.Content.ReadFromJsonAsync<SmsBatchResult>(JsonOptions, cancellationToken);
            var receipts = result?.Results?.Select(x => new SmsDeliveryReceipt(x.Key, x.Status)).ToList()
                           ?? throw new InvalidDataException("API تأییدیه‌ای برنگرداند.");
            await store.ConfirmBatchAsync(batch, receipts, nowUtc, cancellationToken);
            return SyncRunResult.Sent;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await store.RecordSyncFailureAsync(batch, nowUtc, "زمان اتصال تمام شد؛ پیام‌ها محفوظ‌اند.", cancellationToken: CancellationToken.None);
            return SyncRunResult.RetryScheduled;
        }
        catch (HttpRequestException)
        {
            await store.RecordSyncFailureAsync(batch, nowUtc, "اینترنت یا سرور در دسترس نیست؛ بعداً خودکار تلاش می‌شود.", cancellationToken: cancellationToken);
            return SyncRunResult.RetryScheduled;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            await store.RecordSyncFailureAsync(batch, nowUtc, "تأیید API معتبر نبود؛ برای جلوگیری از گم‌شدن پیام دوباره تلاش می‌شود.", cancellationToken: cancellationToken);
            return SyncRunResult.RetryScheduled;
        }
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 32768
    };
}
