FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled-extra

WORKDIR /app
COPY ./dev-dist/Container/ .

ENTRYPOINT ["./Burndown"]
