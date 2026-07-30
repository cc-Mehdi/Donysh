FROM node:22-alpine AS assets
WORKDIR /src
COPY HesabYar.Web/package.json ./
RUN npm install --no-audit --no-fund
COPY HesabYar.Web/Pages ./Pages
COPY HesabYar.Web/Styles ./Styles
RUN mkdir -p wwwroot/css && npm run build:css

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HesabYar.Web/HesabYar.Web.csproj HesabYar.Web/
RUN dotnet restore HesabYar.Web/HesabYar.Web.csproj
COPY HesabYar.Web/ HesabYar.Web/
COPY --from=assets /src/wwwroot/css/app.css HesabYar.Web/wwwroot/css/app.css
RUN dotnet publish HesabYar.Web/HesabYar.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HesabYar.Web.dll"]
