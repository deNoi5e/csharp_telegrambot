#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Homework_09/Homework_09.csproj", "Homework_09/"]
RUN dotnet restore "Homework_09/Homework_09.csproj"
COPY . .
WORKDIR "/src/Homework_09"
RUN dotnet build "Homework_09.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Homework_09.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Homework_09.dll"]