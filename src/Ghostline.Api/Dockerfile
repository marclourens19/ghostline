# Stage 1: Build and Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project files first to leverage Docker layer caching for NuGet restore
COPY ["src/Ghostline.Simulation/Ghostline.Simulation.csproj", "src/Ghostline.Simulation/"]
COPY ["src/Ghostline.Api/Ghostline.Api.csproj", "src/Ghostline.Api/"]
RUN dotnet restore "src/Ghostline.Api/Ghostline.Api.csproj"

# Copy the remaining source code for the simulation and API projects
COPY ["src/Ghostline.Simulation/", "src/Ghostline.Simulation/"]
COPY ["src/Ghostline.Api/", "src/Ghostline.Api/"]

# Build and publish the API
WORKDIR "/src/src/Ghostline.Api"
RUN dotnet publish "Ghostline.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ghostline.Api.dll"]
