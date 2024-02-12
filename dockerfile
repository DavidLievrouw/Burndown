FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app

COPY ./src/Burndown/ ./
RUN dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true /p:TrimMode=partial /p:PublishReadyToRun=true /p:EnableCompressionInSingleFile=true -o out

FROM ubuntu/dotnet-deps:8.0-24.04_103

WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://+:9042
EXPOSE 9042

ENTRYPOINT ["./Burndown"]
