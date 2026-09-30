# Build context is the backend/ directory (see backend/docker-compose.yml):
#   docker build -f WildCat.Host/Dockerfile -t wildcat .
# The image runs the silo with the co-hosted WebSocket gateway on port 5000 (/ws).

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
# The .NET 8+ aspnet images listen on 8080 by default; HTTP_PORTS moves that to 5000
# (setting ASPNETCORE_URLS instead works too but logs an override warning at startup).
ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["WildCat.Host/WildCat.Host.csproj", "WildCat.Host/"]
COPY ["WildCat.Grains/WildCat.Grains.csproj", "WildCat.Grains/"]
COPY ["WildCat.Interfaces/WildCat.Interfaces.csproj", "WildCat.Interfaces/"]
COPY ["WildCat.Protocol/WildCat.Protocol.csproj", "WildCat.Protocol/"]
COPY ["WildCat.Gateway/WildCat.Gateway.csproj", "WildCat.Gateway/"]
RUN dotnet restore "WildCat.Host/WildCat.Host.csproj"
COPY . .
WORKDIR /src/WildCat.Host
RUN dotnet publish "WildCat.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "WildCat.Host.dll"]
