# Build from the repository root: docker build -f apps/api/Dockerfile -t northwind-support-api:dev .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore depends on project/dependency metadata, not application source.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["apps/api/src/SupportDesk.Domain/SupportDesk.Domain.csproj", "apps/api/src/SupportDesk.Domain/"]
COPY ["apps/api/src/SupportDesk.Application/SupportDesk.Application.csproj", "apps/api/src/SupportDesk.Application/"]
COPY ["apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj", "apps/api/src/SupportDesk.Infrastructure/"]
COPY ["apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj", "apps/api/src/SupportDesk.Presentation/"]
RUN dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj

COPY apps/api/src/ apps/api/src/
RUN dotnet publish apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "SupportDesk.Presentation.dll"]
