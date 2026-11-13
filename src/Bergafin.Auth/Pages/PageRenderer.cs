using System.Net;
using System.Text.Json;

namespace Bergafin.Auth.Pages;

/// <summary>Monta as páginas HTML do plugin a partir dos modelos embutidos.</summary>
public static class PageRenderer
{
    private static readonly Lazy<string> Layout = new(() => Load("layout.html"));
    private static readonly Lazy<string> WebLoginBody = new(() => Load("weblogin.html"));
    private static readonly Lazy<string> QuickConnectBody = new(() => Load("quickconnect.html"));

    public static string Message(string title, string text)
        => Page(title, "<h1>" + Enc(title) + "</h1><p>" + Enc(text) + "</p>");

    public static string QuickConnectForm()
        => Page("Conectar aparelho", QuickConnectBody.Value);

    /// <summary>Página que troca o ticket por uma sessão e abre o Jellyfin já logado.</summary>
    public static string WebLogin(string ticket)
        => Page("Entrando…", WebLoginBody.Value.Replace("{{TICKET}}", JsonSerializer.Serialize(ticket), StringComparison.Ordinal));

    /// <summary>Volta para o app Bergafin (Android) com o ticket.</summary>
    public static string AppReturn(string ticket)
    {
        // O ticket é base64url, mas escapa por garantia
        var query = "ticket=" + Uri.EscapeDataString(ticket);
        var intentUrl = "intent://login?" + query + "#Intent;scheme=bergafin;package=" + AppPackage + ";end";
        var schemeUrl = "bergafin://login?" + query;
        var body = "<h1>Pronto!</h1><p>Voltando para o Bergafin…</p>"
            + "<p><a id=\"open\" href=\"" + Enc(intentUrl) + "\"><button type=\"button\">Abrir o Bergafin</button></a></p>"
            + "<p><a href=\"" + Enc(schemeUrl) + "\">Não abriu? Toque aqui.</a></p>"
            + "<script>location.replace(" + JsonSerializer.Serialize(intentUrl) + ");</script>";
        return Page("Voltando para o app", body);
    }

    /// <summary>applicationId do Bergafin-android.</summary>
    private const string AppPackage = "io.github.agelinena.bergafin";

    private static string Page(string title, string body)
        => Layout.Value
            .Replace("{{TITLE}}", Enc(title), StringComparison.Ordinal)
            .Replace("{{BODY}}", body, StringComparison.Ordinal);

    private static string Enc(string text) => WebUtility.HtmlEncode(text);

    private static string Load(string name)
    {
        using var stream = typeof(PageRenderer).Assembly.GetManifestResourceStream("Bergafin.Auth.Pages." + name)
            ?? throw new InvalidOperationException("Página embutida não encontrada: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
