FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac

ARG HONUA_CONSOLE_COMMIT_SHA=unknown
ARG HONUA_CONSOLE_REF=unknown

LABEL org.opencontainers.image.revision=$HONUA_CONSOLE_COMMIT_SHA

WORKDIR /app

COPY --chown=app:app artifacts/honua-console-web/ ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    HONUA_CONSOLE_COMMIT_SHA=${HONUA_CONSOLE_COMMIT_SHA} \
    HONUA_CONSOLE_REF=${HONUA_CONSOLE_REF}

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "Honua.Console.Web.dll"]
