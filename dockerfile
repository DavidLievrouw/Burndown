FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

WORKDIR /app

COPY ./src/Burndown/ ./
RUN dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:PublishReadyToRun=true -o out

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled-extra

WORKDIR /app
COPY --from=build-env /app/out .

ENV ASPNETCORE_URLS=http://+:9042
EXPOSE 9042

ENTRYPOINT ["./Burndown"]
