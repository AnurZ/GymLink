FROM mcr.microsoft.com/dotnet/sdk:10.0.302-alpine3.23 AS build
WORKDIR /source

COPY global.json ./
COPY backend/Directory.Build.props backend/
COPY backend/src/GymLink.Domain/GymLink.Domain.csproj backend/src/GymLink.Domain/
COPY backend/src/GymLink.Application/GymLink.Application.csproj backend/src/GymLink.Application/
COPY backend/src/GymLink.Contracts/GymLink.Contracts.csproj backend/src/GymLink.Contracts/
COPY backend/src/GymLink.Infrastructure/GymLink.Infrastructure.csproj backend/src/GymLink.Infrastructure/
COPY backend/src/GymLink.Worker/GymLink.Worker.csproj backend/src/GymLink.Worker/
RUN dotnet restore backend/src/GymLink.Worker/GymLink.Worker.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/GymLink.Worker/GymLink.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-alpine3.23 AS runtime
RUN apk upgrade --no-cache && apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Europe/Sarajevo
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GymLink.Worker.dll"]
