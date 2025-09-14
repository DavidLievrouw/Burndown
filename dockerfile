FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled-extra

WORKDIR /app
COPY ./dev-dist/Container/ .

ENTRYPOINT ["./Burndown"]
