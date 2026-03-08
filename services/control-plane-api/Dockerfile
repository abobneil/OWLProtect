FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY services services
RUN dotnet publish services/control-plane-api/OWLProtect.ControlPlane.Api.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 5180
ENTRYPOINT ["dotnet", "OWLProtect.ControlPlane.Api.dll"]

