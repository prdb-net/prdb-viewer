# Use .NET 10 and a static React frontend

The backend is .NET 10 and the frontend is React with TypeScript built to
static assets served by the backend. This adopts the proven toolchain of
`prdb-fab`: `Prdb.Sdk` and `Prdb.Hashing` remain native C# dependencies, one
application container remains sufficient, and Node is needed only while
building the frontend.

## Consequences

- The running application image contains the ASP.NET Core runtime, not a second
  frontend server or Node runtime.
- `dotnet build` and `dotnet test` are the backend verification commands, with
  the SDK version pinned in `global.json`.
