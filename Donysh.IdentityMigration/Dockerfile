FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Donysh.IdentityMigration/Donysh.IdentityMigration.csproj Donysh.IdentityMigration/
RUN dotnet restore Donysh.IdentityMigration/Donysh.IdentityMigration.csproj
COPY Donysh.IdentityMigration/ Donysh.IdentityMigration/
RUN dotnet publish Donysh.IdentityMigration/Donysh.IdentityMigration.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Donysh.IdentityMigration.dll"]
