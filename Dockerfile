# Use the official .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy the project files and restore dependencies
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

# Use the ASP.NET runtime image to run the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Run the application
ENTRYPOINT ["dotnet", "RedisLockApp.dll"]

