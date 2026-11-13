using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bergafin.Auth.Configuration;
using Microsoft.Extensions.Logging;

namespace Bergafin.Auth.Oidc;

/// <summary>Quem o Authentik disse que é a pessoa.</summary>
public sealed record AuthentikIdentity(string Subject, string Username, string? DisplayName, IReadOnlyList<string> Groups);

/// <summary>
/// Cliente OIDC mínimo para o Authentik: authorization code + PKCE, troca do code no
/// back-channel (com client secret) e leitura do userinfo. Como o token vem direto do
/// Authentik por HTTPS, não é preciso validar a assinatura do id_token.
/// </summary>
public sealed class AuthentikClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthentikClient> _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private Discovery? _discovery;
    private string? _discoveryIssuer;

    public AuthentikClient(IHttpClientFactory httpClientFactory, ILogger<AuthentikClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    public static string RedirectUri => Config.PublicUrl.TrimEnd('/') + "/Bergafin/Callback";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Config.PublicUrl)
        && !string.IsNullOrWhiteSpace(Config.Issuer)
        && !string.IsNullOrWhiteSpace(Config.ClientId)
        && !string.IsNullOrWhiteSpace(Config.ClientSecret);

    /// <summary>Monta a URL do Authentik para iniciar o login.</summary>
    public async Task<string> BuildAuthorizeUrl(string state, string verifier, CancellationToken ct)
    {
        var discovery = await GetDiscovery(ct).ConfigureAwait(false);
        var challenge = LoginStore.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = Config.ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid profile email",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var qs = string.Join('&', query.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
        return discovery.AuthorizationEndpoint + (discovery.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + qs;
    }

    /// <summary>Troca o code pelo token e lê quem é a pessoa.</summary>
    public async Task<AuthentikIdentity> Redeem(string code, string verifier, CancellationToken ct)
    {
        var discovery = await GetDiscovery(ct).ConfigureAwait(false);
        var http = _httpClientFactory.CreateClient();

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = Config.ClientId,
                ["client_secret"] = Config.ClientSecret,
                ["code_verifier"] = verifier,
            }),
        };
        using var tokenResponse = await http.SendAsync(tokenRequest, ct).ConfigureAwait(false);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Authentik recusou a troca do code: {Status} {Body}", (int)tokenResponse.StatusCode, tokenBody);
            throw new InvalidOperationException("O Authentik recusou o login.");
        }

        using var tokenJson = JsonDocument.Parse(tokenBody);
        var accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Resposta do Authentik sem access_token.");

        using var userinfoRequest = new HttpRequestMessage(HttpMethod.Get, discovery.UserinfoEndpoint);
        userinfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var userinfoResponse = await http.SendAsync(userinfoRequest, ct).ConfigureAwait(false);
        var userinfoBody = await userinfoResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!userinfoResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Falha no userinfo do Authentik: {Status} {Body}", (int)userinfoResponse.StatusCode, userinfoBody);
            throw new InvalidOperationException("Não foi possível ler o usuário no Authentik.");
        }

        using var info = JsonDocument.Parse(userinfoBody);
        var root = info.RootElement;
        var subject = GetString(root, "sub") ?? throw new InvalidOperationException("Userinfo sem sub.");
        var username = GetString(root, "preferred_username") ?? throw new InvalidOperationException("Userinfo sem preferred_username.");
        var groups = new List<string>();
        if (root.TryGetProperty("groups", out var groupsElement) && groupsElement.ValueKind == JsonValueKind.Array)
        {
            groups.AddRange(groupsElement.EnumerateArray()
                .Where(g => g.ValueKind == JsonValueKind.String)
                .Select(g => g.GetString()!));
        }

        return new AuthentikIdentity(subject, username, GetString(root, "name"), groups);
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private async Task<Discovery> GetDiscovery(CancellationToken ct)
    {
        var issuer = Config.Issuer.TrimEnd('/') + "/";
        if (_discovery is not null && _discoveryIssuer == issuer)
        {
            return _discovery;
        }

        await _discoveryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_discovery is not null && _discoveryIssuer == issuer)
            {
                return _discovery;
            }

            var http = _httpClientFactory.CreateClient();
            var body = await http.GetStringAsync(issuer + ".well-known/openid-configuration", ct).ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            _discovery = new Discovery(
                GetString(root, "authorization_endpoint") ?? throw new InvalidOperationException("Discovery sem authorization_endpoint."),
                GetString(root, "token_endpoint") ?? throw new InvalidOperationException("Discovery sem token_endpoint."),
                GetString(root, "userinfo_endpoint") ?? throw new InvalidOperationException("Discovery sem userinfo_endpoint."));
            _discoveryIssuer = issuer;
            return _discovery;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private sealed record Discovery(string AuthorizationEndpoint, string TokenEndpoint, string UserinfoEndpoint);
}
