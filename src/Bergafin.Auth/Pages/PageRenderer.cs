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
