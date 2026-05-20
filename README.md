# obsidian-mcp

English | [简体中文](README.zh-CN.md)

Read and write an Obsidian vault via [MCP (Model Context Protocol)](https://modelcontextprotocol.io/),
gated by OAuth-issued JWT bearer tokens.

This server pairs with any OAuth 2.1 + PKCE authorization server that can mint
JWTs containing `aud`, `iss`, `sub`, `scope` claims. Two signing modes:

- **HS256** (default) — shared symmetric key between AS and this server. Use for self-built minimal AS.
- **RS256** — fetches JWKS automatically via OIDC discovery from `<Issuer>/.well-known/openid-configuration`. Use with any standard provider: [Logto](https://logto.io), [ZITADEL](https://zitadel.com), [Keycloak](https://www.keycloak.org), [Auth0](https://auth0.com), etc.

See [Choosing an AS](#choosing-an-as) below for setup guidance.

## Architecture

```
MCP client (Claude.ai, etc.)
    │
    │ ① GET /.well-known/oauth-authorization-server  (RFC 8414)
    │ ② OAuth Authorization Code + PKCE  (against your AS)
    │ ③ Bearer JWT (aud=obsidian, scope=read:obsidian | write:obsidian)
    │
    ▼
obsidian-mcp /mcp
    │  JWT verify (HS256, shared key with AS)
    │  VaultPathResolver — chroot + blacklist
    │  VaultWriteGuard   — whitelist for writes
    │
    ▼
/vault  (mounted directory — local folder, WebDAV sync target, etc.)
```

## MCP tools

| Tool | Scope required | Description |
|---|---|---|
| `list_vault_tree` | `read:obsidian` | Depth-limited directory tree of the vault |
| `list_files` | `read:obsidian` | Files and subdirs in a directory |
| `read_file` | `read:obsidian` | File content (UTF-8), with optional byte-range params |
| `search` | `read:obsidian` | Literal substring search, glob-filterable |
| `get_metadata` | `read:obsidian` | Size, modified_at, has_frontmatter |
| `write_file` | `write:obsidian` | Overwrite a whitelisted file |
| `append_file` | `write:obsidian` | Append to a whitelisted file |

## Configuration

All settings are bound from configuration with `Vault__`, `Jwt__`, `Mcp__OAuthDiscovery__`
prefixes (double underscore = nested section). Production values must be injected via env vars.

| Variable | Default | Required | Description |
|---|---|---|---|
| `Vault__Root` | `/vault` | yes | Vault root directory inside the container |
| `Vault__Blacklist__0` | — | no | Extra path segments to deny (`.obsidian`, `.trash`, `.git` are always denied) |
| `Vault__WriteWhitelist__0` | — | for write tools | Writable path entries (see below) |
| `Jwt__Algorithm` | `HS256` | no | `HS256` or `RS256` |
| `Jwt__Issuer` | — | **yes** | Expected `iss` claim — your AS's issuer URL |
| `Jwt__Audience` | `obsidian` | no | Expected `aud` claim |
| `Jwt__SigningKey__Current` | — | HS256 only | HS256 signing key, shared with your AS |
| `Jwt__SigningKey__Previous` | — | no | Previous HS256 key during rotation window |
| `Mcp__OAuthDiscovery__Issuer` | — | **yes** | `/.well-known/oauth-authorization-server` `issuer` field |
| `Mcp__OAuthDiscovery__AuthorizationEndpoint` | — | **yes** | Your AS's `/authorize` URL |
| `Mcp__OAuthDiscovery__TokenEndpoint` | — | **yes** | Your AS's `/token` URL |
| `Mcp__OAuthDiscovery__RegistrationEndpoint` | — | no | Your AS's `/register` URL (DCR) |
| `Mcp__OAuthDiscovery__ResourceUrl` | request host | no | RFC 9728 `resource` identifier for this MCP server |
| `AuditLog__Directory` | `/app/logs` | no | Directory for audit log files |
| `ASPNETCORE_ENVIRONMENT` | `Production` | no | `Development` for verbose logs |

### Write whitelist format

`Vault__WriteWhitelist__N` entries gate every write/append operation:

- Ending with `/` (or `\`) → prefix match. Example: `Notes/` allows any path under `Notes/`.
- Otherwise → exact path match. Example: `todo.md` allows only that one file.

Always forbidden regardless of whitelist: any path whose filename is `AGENTS.md`,
`README.md`, or `CLAUDE.md` (these are common agent-context files; mutating them
tends to confuse downstream tooling).

If `WriteWhitelist` is empty, all writes are denied.

## Local development

```bash
# 1. Create a test vault
mkdir -p test-vault/Notes
echo "# Test" > test-vault/Notes/test.md

# 2. Set required env vars
export Vault__Root=./test-vault
export Vault__WriteWhitelist__0=Notes/
export Jwt__Issuer=https://your-auth-server.example.com
export Jwt__Audience=obsidian
export Jwt__SigningKey__Current=dev-secret-key-at-least-32-chars-long
export Mcp__OAuthDiscovery__Issuer=https://your-auth-server.example.com
export Mcp__OAuthDiscovery__AuthorizationEndpoint=https://your-auth-server.example.com/authorize
export Mcp__OAuthDiscovery__TokenEndpoint=https://your-auth-server.example.com/token

# 3. Run
dotnet run

# 4. Generate a test JWT (requires dotnet user-jwts)
dotnet user-jwts create \
    --issuer https://your-auth-server.example.com \
    --audience obsidian \
    --name tester \
    --claim sub=tester \
    --claim scope="read:obsidian write:obsidian"

# 5. Test with MCP Inspector
npx @modelcontextprotocol/inspector
# Transport: Streamable HTTP
# URL: http://localhost:5000/mcp
# Bearer Token: <paste JWT from step 4>
```

## Docker

A multi-stage Dockerfile is included. Build locally with:

```bash
docker build -t obsidian-mcp .
```

Run with a mounted vault:

```bash
docker run --rm -p 8080:8080 \
  -v /path/to/vault:/vault \
  -e Jwt__Issuer=https://your-auth-server.example.com \
  -e Jwt__SigningKey__Current=$JWT_SIGNING_KEY \
  -e Mcp__OAuthDiscovery__Issuer=https://your-auth-server.example.com \
  -e Mcp__OAuthDiscovery__AuthorizationEndpoint=https://your-auth-server.example.com/authorize \
  -e Mcp__OAuthDiscovery__TokenEndpoint=https://your-auth-server.example.com/token \
  -e Vault__WriteWhitelist__0=Notes/ \
  obsidian-mcp
```

The included `.gitea/workflows/build-image.yml` is a Gitea Actions workflow that
builds and pushes the image. It expects these repository Variables / Secrets:

- `vars.REGISTRY` — registry hostname (e.g. `ghcr.io`)
- `vars.IMAGE_OWNER` — registry owner/namespace
- `secrets.PACKAGES_TOKEN` — registry push token

## Choosing an AS

Claude.ai chat enforces the full OAuth Authorization Code + PKCE flow against
your MCP server's `/.well-known/oauth-authorization-server` endpoint — there
is no bearer-token shortcut. Pick one of these paths:

**Hosted (fastest start, recommended for new setups)** — RS256 mode:

| Provider | Free tier | Notes |
|---|---|---|
| [Logto Cloud](https://logto.io) | 5000 MAU | Lightest, ~30 min setup |
| [ZITADEL Cloud](https://zitadel.com) | 25k auths/month | More featureful, slightly heavier docs |

Set `Jwt__Algorithm=RS256` and `Jwt__Issuer=<your-tenant-issuer-URL>`.
Public keys are fetched automatically from `<Issuer>/.well-known/openid-configuration`.

**Self-hosted, full-featured** — RS256 mode:
[Keycloak](https://www.keycloak.org), [ZITADEL](https://github.com/zitadel/zitadel), [Logto](https://github.com/logto-io/logto), [Authentik](https://goauthentik.io).

**Self-hosted, minimal** — HS256 mode:
See [nas-auth](https://github.com/ZhengchenTao/nas-auth) — the reference ~500 LoC AS this server was developed against. Or write your own. The MCP server's `Jwt__SigningKey__Current` and the AS's signing key must match.

**Required AS features regardless of choice:**
- OAuth 2.1 + PKCE (RFC 7636)
- Dynamic Client Registration (RFC 7591) — so Claude.ai can self-register
- `resource` parameter support (RFC 8707) — for audience-bound tokens
- Custom scope support (`read:obsidian`, `write:obsidian`)

## Running tests

```bash
cd obsidian-mcp.Tests
dotnet test
```

## License

MIT
