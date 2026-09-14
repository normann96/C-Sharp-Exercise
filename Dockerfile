FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore on the project files alone, so editing code does not re-resolve every package. The root files
# carry the shared properties and every package version, so a build without them resolves differently.
COPY Directory.Build.props Directory.Packages.props ./
COPY CSharpApp.Api/CSharpApp.Api.csproj CSharpApp.Api/
COPY CSharpApp.Application/CSharpApp.Application.csproj CSharpApp.Application/
COPY CSharpApp.Core/CSharpApp.Core.csproj CSharpApp.Core/
COPY CSharpApp.Infrastructure/CSharpApp.Infrastructure.csproj CSharpApp.Infrastructure/
RUN dotnet restore CSharpApp.Api/CSharpApp.Api.csproj

COPY CSharpApp.Api/ CSharpApp.Api/
COPY CSharpApp.Application/ CSharpApp.Application/
COPY CSharpApp.Core/ CSharpApp.Core/
COPY CSharpApp.Infrastructure/ CSharpApp.Infrastructure/
RUN dotnet publish CSharpApp.Api/CSharpApp.Api.csproj --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# The kernel applies no default action to PID 1, so as PID 1 the application would survive its own abort on an
# unhandled startup exception: spinning on a core, still reported running, never restarted. Kubernetes has no
# --init, so this belongs in the image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Sinks are declared per environment and an unnamed one refuses to start, so the image names its own.
ENV ASPNETCORE_ENVIRONMENT=Production

# Resolves to the base image's numeric uid, which is what Kubernetes needs to honour runAsNonRoot: it cannot
# verify a name is not root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "CSharpApp.Api.dll"]
