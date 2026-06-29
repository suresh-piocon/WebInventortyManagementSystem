# Use the .NET 9 SDK image to build and compile the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project files and restore dependencies
COPY ["src/InventoryManagement.Api/InventoryManagement.Api.csproj", "src/InventoryManagement.Api/"]
COPY ["src/InventoryManagement.Shared/InventoryManagement.Shared.csproj", "src/InventoryManagement.Shared/"]
RUN dotnet restore "src/InventoryManagement.Api/InventoryManagement.Api.csproj"

# Copy the entire source code and publish it
COPY . .
WORKDIR "/app/src/InventoryManagement.Api"
RUN dotnet publish "InventoryManagement.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the ASP.NET Core runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Set environment variables for Render / Railway port mapping
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "InventoryManagement.Api.dll"]
