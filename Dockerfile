# syntax=docker/dockerfile:1
# csv2zugferd as an MCP server (stdio) in a Linux container.
#
#   docker build -t csv2zugferd .
#   docker run -i --rm -v "<folder>:/data" csv2zugferd
#
# The container runs "csv2zugferd mcp" and speaks JSON-RPC over stdin/stdout.
# The tools only see files below the mounted /data folder (the working directory).
# Any other CLI call works as well: docker run --rm -v "<folder>:/data" csv2zugferd --help

ARG APP_VERSION=1.3.1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between source changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY csv2zugferd/csv2zugferd.csproj csv2zugferd/
RUN dotnet restore csv2zugferd/csv2zugferd.csproj -r linux-x64

COPY . .
# Framework-dependent Linux build. The Release defaults of the project target a
# win-x64 single-file executable; the explicit RID and flags override them here.
RUN dotnet publish csv2zugferd/csv2zugferd.csproj -c Release -r linux-x64 \
    --self-contained false -p:PublishSingleFile=false -p:UseAppHost=false \
    --no-restore -o /app

# Chiseled runtime: no shell, no package manager, non-root user "app"; "-extra"
# carries ICU and tzdata, which the German number and date formats need.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS runtime
ARG APP_VERSION
LABEL org.opencontainers.image.title="csv2zugferd" \
      org.opencontainers.image.description="CSV to ZUGFeRD/Factur-X (EN 16931) converter with an MCP server (stdio) that helps AI assistants draft and check the YAML mapping" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.source="https://github.com/rillsoft/csv2zugferd" \
      org.opencontainers.image.url="https://zugferd-tools.de/docs/mcp-server/" \
      org.opencontainers.image.licenses="Apache-2.0" \
      io.modelcontextprotocol.server.name="de.zugferd-tools/csv2zugferd"

COPY --from=build /app /app

# Log files stay out of the mounted user folder.
ENV CSV2ZUGFERD_LOG_DIR=/tmp/csv2zugferd/logs

# Working directory for user files; mount it with -v "<folder>:/data".
WORKDIR /data
USER $APP_UID

ENTRYPOINT ["dotnet", "/app/csv2zugferd.dll"]
CMD ["mcp"]
