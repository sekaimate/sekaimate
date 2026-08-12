FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
WORKDIR /src
COPY BasisSsoBroker.csproj ./
RUN dotnet restore
COPY *.cs ./
RUN dotnet publish -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble
WORKDIR /app
COPY --from=build /app/publish ./
COPY docker-entrypoint.sh /usr/local/bin/basis-sso-broker
RUN chmod 755 /usr/local/bin/basis-sso-broker
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["/usr/local/bin/basis-sso-broker"]
