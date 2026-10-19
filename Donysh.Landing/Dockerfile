FROM node:22-bookworm-slim AS assets
WORKDIR /src
COPY Donysh.Landing/package.json Donysh.Landing/package-lock.json ./
RUN npm ci --ignore-scripts --no-audit --no-fund
COPY Donysh.Landing/Pages ./Pages
COPY Donysh.Landing/Styles ./Styles
RUN mkdir -p wwwroot/css && npm run build:css

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Donysh.Landing/Donysh.Landing.csproj Donysh.Landing/
RUN dotnet restore Donysh.Landing/Donysh.Landing.csproj
COPY Donysh.Landing/ Donysh.Landing/
COPY HesabYar.Web/wwwroot/images/donysh-wordmark.png HesabYar.Web/wwwroot/images/donysh-wordmark.png
COPY HesabYar.Web/wwwroot/images/donysh-mark.png HesabYar.Web/wwwroot/images/donysh-mark.png
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-Regular.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-SemiBold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-SemiBold.woff2
COPY HesabYar.Web/wwwroot/fonts/YekanBakh-ExtraBold.woff2 HesabYar.Web/wwwroot/fonts/YekanBakh-ExtraBold.woff2
COPY --from=assets /src/wwwroot/css/app.css Donysh.Landing/wwwroot/css/app.css
RUN dotnet publish Donysh.Landing/Donysh.Landing.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Donysh.Landing.dll"]
