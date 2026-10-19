FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HesabYar.Web/HesabYar.Web.csproj HesabYar.Web/
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet restore HesabYar.Web/HesabYar.Web.csproj
COPY HesabYar.Web/ HesabYar.Web/
RUN --mount=type=cache,id=donysh-nuget,target=/root/.nuget/packages \
    dotnet publish HesabYar.Web/HesabYar.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HesabYar.Web.dll"]
