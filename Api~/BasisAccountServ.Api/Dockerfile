FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BasisAccountServ.Api.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish BasisAccountServ.Api.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BasisAccountServ.Api.dll"]
