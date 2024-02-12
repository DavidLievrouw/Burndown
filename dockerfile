FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app

COPY ./src/Burndown/ ./
RUN dotnet publish -c Release -r linux-x64 -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://+:9042
EXPOSE 9042

ENTRYPOINT ["./Burndown"]
