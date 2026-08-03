FROM mcr.microsoft.com/dotnet/aspnet:10.0

ARG HONUA_CONSOLE_COMMIT_SHA=unknown
ARG HONUA_CONSOLE_REF=unknown

WORKDIR /app

COPY --chown=app:app artifacts/honua-console-web/ ./

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    HONUA_CONSOLE_COMMIT_SHA=${HONUA_CONSOLE_COMMIT_SHA} \
    HONUA_CONSOLE_REF=${HONUA_CONSOLE_REF}

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "Honua.Console.Web.dll"]
