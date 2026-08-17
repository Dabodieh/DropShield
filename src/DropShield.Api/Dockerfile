FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/DropShield.Api/DropShield.Api.csproj", "src/DropShield.Api/"]
RUN dotnet restore "src/DropShield.Api/DropShield.Api.csproj"
COPY . .
WORKDIR "/src/src/DropShield.Api"
RUN dotnet publish "DropShield.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DropShield.Api.dll"]

