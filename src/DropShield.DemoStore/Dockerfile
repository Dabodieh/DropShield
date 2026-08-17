FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/DropShield.DemoStore/DropShield.DemoStore.csproj", "src/DropShield.DemoStore/"]
RUN dotnet restore "src/DropShield.DemoStore/DropShield.DemoStore.csproj"
COPY . .
WORKDIR "/src/src/DropShield.DemoStore"
RUN dotnet publish "DropShield.DemoStore.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DropShield.DemoStore.dll"]

