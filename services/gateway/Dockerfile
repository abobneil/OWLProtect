FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY services services
RUN dotnet publish services/gateway/OWLProtect.Gateway.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "OWLProtect.Gateway.dll"]

