using System.Net.Mime;
using System.Text.RegularExpressions;
using Bergafin.Auth.Oidc;
using Bergafin.Auth.Pages;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Bergafin.Auth.Api;

/// <summary>
/// Rotas do login pelo Authentik:
///   GET  /Bergafin/Info                    se o login pelo Authentik está configurado
///   GET  /Bergafin/Login?mode=web|webfork|app  vai para o Authentik
///   GET  /Bergafin/QuickConnect[?code=]    aprova um código de Quick Connect pelo Authentik
///   GET  /Bergafin/Callback                volta do Authentik
///   POST /Bergafin/Exchange                troca o ticket por uma sessão do Jellyfin
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("Bergafin")]
public partial class BergafinController : ControllerBase
{
    /// <summary>Esquema do deep link do app Bergafin (fixo, para não virar redirecionamento aberto).</summary>
    public const string AppCallback = "bergafin://login";

    private readonly LoginStore _store;
    private readonly AuthentikClient _authentik;
    private readonly UserProvisioner _provisioner;
    private readonly ISessionManager _sessionManager;
    private readonly IQuickConnect _quickConnect;
    private readonly ILogger<BergafinController> _logger;

    public BergafinController(
        LoginStore store,
        AuthentikClient authentik,
        UserProvisioner provisioner,
        ISessionManager sessionManager,
        IQuickConnect quickConnect,
        ILogger<BergafinController> logger)
    {
        _store = store;
        _authentik = authentik;
        _provisioner = provisioner;
        _sessionManager = sessionManager;
        _quickConnect = quickConnect;
        _logger = logger;
    }

    /// <summary>O Bergafin-web pergunta aqui se mostra o botão do Authentik.</summary>
    [HttpGet("Info")]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult Info()
        => Ok(new { Configured = AuthentikClient.IsConfigured, Version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) });

    [HttpGet("Login")]
    public Task<ActionResult> Login([FromQuery] string? mode, CancellationToken ct)
    {
        var loginMode = mode?.ToLowerInvariant() switch
        {
            "app" => LoginMode.App,
            "webfork" => LoginMode.WebFork,
            _ => LoginMode.Web,
        };
        return StartLogin(loginMode, null, ct);
    }

    [HttpGet("QuickConnect")]
    public Task<ActionResult> QuickConnect([FromQuery] string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult<ActionResult>(Html(PageRenderer.QuickConnectForm()));
        }

        code = code.Trim();
        if (!QuickConnectCode().IsMatch(code))
        {
            return Task.FromResult<ActionResult>(Html(PageRenderer.Message("Código inválido", "O código do Quick Connect tem só números. Confira na tela do aparelho."), 400));
        }

        return StartLogin(LoginMode.QuickConnect, code, ct);
    }

    [HttpGet("Callback")]
    public async Task<ActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return Html(PageRenderer.Message("Login cancelado", "O Authentik não completou o login (" + error + ")."), 400);
        }

        var pending = string.IsNullOrEmpty(state) ? null : _store.TakePending(state);
        if (pending is null || string.IsNullOrEmpty(code))
        {
            return Html(PageRenderer.Message("Login expirado", "Esse login demorou demais ou já foi usado. Tente de novo."), 400);
        }

        AuthentikIdentity identity;
        try
        {
            identity = await _authentik.Redeem(code, pending.Verifier, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
        {
            _logger.LogWarning(e, "Falha ao concluir o login no Authentik.");
            return Html(PageRenderer.Message("Falha no login", "Não foi possível confirmar o login no Authentik. Tente de novo."), 502);
        }

        var result = await _provisioner.Provision(identity).ConfigureAwait(false);
        if (result.User is null)
        {
            return Html(PageRenderer.Message("Acesso negado", result.Denied!), 403);
        }

        var user = result.User;
        switch (pending.Mode)
        {
            case LoginMode.QuickConnect:
                return await ApproveQuickConnect(user.Id, user.Username, pending.QuickConnectCode!).ConfigureAwait(false);

            case LoginMode.App:
                var appTicket = _store.AddTicket(new LoginTicket(user.Id, user.Username, DateTime.UtcNow));
                return Redirect(AppCallback + "?ticket=" + Uri.EscapeDataString(appTicket));

            case LoginMode.WebFork:
                var forkTicket = _store.AddTicket(new LoginTicket(user.Id, user.Username, DateTime.UtcNow));
                return Redirect(Plugin.Instance!.Configuration.PublicUrl.TrimEnd('/') + "/web/#/login?bergafinTicket=" + Uri.EscapeDataString(forkTicket));

            default:
                var webTicket = _store.AddTicket(new LoginTicket(user.Id, user.Username, DateTime.UtcNow));
                return Html(PageRenderer.WebLogin(webTicket));
        }
    }

    [HttpPost("Exchange")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> Exchange([FromBody] ExchangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Ticket)
            || string.IsNullOrWhiteSpace(request.DeviceId)
            || string.IsNullOrWhiteSpace(request.DeviceName)
            || string.IsNullOrWhiteSpace(request.App)
            || string.IsNullOrWhiteSpace(request.AppVersion))
        {
            return BadRequest("Faltam dados do aparelho.");
        }

        var ticket = _store.TakeTicket(request.Ticket);
        if (ticket is null)
        {
            return Unauthorized("Ticket inválido ou expirado.");
        }

        var session = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
        {
            UserId = ticket.UserId,
            Username = ticket.Username,
            App = request.App,
            AppVersion = request.AppVersion,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        }).ConfigureAwait(false);

        _logger.LogInformation("Authentik: sessão criada para {User} em {Device} ({App}).", ticket.Username, request.DeviceName, request.App);
        return Ok(session);
    }

    private async Task<ActionResult> StartLogin(LoginMode mode, string? quickConnectCode, CancellationToken ct)
    {
        if (!AuthentikClient.IsConfigured)
        {
            return Html(PageRenderer.Message("Login indisponível", "O login pelo Authentik ainda não foi configurado no servidor."), 503);
        }

        var verifier = LoginStore.NewSecret();
        var state = _store.AddPending(new PendingLogin(verifier, mode, quickConnectCode, DateTime.UtcNow));
        try
        {
            return Redirect(await _authentik.BuildAuthorizeUrl(state, verifier, ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
        {
            _logger.LogError(e, "Não foi possível falar com o Authentik.");
            return Html(PageRenderer.Message("Authentik fora do ar", "Não foi possível falar com o Authentik. Tente de novo em instantes."), 502);
        }
    }

    private async Task<ActionResult> ApproveQuickConnect(Guid userId, string username, string code)
    {
        try
        {
            await _quickConnect.AuthorizeRequest(userId, code).ConfigureAwait(false);
        }
        catch (ResourceNotFoundException)
        {
            return Html(PageRenderer.Message("Código não encontrado", "Esse código não existe ou expirou. Gere outro no aparelho."), 404);
        }
        catch (Exception e) when (e is InvalidOperationException or MediaBrowser.Controller.Authentication.AuthenticationException)
        {
            return Html(PageRenderer.Message("Não foi possível aprovar", e.Message), 400);
        }

        _logger.LogInformation("Authentik: {User} aprovou um Quick Connect.", username);
        return Html(PageRenderer.Message("Aparelho conectado", "Pronto, " + username + ". O aparelho já está entrando na sua conta."));
    }

    private ContentResult Html(string html, int status = 200)
        => new() { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };

    [GeneratedRegex("^[0-9]{6,10}$")]
    private static partial Regex QuickConnectCode();
}

/// <summary>Pedido de troca do ticket por uma sessão.</summary>
public sealed class ExchangeRequest
{
    public string? Ticket { get; set; }

    public string? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? App { get; set; }

    public string? AppVersion { get; set; }
}
