# syntax=docker/dockerfile:1.6
# ─── Stage 1: build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# 先只复制 csproj，利用层缓存加速 restore
COPY obsidian-mcp.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore obsidian-mcp.csproj

# 复制全部源码并 publish
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish obsidian-mcp.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ─── Stage 2: runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# 安装 ripgrep（V3 搜索优化预留，现在不调用但容器里装上备用）
RUN apt-get update \
    && apt-get install -y --no-install-recommends ripgrep \
    && rm -rf /var/lib/apt/lists/*

# 从 builder 阶段复制 publish 产物
COPY --from=builder /app/publish .

# 日志目录（审计日志挂载点）
RUN mkdir -p /app/logs

# 非 root 运行，安全加固
RUN useradd --system --no-create-home --shell /usr/sbin/nologin obsidian-mcp \
    && chown -R obsidian-mcp:obsidian-mcp /app

USER obsidian-mcp

# 容器内监听 0.0.0.0:8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# OCI 标签（source 和 revision 在 CI 构建时通过 --label 注入）
LABEL org.opencontainers.image.title="obsidian-mcp"
LABEL org.opencontainers.image.description="Obsidian vault MCP server — read/write vault via MCP, auth via OAuth-issued JWT"
LABEL org.opencontainers.image.licenses="MIT"

ENTRYPOINT ["dotnet", "obsidian-mcp.dll"]
