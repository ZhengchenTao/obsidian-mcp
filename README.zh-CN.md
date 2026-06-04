# obsidian-mcp

[English](README.md) | 简体中文

通过 [MCP（Model Context Protocol）](https://modelcontextprotocol.io/) 协议读写 Obsidian vault，访问由 OAuth 签发的 JWT bearer token 控制。

本 server 可对接任何能签发包含 `aud` / `iss` / `sub` / `scope` claim 的 OAuth 2.1 + PKCE AS。支持两种签名模式：

- **HS256**（默认）—— AS 与本 server 共享对称密钥。用于自建的极简 AS。
- **RS256** —— 自动通过 `<Issuer>/.well-known/openid-configuration` 的 OIDC discovery 拉取 JWKS。可对接任意标准 provider：[Logto](https://logto.io)、[ZITADEL](https://zitadel.com)、[Keycloak](https://www.keycloak.org)、[Auth0](https://auth0.com) 等。

部署指引见下方 [Choosing an AS](#choosing-an-as)。

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
    │  VaultPathResolver — chroot + blacklist (读写共用同一道门禁)
    │
    ▼
/vault  (任意挂载目录 —— 本地文件夹 / WebDAV / NFS / SMB 同步目标皆可。
         本服务只读写本地路径,不实现任何同步协议。)
```

## MCP tools

| 工具 | 所需 scope | 说明 |
|---|---|---|
| `list_vault_tree` | `read:obsidian` | 限定深度的 vault 目录树 |
| `list_files` | `read:obsidian` | 列出某个目录下的文件与子目录 |
| `read_file` | `read:obsidian` | 读取文件内容（UTF-8，可选 byte-range 参数） |
| `search` | `read:obsidian` | 纯字符串子串搜索，可用 glob 过滤 |
| `get_metadata` | `read:obsidian` | size、modified_at、has_frontmatter |
| `write_file` | `write:obsidian` | 覆盖写任意非黑名单文件 |
| `append_file` | `write:obsidian` | 在任意非黑名单文件末尾追加 |

## Configuration

所有配置项通过 `Vault__` / `Jwt__` / `Mcp__OAuthDiscovery__` 前缀绑定（double underscore 表示嵌套 section）。生产环境必须通过环境变量注入。

| 变量 | 默认值 | 必填 | 说明 |
|---|---|---|---|
| `Vault__Root` | `/vault` | 是 | 容器内的 vault 根目录 |
| `Vault__Blacklist__0` | — | 否 | 额外要拒绝的路径片段，读写都挡（`.obsidian`、`.trash`、`.git` 始终被拒） |
| `Jwt__Algorithm` | `HS256` | 否 | `HS256` 或 `RS256` |
| `Jwt__Issuer` | — | **是** | 期望的 `iss` claim —— 你 AS 的 issuer URL |
| `Jwt__Audience` | `obsidian` | 否 | 期望的 `aud` claim |
| `Jwt__SigningKey__Current` | — | 仅 HS256 | HS256 签名密钥，与你的 AS 共享 |
| `Jwt__SigningKey__Previous` | — | 否 | 轮换窗口内的旧 HS256 密钥 |
| `Mcp__OAuthDiscovery__Issuer` | — | **是** | `/.well-known/oauth-authorization-server` 中的 `issuer` 字段 |
| `Mcp__OAuthDiscovery__AuthorizationEndpoint` | — | **是** | 你 AS 的 `/authorize` URL |
| `Mcp__OAuthDiscovery__TokenEndpoint` | — | **是** | 你 AS 的 `/token` URL |
| `Mcp__OAuthDiscovery__RegistrationEndpoint` | — | 否 | 你 AS 的 `/register` URL（DCR） |
| `Mcp__OAuthDiscovery__ResourceUrl` | request host | 否 | RFC 9728 中本 MCP server 的 `resource` 标识 |
| `AuditLog__Directory` | `/app/logs` | 否 | 审计日志目录 |
| `ASPNETCORE_ENVIRONMENT` | `Production` | 否 | `Development` 启用详细日志 |

### 写入门禁

写入（`write_file` / `append_file`）和读取走同一道门禁：只受 `Vault__Blacklist`
加路径安全（禁穿越、禁绝对路径、禁 symlink）约束，命中黑名单以外的任意路径都能写。
想让某个目录读写双禁（例如密码目录），把它的路径段加进 `Vault__Blacklist__N` 即可。

## Local development

```bash
# 1. 建一个测试 vault
mkdir -p test-vault/Notes
echo "# Test" > test-vault/Notes/test.md

# 2. 设置必要的环境变量
export Vault__Root=./test-vault
export Jwt__Issuer=https://your-auth-server.example.com
export Jwt__Audience=obsidian
export Jwt__SigningKey__Current=dev-secret-key-at-least-32-chars-long
export Mcp__OAuthDiscovery__Issuer=https://your-auth-server.example.com
export Mcp__OAuthDiscovery__AuthorizationEndpoint=https://your-auth-server.example.com/authorize
export Mcp__OAuthDiscovery__TokenEndpoint=https://your-auth-server.example.com/token

# 3. 跑起来
dotnet run

# 4. 生成测试用 JWT（需要 dotnet user-jwts）
dotnet user-jwts create \
    --issuer https://your-auth-server.example.com \
    --audience obsidian \
    --name tester \
    --claim sub=tester \
    --claim scope="read:obsidian write:obsidian"

# 5. 用 MCP Inspector 测试
npx @modelcontextprotocol/inspector
# Transport: Streamable HTTP
# URL: http://localhost:5000/mcp
# Bearer Token: <粘贴步骤 4 得到的 JWT>
```

## Docker

仓库内附 multi-stage Dockerfile。本地构建：

```bash
docker build -t obsidian-mcp .
```

挂载 vault 后运行：

```bash
docker run --rm -p 8080:8080 \
  -v /path/to/vault:/vault \
  -e Jwt__Issuer=https://your-auth-server.example.com \
  -e Jwt__SigningKey__Current=$JWT_SIGNING_KEY \
  -e Mcp__OAuthDiscovery__Issuer=https://your-auth-server.example.com \
  -e Mcp__OAuthDiscovery__AuthorizationEndpoint=https://your-auth-server.example.com/authorize \
  -e Mcp__OAuthDiscovery__TokenEndpoint=https://your-auth-server.example.com/token \
  -e Vault__Blacklist__0=01-Secret \
  obsidian-mcp
```

仓库内的 `.gitea/workflows/build-image.yml` 是一个 Gitea Actions workflow，负责构建并推送镜像，并可选在 runner 主机上重启容器（由下方 `vars.DEPLOY_PATH` 控制）。需要在仓库设置中配置：

- `vars.REGISTRY` —— registry 主机名（例如 `ghcr.io`，自建 Gitea Container Registry 写 `git.example.com`）
- `vars.IMAGE_OWNER` —— registry 的 owner / namespace
- `secrets.PACKAGES_TOKEN` —— registry 推送 token
- `vars.DEPLOY_PATH` —— *(可选)* runner 主机上某个 docker-compose 目录的路径。配上之后 workflow 会跑一个 `deploy` 后续 job：`cd` 到这个目录后 `docker compose up -d` 拉新镜像。留空只 build & push。

## Choosing an AS

Claude.ai 网页端强制走完整的 OAuth Authorization Code + PKCE 流程，对接你 MCP server 的 `/.well-known/oauth-authorization-server` 端点 —— 没有 bearer token 捷径可用。在下面几条路径中选一条：

**Hosted (fastest start, recommended for new setups)** —— RS256 模式：

| Provider | 免费额度 | 备注 |
|---|---|---|
| [Logto Cloud](https://logto.io) | 5000 MAU | 最轻量，约 30 分钟搭好 |
| [ZITADEL Cloud](https://zitadel.com) | 25k auths / 月 | 功能更全，文档稍重 |

设置 `Jwt__Algorithm=RS256` 与 `Jwt__Issuer=<你的 tenant issuer URL>`。公钥会自动从 `<Issuer>/.well-known/openid-configuration` 拉取。

**Self-hosted, full-featured** —— RS256 模式：
[Keycloak](https://www.keycloak.org)、[ZITADEL](https://github.com/zitadel/zitadel)、[Logto](https://github.com/logto-io/logto)、[Authentik](https://goauthentik.io)。

**Self-hosted, minimal** —— HS256 模式：
参见 [nas-auth](https://github.com/ZhengchenTao/nas-auth) —— 本 server 在开发过程中对接的那个约 500 行 LoC 的参考 AS。或者自己写一个。MCP server 的 `Jwt__SigningKey__Current` 必须与 AS 的签名密钥保持一致。

**不论选哪条，AS 必须支持：**
- OAuth 2.1 + PKCE（RFC 7636）
- Dynamic Client Registration（RFC 7591）—— 让 Claude.ai 能自助注册
- `resource` 参数（RFC 8707）—— 用于签发 audience-bound token
- 自定义 scope 支持（`read:obsidian`、`write:obsidian`）

## Running tests

```bash
cd obsidian-mcp.Tests
dotnet test
```

## License

MIT
