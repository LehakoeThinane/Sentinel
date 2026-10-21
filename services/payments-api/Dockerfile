# Build context: repository root (shared build settings live there).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
# Restore first so the package layer is cached until the project file changes.
COPY services/payments-api/src/Payments.Api/Payments.Api.csproj services/payments-api/src/Payments.Api/
RUN dotnet restore services/payments-api/src/Payments.Api/Payments.Api.csproj
COPY services/payments-api/src/ services/payments-api/src/
RUN dotnet publish services/payments-api/src/Payments.Api/Payments.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Non-root user provided by the .NET base image.
USER $APP_UID
ENTRYPOINT ["dotnet", "Payments.Api.dll"]
