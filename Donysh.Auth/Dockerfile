FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Donysh.Auth/Donysh.Auth.csproj Donysh.Auth/
COPY HesabYar.Web/HesabYar.Web.csproj HesabYar.Web/
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet restore Donysh.Auth/Donysh.Auth.csproj
COPY Donysh.Auth/ Donysh.Auth/
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Bold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Bold.woff2
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet publish Donysh.Auth/Donysh.Auth.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
RUN mkdir -p /keys && chown -R $APP_UID:$APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Donysh.Auth.dll"]
