FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/ .
RUN dotnet restore TuracoChorus/TuracoChorus.csproj
RUN dotnet publish TuracoChorus/TuracoChorus.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TuracoChorus.dll"]
