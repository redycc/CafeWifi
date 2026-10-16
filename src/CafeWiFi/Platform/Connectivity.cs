namespace CafeWiFi.Platform;

/// <summary>The same probe Windows uses (NCSI) to decide whether a network reaches the internet.</summary>
public sealed class Connectivity : IConnectivity
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<bool> IsOnlineAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "http://www.msftconnecttest.com/connecttest.txt");
            req.Headers.CacheControl = new() { NoCache = true };
            using var res = await Http.SendAsync(req);
            return res.IsSuccessStatusCode && (await res.Content.ReadAsStringAsync()).Trim() == "Microsoft Connect Test";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
