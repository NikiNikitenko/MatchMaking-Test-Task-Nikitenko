# Use ASP.NET runtime for the final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Use .NET SDK for build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore
COPY MatchMaking.Worker/MatchMaking.Worker.csproj MatchMaking.Worker/
RUN dotnet restore MatchMaking.Worker/MatchMaking.Worker.csproj

# Copy the rest of the source code
COPY . .
WORKDIR /src/MatchMaking.Worker
RUN dotnet build -c Release -o /app/build

# Publish the app
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MatchMaking.Worker.dll"]
