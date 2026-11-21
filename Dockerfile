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

# Tạo sẵn các thư mục upload và phân quyền ghi đầy đủ cho user ứng dụng trước khi chuyển sang USER $APP_UID
RUN mkdir -p /app/wwwroot/uploads/returns \
             /app/wwwroot/uploads/delivery-proof \
             /app/wwwroot/uploads/brands \
             /app/wwwroot/uploads/products \
             /app/wwwroot/images/returns \
             /app/wwwroot/images/products \
    && chown -R $APP_UID:$APP_UID /app/wwwroot/uploads /app/wwwroot/images \
    && chmod -R 777 /app/wwwroot/uploads /app/wwwroot/images

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "EMua.dll"]
