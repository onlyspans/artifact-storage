FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Onlyspans.Artifact-Storage.slnx ./
COPY src/Onlyspans.Artifact-Storage.Api/Onlyspans.Artifact-Storage.Api.csproj src/Onlyspans.Artifact-Storage.Api/

RUN dotnet restore src/Onlyspans.Artifact-Storage.Api/Onlyspans.Artifact-Storage.Api.csproj

COPY src/Onlyspans.Artifact-Storage.Api/ src/Onlyspans.Artifact-Storage.Api/

WORKDIR /src/src/Onlyspans.Artifact-Storage.Api
RUN dotnet build -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "Onlyspans.Artifact-Storage.Api.dll"]
