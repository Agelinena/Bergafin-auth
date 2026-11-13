using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Bergafin.Auth.Oidc;

/// <summary>Para onde o login volta depois do Authentik.</summary>
public enum LoginMode
{
    /// <summary>Navegador: abre o Jellyfin já logado.</summary>
    Web,

    /// <summary>App Android: volta para bergafin://login com um ticket.</summary>
    App,

    /// <summary>Aprova um código de Quick Connect.</summary>
    QuickConnect,
}

/// <summary>Login em andamento (entre ir ao Authentik e voltar).</summary>
public sealed record PendingLogin(string Verifier, LoginMode Mode, string? QuickConnectCode, DateTime Created);

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
