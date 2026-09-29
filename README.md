# obsidian-mcp

English | [简体中文](README.zh-CN.md)

An [MCP](https://modelcontextprotocol.io/) server that lets an AI client read
and write the notes in an Obsidian vault. It works on plain files in a mounted
directory, so it doesn't care how the vault is synced (WebDAV, Syncthing, a
NAS share…), and it only accepts OAuth access tokens, so you can add it to
Claude, ChatGPT or Cursor like any other remote connector.

## How the three repos fit together

- [nas-auth](https://github.com/ZhengchenTao/nas-auth): the authorization
  server. Signs users in and issues tokens.
- [obsidian-mcp](https://github.com/ZhengchenTao/obsidian-mcp): MCP server for
  an Obsidian vault (read and write).
- [gitea-mcp](https://github.com/ZhengchenTao/gitea-mcp): MCP server for a
  Gitea instance (read-only).

An MCP client finds the authorization server through the MCP server's
metadata, registers itself, lets the user sign in, and gets a token that is
only valid for that one MCP server. The MCP servers check the token against
nas-auth's public keys. They never see a password or a shared secret. Each
repo also works on its own: the MCP servers accept tokens from any standard
OAuth server, and nas-auth can front any service that verifies JWTs.

## How a request gets in

```
MCP client (Claude, ChatGPT, Cursor …)
    │ 1. GET /.well-known/oauth-protected-resource   → which auth server to use
    │ 2. OAuth authorization code + PKCE              (against that server)
    │ 3. POST /mcp with Bearer <JWT>  (aud=obsidian, scope read:obsidian / write:obsidian)
    ▼
obsidian-mcp
    │ checks the JWT (RS256 via JWKS, or an HS256 shared key)
    │ resolves every path inside the vault root, applies the blacklist
    ▼
/vault   (any mounted directory)
```

## Tools

| Tool | Scope | |
|---|---|---|
| `list_vault_tree` | `read:obsidian` | Directory tree, depth-limited |
| `list_files` | `read:obsidian` | Files and folders in one directory |
| `read_file` | `read:obsidian` | File content (UTF-8); `offset` / `limit` in bytes for large files |
| `search` | `read:obsidian` | Plain substring search, optional glob filter |
| `get_metadata` | `read:obsidian` | Size, modified time, whether it has front matter |
| `write_file` | `write:obsidian` | Create or overwrite a file |
| `append_file` | `write:obsidian` | Append to a file |

Writes are allowed wherever reads are. The limits are the blacklist and path
safety: no `..`, no absolute paths, symlinks are refused. `.obsidian`,
`.trash` and `.git` are always hidden. To keep a folder (say, one with
passwords) out of reach entirely, add it to `Vault__Blacklist__N`.

## Configuration

Environment variables, `__` for nesting.

| Variable | Default | |
|---|---|---|
| `Vault__Root` | `/vault` | Vault directory inside the container |
| `Vault__Blacklist__N` | – | Extra path segments to hide from reads and writes |
| `Jwt__Algorithm` | `HS256` | `RS256` (keys from the issuer's JWKS) or `HS256` (shared key) |
| `Jwt__Issuer` | – | Expected `iss`. Required. In RS256 mode keys are fetched from `<Issuer>/.well-known/openid-configuration`. |
| `Jwt__Audience` | `obsidian` | Expected `aud` |
| `Jwt__ValidTypes__N` | – | RS256 only: allowed `typ` header values. Set `at+jwt` for nas-auth, whose id_tokens share the signing key. Empty means no check, for providers whose tokens say `typ: JWT`. |
| `Jwt__SigningKey__Current` / `__Previous` | – | HS256 only: the key shared with your auth server, and the previous one during rotation |
| `Mcp__OAuthDiscovery__Issuer` | – | Required. Published in this server's OAuth metadata. |
| `Mcp__OAuthDiscovery__AuthorizationEndpoint` | – | Required |
| `Mcp__OAuthDiscovery__TokenEndpoint` | – | Required |
| `Mcp__OAuthDiscovery__RegistrationEndpoint` | – | The auth server's `/register`, if it supports dynamic registration |
| `Mcp__OAuthDiscovery__ResourceUrl` | request host | The RFC 9728 `resource` value. Must match what the auth server expects. |
| `AuditLog__Directory` | `/app/logs` | One audit log file per day |

With nas-auth that comes down to:

```
Jwt__Algorithm=RS256
Jwt__Issuer=https://auth.example.com
Jwt__ValidTypes__0=at+jwt
Mcp__OAuthDiscovery__Issuer=https://auth.example.com
Mcp__OAuthDiscovery__AuthorizationEndpoint=https://auth.example.com/authorize
Mcp__OAuthDiscovery__TokenEndpoint=https://auth.example.com/token
Mcp__OAuthDiscovery__RegistrationEndpoint=https://auth.example.com/register
Mcp__OAuthDiscovery__ResourceUrl=https://obsidian-mcp.example.com
```

and an `obsidian` entry in nas-auth's `resources.json` with the same
`resource_url`.

## Which auth server

Claude and other remote-connector clients insist on the full OAuth flow:
authorization code with PKCE, discovered from this server's metadata. There is
no "paste a token" option. Whatever you use has to support PKCE, dynamic client
registration (so the client can register itself), the `resource` parameter
(RFC 8707) and custom scopes (`read:obsidian`, `write:obsidian`).

- [nas-auth](https://github.com/ZhengchenTao/nas-auth) is the one this server
  was written against. Small, self-hosted, RS256 mode as shown above.
- Hosted: [Logto](https://logto.io), [ZITADEL](https://zitadel.com),
  [Auth0](https://auth0.com). Use RS256 mode with your tenant's issuer URL.
- Self-hosted and bigger: [Keycloak](https://www.keycloak.org),
  [Authentik](https://goauthentik.io), or ZITADEL / Logto on your own box.
- HS256 mode is for a minimal auth server you write yourself; it needs the same
  key on both sides.

## Running it

```bash
docker build -t obsidian-mcp .

docker run --rm -p 8080:8080 \
  -v /path/to/vault:/vault \
  -e Jwt__Algorithm=RS256 \
  -e Jwt__Issuer=https://auth.example.com \
  -e Jwt__ValidTypes__0=at+jwt \
  -e Mcp__OAuthDiscovery__Issuer=https://auth.example.com \
  -e Mcp__OAuthDiscovery__AuthorizationEndpoint=https://auth.example.com/authorize \
  -e Mcp__OAuthDiscovery__TokenEndpoint=https://auth.example.com/token \
  -e Vault__Blacklist__0=Private \
  obsidian-mcp
```

The container user has to be able to write to the vault if you want
`write_file` / `append_file`; mount it read-only (`:ro`) if you don't.

For local development it's easiest to use HS256 and mint a token yourself:

```bash
mkdir -p test-vault/Notes && echo "# Test" > test-vault/Notes/test.md

export Vault__Root=./test-vault
export Jwt__Issuer=https://auth.example.com
export Jwt__SigningKey__Current=dev-secret-key-at-least-32-chars-long
export Mcp__OAuthDiscovery__Issuer=https://auth.example.com
export Mcp__OAuthDiscovery__AuthorizationEndpoint=https://auth.example.com/authorize
export Mcp__OAuthDiscovery__TokenEndpoint=https://auth.example.com/token
dotnet run

dotnet user-jwts create --issuer https://auth.example.com --audience obsidian \
  --name tester --claim sub=tester --claim scope="read:obsidian write:obsidian"

npx @modelcontextprotocol/inspector
# Streamable HTTP, http://localhost:5000/mcp, paste the token as Bearer
```

Tests: `dotnet test obsidian-mcp.Tests`.

## CI

`.gitea/workflows/build-image.yml` builds and pushes
`<REGISTRY>/<IMAGE_OWNER>/obsidian-mcp` on every push to `main` and can then
trigger a redeploy over SSH. It reads `vars.REGISTRY`, `vars.IMAGE_OWNER`,
`secrets.AIFACELY_REGISTRY_TOKEN`, and for the deploy step
`vars.DEPLOY_SERVICE`, `secrets.NAS_CI_SSH_KEY`, `secrets.NAS_SSH_HOST`,
`secrets.NAS_SSH_KNOWN_HOSTS`. The action URLs and build proxy match my own CI;
change them in a fork.

## License

MIT
