# ASP.NET Core, DI, HTTP, and metrics

This ASP.NET Core sample registers a named shield, protects an `HttpClientFactory` client with
`AddStandardShield`, and exports the Kevlar meter through OpenTelemetry. Its `/orders` minimal API
endpoint calls an in-process flaky handler that fails twice before recovering.

Run `dotnet run --project samples/WebApi -f net10.0`, then request `/orders` from the address printed
by ASP.NET Core. For a headless verification, add `-- --smoke`; that path performs the same resilient
downstream call, asserts three attempts and two retry measurements, prints the result, and exits.
The sample intentionally keeps transport local so it needs no external service.
