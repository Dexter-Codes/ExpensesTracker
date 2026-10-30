FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Expenses.Web/Expenses.Web.csproj src/Expenses.Web/
RUN dotnet restore src/Expenses.Web/Expenses.Web.csproj
COPY src/Expenses.Web/ src/Expenses.Web/
RUN dotnet publish src/Expenses.Web/Expenses.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Expenses.Web.dll"]
