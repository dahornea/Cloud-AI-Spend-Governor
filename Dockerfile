FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config ./
COPY CloudAiSpendGovernor.slnx ./
COPY src/SpendGovernor.Core/SpendGovernor.Core.csproj src/SpendGovernor.Core/
COPY src/SpendGovernor.Infrastructure/SpendGovernor.Infrastructure.csproj src/SpendGovernor.Infrastructure/
COPY src/SpendGovernor.Api/SpendGovernor.Api.csproj src/SpendGovernor.Api/

RUN dotnet restore CloudAiSpendGovernor.slnx --configfile NuGet.Config

COPY . .
RUN dotnet publish src/SpendGovernor.Api/SpendGovernor.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "SpendGovernor.Api.dll"]
