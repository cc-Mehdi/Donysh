using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DonyshMobile.Core;

public sealed record LifeConnection(string Server, string Token, string? UserSubject = null,
    string? AccountLabel = null);
public sealed record LifeBlock(Guid PatternId, string OriginalDate, Guid ActivityId, string Title,
    string ColorHex, int StartMinute, int DurationMinutes, bool IsContinuation, string? Status);
public sealed record LifeDay(string Date, string Name, string PersianDate, List<LifeBlock> Blocks);
public sealed record LifeActivity(Guid Id, string Title);
public sealed record LifeWeek(string Today, List<LifeDay> Days, List<LifeActivity> Activities);
public sealed record LifeReminder(string Id, string Title, string StartLabel, string ScheduledDate, DateTimeOffset RemindAtUtc);

public static class LifeConnectionStore
{
    private const string Key = "donysh-life-connection-v1";
    public static async Task<LifeConnection?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(Key);
            var connection = json is null ? null : JsonSerializer.Deserialize<LifeConnection>(json);
            return connection is { Token.Length: 64 } && connection.Token.All(Uri.IsHexDigit)
                && MobileApiClient.NormalizeServer(connection.Server) == connection.Server ? connection : null;
        }
        catch { return null; }
    }
    public static Task SaveAsync(LifeConnection connection) =>
        SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(connection));
    public static void Remove() => SecureStorage.Default.Remove(Key);
}

public sealed class LifeMobileClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<LifeConnection?> SignInAsync(string ticket, string installationId,
        string deviceName, string? previousToken = null, CancellationToken ct = default)
    {
        var server = "https://life.donysh.ir";
        if (ticket.Length is < 20 or > 2048 || installationId.Length != 64 || !installationId.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 80)
            throw new ArgumentException("اطلاعات ورود معتبر نیست.");
        using var response = await _http.PostAsJsonAsync(server + "/api/life-mobile/session",
            new { ticket, installationId, deviceName, previousToken }, Json, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("مجوز ورود زندگی معتبر نیست؛ دوباره تلاش کنید.");
        if (response.StatusCode == HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionResponse>(Json, ct);
        if (result?.Token is not { Length: 64 } || !result.Token.All(Uri.IsHexDigit))
            throw new InvalidDataException("پاسخ ورود معتبر نیست.");
        return new LifeConnection(server, result.Token, result.UserSubject);
    }

    public Task<LifeWeek> WeekAsync(LifeConnection connection, CancellationToken ct = default) =>
        GetAsync<LifeWeek>(connection, "/api/life-mobile/week", ct);
    public Task<List<LifeReminder>> RemindersAsync(LifeConnection connection, CancellationToken ct = default) =>
        GetAsync<List<LifeReminder>>(connection, "/api/life-mobile/reminders", ct);
    public Task RecordAsync(LifeConnection connection, LifeBlock block, string status, int? actualMinutes = null, CancellationToken ct = default) =>
        PostAsync(connection, $"/api/life-mobile/occurrences/{block.PatternId:D}/{block.OriginalDate}",
            new { status, actualMinutes }, ct);
    public Task AddActualAsync(LifeConnection connection, Guid activityId, string date,
        string? startTime, int minutes, string? note, CancellationToken ct = default) =>
        PostAsync(connection, "/api/life-mobile/actual", new { activityId, date, startTime, actualMinutes = minutes, note }, ct);
    public async Task LogoutAsync(LifeConnection connection, CancellationToken ct = default)
    {
        using var request = Authorized(connection, HttpMethod.Post, "/api/life-mobile/logout");
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) response.EnsureSuccessStatusCode();
    }

    private async Task<T> GetAsync<T>(LifeConnection connection, string path, CancellationToken ct)
    {
        using var request = Authorized(connection, HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, ct);
        Check(response);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidDataException("پاسخ سامانه معتبر نیست.");
    }
    private async Task PostAsync(LifeConnection connection, string path, object body, CancellationToken ct)
    {
        using var request = Authorized(connection, HttpMethod.Post, path);
        request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, ct);
        Check(response);
    }
    private static HttpRequestMessage Authorized(LifeConnection connection, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, connection.Server + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        return request;
    }
    private static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedAccessException("اتصال لایف منقضی یا لغو شده است.");
        response.EnsureSuccessStatusCode();
    }
    private sealed record SessionResponse(string Token, string UserSubject);
}
