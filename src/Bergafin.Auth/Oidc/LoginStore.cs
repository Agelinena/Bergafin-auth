using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Bergafin.Auth.Oidc;

/// <summary>Para onde o login volta depois do Authentik.</summary>
public enum LoginMode
{
    /// <summary>Navegador com o web oficial: página do plugin que grava o login e abre o Jellyfin.</summary>
    Web,

    /// <summary>Bergafin-web: volta para /web/#/login?bergafinTicket=…, e o próprio web troca o ticket.</summary>
    WebFork,

    /// <summary>App Android: volta para bergafin://login com um ticket.</summary>
    App,

    /// <summary>Aprova um código de Quick Connect.</summary>
    QuickConnect,
}

/// <summary>Login em andamento (entre ir ao Authentik e voltar).</summary>
/// <remarks>ClientState: valor aleatório do web (ou do app) que volta junto com o ticket. O web só aceita
/// o ticket se o state for o que ele mesmo guardou, então um link de terceiros não faz o
/// aparelho entrar em outra conta.</remarks>
public sealed record PendingLogin(string Verifier, LoginMode Mode, string? QuickConnectCode, string? ClientState, DateTime Created);

/// <summary>Ticket de uso único que troca o login do Authentik por uma sessão do Jellyfin.</summary>
public sealed record LoginTicket(Guid UserId, string Username, DateTime Created);

/// <summary>
/// Guarda, só em memória, os logins em andamento e os tickets. Reiniciar o Jellyfin
/// invalida tudo, o que só obriga a clicar em "Entrar" de novo.
/// </summary>
public sealed class LoginStore
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(2);
    private const int MaxEntries = 2000;

    private readonly ConcurrentDictionary<string, PendingLogin> _pending = new();
    private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new();

    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Guarda o login e devolve o state para mandar ao Authentik.</summary>
    public string AddPending(PendingLogin login)
    {
        Sweep();
        var state = NewSecret();
        _pending[state] = login;
        return state;
    }

    public PendingLogin? TakePending(string state)
        => _pending.TryRemove(state, out var login) && DateTime.UtcNow - login.Created < PendingTtl ? login : null;

    public string AddTicket(LoginTicket ticket)
    {
        Sweep();
        var id = NewSecret();
        _tickets[id] = ticket;
        return id;
    }

    public LoginTicket? TakeTicket(string id)
        => _tickets.TryRemove(id, out var ticket) && DateTime.UtcNow - ticket.Created < TicketTtl ? ticket : null;

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, value) in _pending)
        {
            if (now - value.Created >= PendingTtl)
            {
                _pending.TryRemove(key, out _);
            }
        }

        foreach (var (key, value) in _tickets)
        {
            if (now - value.Created >= TicketTtl)
            {
                _tickets.TryRemove(key, out _);
            }
        }

        // Proteção contra quem abre milhares de logins sem terminar
        if (_pending.Count > MaxEntries)
        {
            _pending.Clear();
        }

        if (_tickets.Count > MaxEntries)
        {
            _tickets.Clear();
        }
    }
}
