# Stage 1: Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore NuGet packages
COPY ["SmartBank.csproj", "./"]
RUN dotnet restore "SmartBank.csproj"

# Copy source code and build production release
COPY . .
RUN dotnet publish "SmartBank.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Production runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# ASP.NET Core environment and listening ports
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartBank.dll"]
