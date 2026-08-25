# ASP.NET Core, DI, HTTP, and metrics

This headless ASP.NET Core sample registers a named shield, protects an `HttpClientFactory` client with `AddStandardShield`, registers the Kevlar meter with OpenTelemetry, and verifies two retries against an in-process flaky handler. Run `dotnet run --project samples/WebApi -f net10.0 -- --smoke`.
