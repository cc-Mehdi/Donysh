FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Donysh.Life/Donysh.Life.csproj Donysh.Life/
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet restore Donysh.Life/Donysh.Life.csproj
COPY Donysh.Life/ Donysh.Life/
COPY HesabYar.Web/wwwroot/images/donysh-mark.png HesabYar.Web/wwwroot/images/donysh-mark.png
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-SemiBold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-SemiBold.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-ExtraBold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-ExtraBold.woff2
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet publish Donysh.Life/Donysh.Life.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
RUN mkdir -p /keys && chown -R $APP_UID:$APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Donysh.Life.dll"]
