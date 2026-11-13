# Bergafin Auth

Plugin do Jellyfin (12.x) para entrar **só pelo Authentik**.

- **Login OIDC** com o Authentik (authorization code + PKCE). Os grupos definem o acesso: `jellyfin_users` entra e `jellyfin_admins` vira admin. O nível de admin é atualizado a cada login.
- **Senha bloqueada:** as contas do Authentik ficam com o provedor `Authentik (Bergafin)`, que recusa qualquer login por senha. Nenhum app consegue entrar com usuário e senha nessas contas.
- **Quick Connect pelo Authentik:** em `/Bergafin/QuickConnect` você digita o código do aparelho, confirma quem é no Authentik e o aparelho entra na sua conta. Serve para TV, apps oficiais e o Seerr.
- **App Android (Bergafin):** `/Bergafin/Login?mode=app` volta para `bergafin://login?ticket=…`. O app troca o ticket por uma sessão em `POST /Bergafin/Exchange`.
- **Contas locais** (provedor padrão, com senha do Jellyfin) nunca são ligadas ao Authentik. Para prendê-las à rede de casa, desligue *Permitir acesso remoto* nelas. O Jellyfin confere isso **em toda requisição**, não só no login.

## Instalação

1. Em *Painel → Plugins → Repositórios*, adicione:
   `https://raw.githubusercontent.com/Agelinena/Bergafin-auth/manifest/manifest.json`
2. Instale **Bergafin Auth** pelo catálogo e reinicie o Jellyfin.
3. No Authentik, crie um provider **OAuth2/OpenID** confidencial:
   - **Redirect URI:** `https://SEU-JELLYFIN/Bergafin/Callback`;
   - **Escopos:** `openid`, `profile` e `email` (o `profile` do Authentik já envia `groups`).
4. Em *Painel → Plugins → Bergafin Auth*, preencha o endereço público, o issuer, o client ID e o client secret.
5. Em *Painel → Rede*, configure:
   - **Proxies conhecidos:** a faixa do proxy reverso;
   - **Redes locais:** a rede de casa.

   Sem isso, o Jellyfin não vê o IP real do cliente.

## Rotas

| Rota | O que faz |
|---|---|
| `GET /Bergafin/Login` | Login no navegador |
| `GET /Bergafin/Login?mode=app` | Login do app Android |
| `GET /Bergafin/QuickConnect` | Aprova um código de Quick Connect |
| `GET /Bergafin/Callback` | Volta do Authentik |
| `POST /Bergafin/Exchange` | Troca o ticket (uso único, 2 min) por uma sessão |

## Versões

Um workflow diário confere a última versão estável do Jellyfin e compila o plugin contra ela:

- **Se compilar:** publica um patch novo, e o Jellyfin atualiza o plugin sozinho.
- **Se não compilar:** abre uma issue, e a versão anterior continua valendo.

Licença: GPL-2.0, a mesma do Jellyfin.
