
# BUILD
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["EMua/EMua.csproj", "EMua/"]

RUN dotnet restore "EMua/EMua.csproj"

COPY EMua/ EMua/

RUN dotnet publish "EMua/EMua.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# RUNTIME
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "EMua.dll"]
