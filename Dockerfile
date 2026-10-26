FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG NUGET_SOURCE=https://api.nuget.org/v3/index.json
WORKDIR /source
COPY src/EntraRolesDemo/EntraRolesDemo.csproj src/EntraRolesDemo/
RUN dotnet restore src/EntraRolesDemo/EntraRolesDemo.csproj --source "$NUGET_SOURCE"
COPY src/EntraRolesDemo/ src/EntraRolesDemo/
RUN dotnet publish src/EntraRolesDemo/EntraRolesDemo.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/home/data/entra-roles-keys
RUN mkdir -p /home/data/entra-roles-keys && chown -R app:app /home/data
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "EntraRolesDemo.dll"]
