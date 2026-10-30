FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source
COPY global.json NuGet.Config ./
COPY src/Expenses.Web/Expenses.Web.csproj src/Expenses.Web/
RUN dotnet restore src/Expenses.Web/Expenses.Web.csproj -a $TARGETARCH
COPY src/Expenses.Web/ src/Expenses.Web/
RUN dotnet publish src/Expenses.Web/Expenses.Web.csproj -c Release --no-restore \
    -a $TARGETARCH --self-contained false -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__DefaultConnection="Data Source=/data/expenses.db"
ENV DataProtection__KeyPath=/data/keys
RUN mkdir /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Expenses.Web.dll"]
