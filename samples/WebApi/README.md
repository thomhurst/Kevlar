# ASP.NET Core, DI, HTTP, and metrics

This ASP.NET Core sample registers a named shield, protects an `HttpClientFactory` client with
`AddStandardShield`, and exports the Kevlar meter through OpenTelemetry. Its `/orders` minimal API
endpoint calls an in-process flaky handler that fails twice before recovering.

`AddKevlarValidationOnStart()` constructs explicitly registered named shields before the host starts
serving requests. An invalid named shield fails startup with its name and original configuration or
factory error. Validation does not execute a protected operation or instantiate `HttpClient` pipelines.

Run `dotnet run --project samples/WebApi -f net10.0`, then request `/orders` from the address printed
by ASP.NET Core. For a headless verification, add `-- --smoke`; that path performs the same resilient
downstream call, asserts three attempts and two retry measurements, prints the result, and exits.
Smoke mode starts and stops the host on an ephemeral loopback port to exercise startup validation.
The sample intentionally keeps transport local so it needs no external service.
