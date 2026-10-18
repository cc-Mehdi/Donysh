FROM node:22-bookworm-slim AS assets
WORKDIR /src
COPY Donysh.Auth/package.json Donysh.Auth/package-lock.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund
COPY Donysh.Auth/Pages ./Pages
COPY Donysh.Auth/Styles ./Styles
RUN mkdir -p wwwroot/css && npm run build:css

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Donysh.Auth/Donysh.Auth.csproj Donysh.Auth/
COPY HesabYar.Web/HesabYar.Web.csproj HesabYar.Web/
RUN dotnet restore Donysh.Auth/Donysh.Auth.csproj
COPY Donysh.Auth/ Donysh.Auth/
COPY HesabYar.Web/wwwroot/images/logo-mark.svg HesabYar.Web/wwwroot/images/logo-mark.svg
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Bold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Bold.woff2
COPY --from=assets /src/wwwroot/css/app.css Donysh.Auth/wwwroot/css/app.css
RUN dotnet publish Donysh.Auth/Donysh.Auth.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
RUN mkdir -p /keys && chown -R $APP_UID:$APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Donysh.Auth.dll"]
