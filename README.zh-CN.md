# obsidian-mcp

[English](README.md) | 简体中文

一个 [MCP](https://modelcontextprotocol.io/) 服务，让 AI 客户端读写 Obsidian 笔记库里的笔记。它直接操作挂载进来的目录里的普通文件，笔记库怎么同步（WebDAV、Syncthing、NAS 共享……）都无所谓；它只认 OAuth access token，所以能像其他远程连接器一样加到 Claude、ChatGPT、Cursor 里。

## 三个仓怎么配合

- [nas-auth](https://github.com/ZhengchenTao/nas-auth)：授权服务，负责登录和签发 token。
- [obsidian-mcp](https://github.com/ZhengchenTao/obsidian-mcp)：Obsidian 笔记库的 MCP 服务（可读写）。
- [gitea-mcp](https://github.com/ZhengchenTao/gitea-mcp)：Gitea 的 MCP 服务（只读）。

MCP 客户端从 MCP 服务的元数据里找到授权服务，自己注册，让用户登录，拿到一个只对这一个 MCP 服务有效的 token。MCP 服务用 nas-auth 公布的公钥验 token，碰不到密码，也不需要和谁共享密钥。三个仓也可以单独用：两个 MCP 服务能接任何标准的 OAuth 服务，nas-auth 也能给任何会验 JWT 的服务做登录。

## 请求怎么进来

```
MCP 客户端（Claude、ChatGPT、Cursor …）
    │ 1. GET /.well-known/oauth-protected-resource   → 该找哪个授权服务
    │ 2. OAuth 授权码 + PKCE                           （对那个授权服务）
    │ 3. POST /mcp，带 Bearer <JWT>（aud=obsidian，scope 为 read:obsidian / write:obsidian）
    ▼
obsidian-mcp
    │ 验 JWT（RS256 走 JWKS，或 HS256 共享密钥）
    │ 所有路径都解析在笔记库根目录内，再过黑名单
    ▼
/vault（任意挂载目录）
```

## 工具

| 工具 | scope | |
|---|---|---|
| `list_vault_tree` | `read:obsidian` | 目录树，限深度 |
| `list_files` | `read:obsidian` | 某个目录下的文件和文件夹 |
| `read_file` | `read:obsidian` | 文件内容（UTF-8）；大文件可用 `offset` / `limit`（字节） |
| `search` | `read:obsidian` | 纯文本子串搜索，可加 glob 过滤 |
| `get_metadata` | `read:obsidian` | 大小、修改时间、有没有 front matter |
| `write_file` | `write:obsidian` | 新建或覆盖文件 |
| `append_file` | `write:obsidian` | 往文件末尾追加 |

能读的地方就能写，限制只有黑名单和路径安全：不许 `..`、不许绝对路径、拒绝符号链接。`.obsidian`、`.trash`、`.git` 始终隐藏。想让某个文件夹（比如放密码的）彻底碰不到，加进 `Vault__Blacklist__N`。

## 配置

用环境变量，`__` 表示层级。

| 变量 | 默认值 | |
|---|---|---|
| `Vault__Root` | `/vault` | 容器内的笔记库目录 |
| `Vault__Blacklist__N` | – | 额外要隐藏的路径段，读写都禁 |
| `Jwt__Algorithm` | `HS256` | `RS256`（从 issuer 的 JWKS 取公钥）或 `HS256`（共享密钥） |
| `Jwt__Issuer` | – | 期望的 `iss`，必填。RS256 模式从 `<Issuer>/.well-known/openid-configuration` 拉公钥。 |
| `Jwt__Audience` | `obsidian` | 期望的 `aud` |
| `Jwt__ValidTypes__N` | – | 仅 RS256：允许的 `typ` 头。对接 nas-auth 设 `at+jwt`，因为它的 id_token 用的是同一把钥。留空不检查，兼容 token 里写 `typ: JWT` 的服务。 |
| `Jwt__SigningKey__Current` / `__Previous` | – | 仅 HS256：和授权服务共享的密钥，以及轮换期间的上一把 |
| `Mcp__OAuthDiscovery__Issuer` | – | 必填，写进本服务的 OAuth 元数据 |
| `Mcp__OAuthDiscovery__AuthorizationEndpoint` | – | 必填 |
| `Mcp__OAuthDiscovery__TokenEndpoint` | – | 必填 |
| `Mcp__OAuthDiscovery__RegistrationEndpoint` | – | 授权服务的 `/register`，支持动态注册时填 |
| `Mcp__OAuthDiscovery__ResourceUrl` | 请求的 host | RFC 9728 的 `resource`，要和授权服务那边一致 |
| `AuditLog__Directory` | `/app/logs` | 审计日志，一天一个文件 |

对接 nas-auth 就是这些：

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

再在 nas-auth 的 `resources.json` 里加一个 `obsidian` 条目，`resource_url` 填同一个地址。

## 用哪个授权服务

Claude 这类远程连接器客户端只走完整的 OAuth 流程：从本服务的元数据发现授权服务，授权码 + PKCE，没有「直接贴一个 token」的选项。所以授权服务得支持 PKCE、动态客户端注册（客户端要自己注册）、`resource` 参数（RFC 8707）和自定义 scope（`read:obsidian`、`write:obsidian`）。

- [nas-auth](https://github.com/ZhengchenTao/nas-auth)：本服务就是照着它写的。小，自建，按上面的 RS256 配置即可。
- 托管服务：[Logto](https://logto.io)、[ZITADEL](https://zitadel.com)、[Auth0](https://auth0.com)。用 RS256 模式，填租户的 issuer 地址。
- 自建、功能更全：[Keycloak](https://www.keycloak.org)、[Authentik](https://goauthentik.io)，或者自己部署 ZITADEL / Logto。
- HS256 模式留给你自己写的极简授权服务，两边要用同一把密钥。

## 运行

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

要用 `write_file` / `append_file`，容器用户得对笔记库有写权限；不需要写就挂成只读（`:ro`）。

本地开发用 HS256、自己签一个 token 最省事：

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
# 选 Streamable HTTP，地址 http://localhost:5000/mcp，把 token 填进 Bearer
```

测试：`dotnet test obsidian-mcp.Tests`。

## CI

`.gitea/workflows/build-image.yml` 在每次推送到 `main` 时构建并推送 `<REGISTRY>/<IMAGE_OWNER>/obsidian-mcp`，然后可以经 SSH 触发重新部署。用到 `vars.REGISTRY`、`vars.IMAGE_OWNER`、`secrets.AIFACELY_REGISTRY_TOKEN`，部署步骤另外用 `vars.DEPLOY_SERVICE`、`secrets.NAS_CI_SSH_KEY`、`secrets.NAS_SSH_HOST`、`secrets.NAS_SSH_KNOWN_HOSTS`。里面的 action 地址和构建代理是按我自己的 CI 写的，fork 后改成你的。

## 许可

MIT
