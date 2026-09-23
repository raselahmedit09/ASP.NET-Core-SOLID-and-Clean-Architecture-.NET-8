# HR Management System — Clean Architecture, CQRS & API Gateway (.NET 8) — Developer Guide

A microservices-based HR Management System built with **.NET 8**, following **Clean Architecture** with **CQRS**/**MediatR**, sitting behind an **Ocelot API Gateway**, with **NSwag**-generated typed clients for service-to-service calls.

This guide is written for a developer who has just cloned the repo: how the solution is laid out, how to run it end-to-end, how the architecture actually works layer by layer, and — critically — the non-obvious gotchas already baked into this codebase that will otherwise cost you an afternoon.

---

## Table of contents

1. [Solution architecture](#solution-architecture)
2. [Solution / folder map](#solution--folder-map)
3. [Prerequisites](#prerequisites)
4. [Ports & URLs reference](#ports--urls-reference)
5. [First-time setup](#first-time-setup)
6. [Running the system](#running-the-system)
7. [Clean Architecture layers, explained](#clean-architecture-layers-explained)
8. [CQRS with MediatR — the LeaveType reference slice](#cqrs-with-mediatr--the-leavetype-reference-slice)
9. [Validation — how it actually runs (read this before adding a command)](#validation--how-it-actually-runs-read-this-before-adding-a-command)
10. [Repository pattern & EF Core](#repository-pattern--ef-core)
11. [Global exception handling](#global-exception-handling)
12. [Ocelot API Gateway](#ocelot-api-gateway)
13. [NSwag + RestApiClient — service-to-service calls](#nswag--restapiclient--service-to-service-calls)
14. [Swagger / OpenAPI](#swagger--openapi)
15. [What's actually implemented vs. scaffolded](#whats-actually-implemented-vs-scaffolded)
16. [Common developer scenarios](#common-developer-scenarios)
    - [Add a new CQRS command/query to an existing feature](#scenario-a--add-a-new-cqrs-commandquery-to-an-existing-feature)
    - [Add a brand-new vertical slice/feature (end-to-end)](#scenario-b--add-a-brand-new-vertical-slicefeature-end-to-end)
    - [Add a brand-new microservice](#scenario-c--add-a-brand-new-microservice)
    - [Change a connection string / run on a teammate's machine](#scenario-d--change-a-connection-string--run-on-a-teammates-machine)
    - [Add or change a route in the API Gateway](#scenario-e--add-or-change-a-route-in-the-api-gateway)
    - [Regenerate an NSwag client after changing an API's contract](#scenario-f--regenerate-an-nswag-client-after-changing-an-apis-contract)
17. [Testing](#testing)
18. [Troubleshooting](#troubleshooting)
19. [Useful links](#useful-links)

---

## Solution architecture

```
                         ┌───────────────────────────┐
                         │   OcelotApiGateway          │  https://localhost:7090
                         │   (single catch-all route)   │
                         └──────────────┬────────────┘
                                        │ round-robins "/{everything}" across:
                       ┌────────────────┴────────────────┐
                       ▼                                  ▼
        ┌───────────────────────────┐      ┌───────────────────────────┐
        │  HR.LeaveManagement.Api     │      │  HR.Employee.Api             │
        │  https://localhost:7091      │      │  https://localhost:7093      │
        │  http://localhost:7092        │      │  http://localhost:7094        │
        │                               │◄────►│                               │
        │  calls Employee API via       │      │  calls LeaveManagement API    │
        │  EmployeeModuleAPI (RestApiClient)     via LeaveManagementModuleAPI  │
        └───────────────┬───────────────┘      └───────────────┬───────────────┘
                        │ Clean Architecture layers:            │ (thin — no Application layer yet)
                        │  API → Application → Domain            │
                        │       ↘ Infrastructure / Persistence    │
                        ▼                                        │
              ┌────────────────────┐                              │
              │  SQL Server          │                              │
              │  db_hr_leavemanagement │                            │
              └────────────────────┘                              │
                                                                    ▼
                                                    both share ──► RestApiClient
                                                                (NSwag-generated
                                                                 typed HTTP clients)
```

- **OcelotApiGateway** is the single public entry point in front of both APIs.
- **HR.LeaveManagement.Api** is the fully-built-out service: Domain → Application (CQRS/MediatR/FluentValidation/AutoMapper) → Infrastructure/Persistence (EF Core + repositories) → API.
- **HR.Employee.Api** is intentionally thin — currently just a controller layer with no Domain/Application/Infrastructure projects of its own (see [What's actually implemented vs. scaffolded](#whats-actually-implemented-vs-scaffolded)).
- **RestApiClient** is a shared project containing NSwag-generated typed HTTP clients (`LeaveManagementModuleAPI`, `EmployeeModuleAPI`) so each API can call the other without hand-writing `HttpClient` code.
- Both APIs reference `RestApiClient` **and** are the source of the OpenAPI spec that regenerates part of `RestApiClient` on every Debug build — see the [NSwag section](#nswag--restapiclient--service-to-service-calls) for why that matters.

---

## Solution / folder map

```
ASP.NET-Core-SOLID-and-Clean-Architecture-.NET-8/
├── HR.Management.Clean.sln
├── ApiGateway/
│   └── OcelotApiGateway/            # Ocelot gateway — ocelot.json, Program.cs
├── RestApiClient/
│   └── RestApiClient/               # NSwag-generated clients + DI registration (shared by both APIs)
├── ServiceApplications/
│   ├── LeaveManagement/
│   │   ├── Core/
│   │   │   ├── HR.LeaveManagement.Domain/        # Entities (LeaveType, LeaveAllocation, LeaveRequest), BaseEntity
│   │   │   └── HR.LeaveManagement.Application/   # CQRS Features, validators, DTOs, contracts (interfaces)
│   │   ├── Infrastructure/
│   │   │   ├── HR.LeaveManagement.Persistence/   # DbContext, EF configurations, migrations, repositories
│   │   │   └── HR.LeaveManagement.Infrastructure/# Email sender, logger adapter
│   │   └── API/
│   │       └── HR.LeaveManagement.Api/           # Controllers, middleware, Program.cs, nswag.json
│   └── Employee/
│       └── API/
│           └── HR.Employee.Api/                  # Controllers, Program.cs, nswag.json (no Domain/Application/Infra yet)
└── Test/
    ├── HR.LeaveManagement.Application.UnitTests/       # xUnit + Moq + Shouldly
    └── HR.LeaveManagement.Persistence.IntegrationTests/ # xUnit + EF Core InMemory
```

The `.sln` groups these into solution folders named `API`, `Core`, `Infrastructure`, `UI` (currently empty — there is no frontend project in this solution; the Angular microfrontend lives in a sibling repo), and `Test` — open the `.sln` in Visual Studio/Rider to navigate by that grouping rather than the physical folder tree above.

---

## Prerequisites

- **.NET 8 SDK 8.0.200** or compatible — pinned in [`global.json`](global.json). If `dotnet --version` reports an older/newer major, install the matching SDK or the build will fail to resolve.
- **SQL Server** (LocalDB, Express, or full) reachable with Windows/Trusted authentication, or adjust the connection string to use SQL auth.
- A REST client for exploring the APIs (Swagger UI is built in — see below — or use the provided `.http` files in each API project with the VS Code REST Client extension / Visual Studio's built-in HTTP editor).
- No Node/npm or frontend tooling is required for this repository — the `UI` solution folder is a placeholder.

---

## Ports & URLs reference

| Project | HTTPS | HTTP | Swagger UI |
|---|---|---|---|
| OcelotApiGateway | `https://localhost:7090` | — | n/a (gateway only) |
| HR.LeaveManagement.Api | `https://localhost:7091` | `http://localhost:7092` | `/swagger` |
| HR.Employee.Api | `https://localhost:7093` | `http://localhost:7094` | `/swagger` |

These are fixed in each project's `Properties/launchSettings.json` and cross-referenced in `ocelot.json`, `HostDocumentFilter.cs`, and `RestApiClient/ServiceRegistrationExtensions.cs` — if you change a port, you must update it in **all** of those places (see [Troubleshooting](#troubleshooting)).

---

## First-time setup

```sh
git clone https://github.com/raselahmedit09/ASP.NET-Core-SOLID-and-Clean-Architecture-.NET-8.git
cd ASP.NET-Core-SOLID-and-Clean-Architecture-.NET-8
dotnet restore
```

### Database

`HR.LeaveManagement.Api/appsettings.json` has:

```json
"ConnectionStrings": {
  "HrDatabaseConnectionString": "Server=SWD-RASEL-L;Database=db_hr_leavemanagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"
}
```

`Server=SWD-RASEL-L` is the original author's machine name — **you must change this** to your own SQL Server instance (e.g. `.`, `localhost`, `(localdb)\mssqllocaldb`, or a named instance) before the API will start.

There's an EF Core migration already checked in (`HR.LeaveManagement.Persistence/Migrations/20240501025419_InitialMigration.cs`), including seed data for a "Vacation" leave type (`LeaveTypeConfiguration.HasData`). **Prefer applying that migration over the raw SQL scripts** that used to be pasted in this README:

```sh
dotnet tool install --global dotnet-ef   # once, if you don't have it
cd ServiceApplications/LeaveManagement/API/HR.LeaveManagement.Api
dotnet ef database update --project ../../Infrastructure/HR.LeaveManagement.Persistence/HR.LeaveManagement.Persistence.csproj
```

This creates `db_hr_leavemanagement` with `LeaveTypes`, `LeaveAllocations`, and `LeaveRequests` tables (matching what the old manual SQL scripts did, but versioned and repeatable).

---

## Running the system

Each project runs independently with `dotnet run` (or F5 per-project in Visual Studio/Rider). Start them in this order so cross-service calls succeed:

```sh
# Terminal 1
cd ServiceApplications/LeaveManagement/API/HR.LeaveManagement.Api
dotnet run

# Terminal 2
cd ServiceApplications/Employee/API/HR.Employee.Api
dotnet run

# Terminal 3
cd ApiGateway/OcelotApiGateway
dotnet run
```

Then browse:
- `https://localhost:7091/swagger` — LeaveManagement API
- `https://localhost:7093/swagger` — Employee API
- `https://localhost:7090/` — Gateway root (`Hello World!` — it has no UI of its own, only routes)

> On first run, .NET will prompt you to trust the local HTTPS dev certificate if you haven't already (`dotnet dev-certs https --trust`). All three apps use HTTPS, so do this once up front.

---

## Clean Architecture layers, explained

Using **HR.LeaveManagement** as the reference (it's the only service with all four layers):

| Layer | Project | Responsibility | Depends on |
|---|---|---|---|
| Domain | `HR.LeaveManagement.Domain` | Entities (`LeaveType`, `LeaveAllocation`, `LeaveRequest`), `BaseEntity` (Id, DateCreated, DateModified) | nothing |
| Application | `HR.LeaveManagement.Application` | CQRS commands/queries + handlers (MediatR), validators (FluentValidation), DTOs (AutoMapper), repository **interfaces** (`Contracts/Persistence`), cross-cutting **interfaces** (`Contracts/Email`, `Contracts/Logging`) | Domain only |
| Infrastructure | `HR.LeaveManagement.Persistence` (EF Core + repositories) and `HR.LeaveManagement.Infrastructure` (email, logging) | Implements the Application layer's interfaces | Application, Domain |
| API | `HR.LeaveManagement.Api` | Controllers, DI composition root (`Program.cs` wires up all four `Add*Services()` extension methods), middleware, Swagger/NSwag | all of the above |

This is the classic Clean Architecture dependency rule: **arrows point inward**, Application never references Infrastructure or API, and Domain references nothing. `Program.cs` is where the layers get wired together:

```csharp
builder.Services.AddApplicationServices();      // MediatR + AutoMapper
builder.Services.AddInfrastructureServices(...); // Email, Logging
builder.Services.AddPersistenceServices(...);    // DbContext + repositories
builder.Services.RestApiClientGatewayServices(); // typed HTTP clients to the other API
```

**HR.Employee.Api** does not (yet) follow this — it's a single ASP.NET Core Web API project with controllers calling `RestApiClient` directly. Treat it as a starting point to grow into the same 4-layer shape, not as a second reference implementation.

---

## CQRS with MediatR — the LeaveType reference slice

Every feature under `Application/Features/<Entity>` is split into **Commands** (writes) and **Queries** (reads), each its own folder with its own MediatR message + handler:

```
Features/LeaveType/
├── Commands/
│   ├── CreateLeaveType/  → CreateLeaveTypeCommand (IRequest<int>), Handler, Validator
│   ├── UpdateLeaveType/  → UpdateLeaveTypeCommand, Handler
│   └── DeleteLeaveType/  → DeleteLeaveTypeCommand, Handler
└── Queries/
    ├── GetAllLeaveTypes/     → GetLeaveTypesQuery (IRequest<List<LeaveTypeDto>>), Handler, LeaveTypeDto
    └── GetLeaveTypeDetails/  → GetLeaveTypeDetailsQuery, Handler, LeaveTypeDetailsDto
```

`LeaveTypesController` just forwards HTTP verbs to `IMediator.Send(...)` — it contains **no business logic**:

```csharp
[HttpPost]
public async Task<ActionResult> Post(CreateLeaveTypeCommand leaveType)
{
    var response = await _mediator.Send(leaveType);
    return CreatedAtAction(nameof(Get), new { id = response });
}
```

`MediatR` and `AutoMapper` are registered once, by assembly scanning, in `ApplicationServiceRegistration.AddApplicationServices()` — so a new command/query handler in `HR.LeaveManagement.Application` is picked up automatically; you never register handlers individually.

---

## Validation — how it actually runs (read this before adding a command)

If you've used MediatR + FluentValidation before, you likely expect a `ValidationBehavior` `IPipelineBehavior` that runs validators automatically before every handler. **This repo does not have one.** `ApplicationServiceRegistration` only registers AutoMapper and MediatR — no `IPipelineBehavior<,>` and no `AddValidatorsFromAssembly(...)`.

Instead, `CreateLeaveTypeCommandHandler` constructs and runs its validator **manually**, inline:

```csharp
public async Task<int> Handle(CreateLeaveTypeCommand request, CancellationToken cancellationToken)
{
    var validator = new CreateLeaveTypeCommandValidator(_leaveTypeRepository);
    var validationResult = await validator.ValidateAsync(request);

    if (validationResult.Errors.Any())
        throw new BadRequestException("Invalid Leave type", validationResult);
    ...
}
```

**Implication for you:** when you add a new command with a validator, you must repeat this `new XyzValidator(...); await validator.ValidateAsync(request); if (errors) throw new BadRequestException(...)` block yourself inside the handler — it is not automatic. `UpdateLeaveTypeCommandHandler` currently has **no validator at all**, which is worth being aware of if you're extending it (updates aren't validated the way creates are).

If you want the conventional automatic-validation behavior, that's a good, self-contained improvement: add `services.AddValidatorsFromAssembly(...)` + a generic `IPipelineBehavior<TRequest, TResponse>` and register it with `cfg.AddOpenBehavior(...)` — but that's a deliberate architecture change, not something to assume already exists.

---

## Repository pattern & EF Core

- `IGenericRepository<T>` (Application layer, `Contracts/Persistence`) defines `GetAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `DeleteByIdAsync` for any `T : BaseEntity`.
- `GenericRepository<T>` (Persistence layer) implements it against `HrDatabaseContext`.
- Entity-specific repositories (`LeaveTypeRepository`, `LeaveAllocationRepository`, `LeaveRequestRepository`) extend `GenericRepository<T>` and add entity-specific interface methods, e.g. `ILeaveTypeRepository.IsLeaveTypeUnique(string name)`.
- All of it is registered as `Scoped` in `PersistenceServiceRegistration.AddPersistenceServices()` — the generic repository is registered as an **open generic** (`typeof(IGenericRepository<>), typeof(GenericRepository<>)`), so `IGenericRepository<LeaveType>` resolves without any extra registration when you add a new entity.
- `HrDatabaseContext.SaveChangesAsync` auto-stamps `DateCreated`/`DateModified` on any tracked `BaseEntity` — you never set those fields by hand in a command handler.
- EF entity configuration (`IEntityTypeConfiguration<T>`) is applied **globally by assembly scan** in `OnModelCreating` (`modelBuilder.ApplyConfigurationsFromAssembly(...)`) — drop a new `XyzConfiguration : IEntityTypeConfiguration<Xyz>` class anywhere in the Persistence project and it's picked up automatically; you don't need to register it.

**Migrations** — from `HR.LeaveManagement.Api` (the startup project) or by pointing `--project` explicitly:

```sh
dotnet ef migrations add <Name> --project ../../Infrastructure/HR.LeaveManagement.Persistence/HR.LeaveManagement.Persistence.csproj
dotnet ef database update --project ../../Infrastructure/HR.LeaveManagement.Persistence/HR.LeaveManagement.Persistence.csproj
```

---

## Global exception handling

`ExceptionMiddleware` (registered first in `Program.cs`, before Swagger/HTTPS/Auth) wraps the whole pipeline in try/catch and maps exceptions to a `CustomProblemDetails` (an RFC7807 `ProblemDetails` subtype with an `Errors` dictionary):

| Exception | Status code |
|---|---|
| `BadRequestException` (thrown by handlers on validation failure — carries `ValidationErrors` from FluentValidation) | 400 |
| `NotFoundException` | 404 |
| anything else | 500 (includes the raw `ex.StackTrace` in `Detail` — fine for local dev, **don't ship that to production as-is**) |

Every handled exception is also logged via `ILogger<ExceptionMiddleware>` as a JSON-serialized problem object. `HR.Employee.Api` does **not** have this middleware yet — unhandled exceptions there fall back to the ASP.NET Core default developer exception page/500 response.

---

## Ocelot API Gateway

`ocelot.json` currently defines a **single** route:

```json
{
  "DownstreamPathTemplate": "/{everything}",
  "DownstreamScheme": "https",
  "DownstreamHostAndPorts": [
    { "Host": "localhost", "Port": 7091 },
    { "Host": "localhost", "Port": 7093 }
  ],
  "UpstreamPathTemplate": "/{everything}",
  "LoadBalancerOptions": { "Type": "RoundRobin" }
}
```

> ⚠️ **This is very likely not what you want as-is.** It load-balances *every* request across ports **7091 (LeaveManagement API)** and **7093 (Employee API)** — i.e. two entirely different services on the same catch-all path — using round-robin. A client calling `https://localhost:7090/api/LeaveTypes` will get routed to the *Employee* API roughly half the time, and 404 there. This looks like a copy/paste leftover from an earlier single-route setup rather than an intentional load-balancing scenario (load balancing makes sense across **replicas of the same service**, not across two different services). See [Scenario E](#scenario-e--add-or-change-a-route-in-the-api-gateway) for the fix: give each service its own upstream path prefix instead of sharing one.

The commented-out block further down in `ocelot.json` shows the pre-gateway routing intent (`/gateway/LeaveTypes` → port 7091 only) — that's the shape to restore/extend per service.

---

## NSwag + RestApiClient — service-to-service calls

Both `HR.LeaveManagement.Api` and `HR.Employee.Api` have:
- an `nswag.json` (NSwag configuration — generates a C# client from the API's own OpenAPI document)
- an MSBuild post-build target that runs NSwag automatically **every Debug build**:

```xml
<Target Name="NSwag" AfterTargets="PostBuildEvent" Condition=" '$(Configuration)' == 'Debug' ">
  <Exec Command="dotnet tool restore"></Exec>
  <Exec WorkingDirectory="$(ProjectDir)" EnvironmentVariables="ASPNETCORE_ENVIRONMENT=Development"
        Command="$(NSwagExe_Net80) run nswag.json /variables:Configuration=$(Configuration)" />
</Target>
```

Each `nswag.json`'s `codeGenerators.openApiToCSharpClient` writes its generated client **out of its own project and into `RestApiClient`**:
- `HR.LeaveManagement.Api` → generates `RestApiClient/RestApiClient/LeaveManagement/LeaveManagementModuleAPI.cs` (+ `...Contacts.cs` for the DTOs)
- `HR.Employee.Api` → generates `RestApiClient/RestApiClient/Employee/EmployeeModuleAPI.cs` (+ `...Contacts.cs`)

`RestApiClient/ServiceRegistrationExtensions.cs` then registers both generated clients as typed `HttpClient`s (`AddHttpClient<T>` + `AddScoped<T>`), and **both are hardcoded to base URL `https://localhost:7090`** — i.e. calls go through the **Ocelot gateway**, not directly service-to-service. Combined with the round-robin gotcha above, this is the mechanism by which cross-service calls (`LeaveTypesController.GetEmployee()` → Employee API, `LeaveController.GetLeave()` → LeaveManagement API) can silently hit the wrong downstream service.

**The "why is my new client method missing" gotcha:** because the generated `.cs` files live in `RestApiClient` but are *produced* by a post-build step of a project that already references `RestApiClient` (`ProjectReference`), the dependency graph is effectively: build `RestApiClient` (with whatever was generated last time) → build the API project → *then* regenerate `RestApiClient`'s source for next time. If you add/change a controller action and immediately reference the new client method elsewhere, **you may need to build twice** — once to regenerate the client source, once more to actually compile against it.

---

## Swagger / OpenAPI

Both APIs run **both** Swashbuckle (`AddSwaggerGen`/`UseSwaggerUI`, serves interactive `/swagger` UI) and NSwag (`AddOpenApiDocument`/`UseOpenApi`, serves the raw OpenAPI document NSwag's own client generator consumes) side by side — they're independent and redundant but both are wired up; don't be surprised to see two different Swagger-ish middlewares in the same `Program.cs`.

`HostDocumentFilter` on `HR.LeaveManagement.Api` hardcodes the Swagger doc's server URL to `https://localhost:7091` — if you change that API's HTTPS port, update this filter too, or generated clients/Swagger UI's "Try it out" will point at the wrong host.

---

## What's actually implemented vs. scaffolded

Don't assume every controller is a real, working feature — this is a partially-built sample:

| Feature | Domain entity | Application (CQRS) | Repository | Controller | Status |
|---|---|---|---|---|---|
| Leave **Types** | ✅ `LeaveType` | ✅ full Create/Update/Delete/GetAll/GetDetails + validator | ✅ `LeaveTypeRepository` | ✅ wired to MediatR | **Reference implementation** — copy this pattern |
| Leave **Allocations** | ✅ `LeaveAllocation` | ✅ Create/Update/Delete commands exist | ✅ `LeaveAllocationRepository` | ⚠️ `LeaveAllocationController` is still the **default scaffolded WebAPI template** (`Get`/`Post`/`Put`/`Delete` returning `"value1"`/`"value2"` strings) — not wired to MediatR at all | Application layer ready, controller not connected yet |
| Leave **Requests** | ✅ `LeaveRequest` domain entity + `ILeaveRequestRepository`/`LeaveRequestRepository` exist | ❌ `Features/LeaveRequest/**` is excluded from compilation in `HR.LeaveManagement.Application.csproj` (`<Compile Remove="Features\LeaveRequest\**" />`) and in the unit test project | ✅ repository exists | ⚠️ `LeaveRequestsController` is also the default scaffolded template | Least complete — CQRS layer isn't even compiled in |
| **Employee** service | — | — | — | `RegistrationController` (stub, returns a fixed string) + `LeaveController` (calls the other API) | Thin proof-of-concept for cross-service calls, not a real Employee domain yet |

Use **LeaveType** as the template whenever you're told to "add a feature like the existing ones" — it's the only slice that's complete top-to-bottom.

---

## Common developer scenarios

### Scenario A — Add a new CQRS command/query to an existing feature

Example: add a `GetLeaveTypesByYearQuery`.

1. Create `Features/LeaveType/Queries/GetLeaveTypesByYear/GetLeaveTypesByYearQuery.cs` implementing `IRequest<List<LeaveTypeDto>>`.
2. Add `GetLeaveTypesByYearQueryHandler : IRequestHandler<GetLeaveTypesByYearQuery, List<LeaveTypeDto>>` next to it, injecting `IMapper` and/or `ILeaveTypeRepository` via constructor.
3. If it needs validation, write the validator and call it **manually inside the handler** exactly like `CreateLeaveTypeCommandHandler` does — see [Validation](#validation--how-it-actually-runs-read-this-before-adding-a-command).
4. Add a controller action on `LeaveTypesController` that does `await _mediator.Send(new GetLeaveTypesByYearQuery(...))`.
5. No DI registration needed — MediatR finds the handler by assembly scan.

### Scenario B — Add a brand-new vertical slice/feature (end-to-end)

Use LeaveType as your template, top to bottom:

1. **Domain**: add the entity in `HR.LeaveManagement.Domain` inheriting `BaseEntity`.
2. **Persistence**: add `DbSet<T>` to `HrDatabaseContext`, add an `IEntityTypeConfiguration<T>` under `Configurations/` (picked up automatically), then `dotnet ef migrations add <Name>` + `dotnet ef database update`.
3. **Application contracts**: add `IXyzRepository : IGenericRepository<Xyz>` under `Contracts/Persistence` for anything beyond the generic CRUD methods.
4. **Persistence repository**: implement `XyzRepository : GenericRepository<Xyz>, IXyzRepository`.
5. **Register** the new repository interface/implementation pair in `PersistenceServiceRegistration.AddPersistenceServices()` (the generic repository itself needs no new registration — only entity-specific interfaces do).
6. **Application CQRS**: add `Features/Xyz/Commands/...` and `Features/Xyz/Queries/...` following the LeaveType folder shape (Command/Query + Handler + Validator where needed + DTO via AutoMapper profile in `MappingProfiles/`).
7. **API**: add `XyzController : ControllerBase` that forwards to `IMediator`, following `LeaveTypesController`.
8. **Tests**: add a `Mock<IXyzRepository>` helper under `Test/HR.LeaveManagement.Application.UnitTests/Mocks/` (see `MockLeaveTypeRepository`) and handler tests under `Features/Xyz/...` in the unit test project; add EF InMemory integration tests under the Persistence integration test project if the feature has non-trivial querying.

### Scenario C — Add a brand-new microservice

Mirror `HR.Employee.Api`'s folder shape (or grow it into the full 4-layer shape from Scenario B if the new service has real business logic):

1. `dotnet new webapi -n HR.<Name>.Api` under `ServiceApplications/<Name>/API/`.
2. Add it to `HR.Management.Clean.sln` (`dotnet sln add ...`), under a matching solution folder (`API`/`Core`/`Infrastructure` as needed) so it shows up the same way as the existing services in Visual Studio/Rider.
3. Pick two free ports for its `launchSettings.json` (`https`/`http`) — don't reuse 7090–7094.
4. If it needs to call another service, reference `RestApiClient` and add a new NSwag config (`nswag.json`) + post-build target copied from `HR.Employee.Api.csproj`, generating into a new subfolder of `RestApiClient/RestApiClient/<Name>/`.
5. Register its typed client in `RestApiClient/ServiceRegistrationExtensions.cs` — and this time, give it its **own** correctly-scoped Ocelot route rather than reusing the shared catch-all (see Scenario E).
6. Add its downstream route to `ocelot.json`.

### Scenario D — Change a connection string / run on a teammate's machine

Edit `ServiceApplications/LeaveManagement/API/HR.LeaveManagement.Api/appsettings.json` → `ConnectionStrings:HrDatabaseConnectionString`, replacing `Server=SWD-RASEL-L` with your own SQL Server instance. For per-developer overrides without touching the checked-in file, prefer `appsettings.Development.json` (already present, currently empty of this key) or a user secret (`dotnet user-secrets set "ConnectionStrings:HrDatabaseConnectionString" "..."`) instead of committing your machine name.

### Scenario E — Add or change a route in the API Gateway

To fix the round-robin gotcha (or add a new service), give each service its **own** upstream path instead of sharing `/{everything}`:

```json
{
  "Routes": [
    {
      "DownstreamPathTemplate": "/{everything}",
      "DownstreamScheme": "https",
      "DownstreamHostAndPorts": [{ "Host": "localhost", "Port": 7091 }],
      "UpstreamPathTemplate": "/leavemanagement/{everything}"
    },
    {
      "DownstreamPathTemplate": "/{everything}",
      "DownstreamScheme": "https",
      "DownstreamHostAndPorts": [{ "Host": "localhost", "Port": 7093 }],
      "UpstreamPathTemplate": "/employee/{everything}"
    }
  ]
}
```

Only reach for `LoadBalancerOptions`/`RoundRobin` when a single `DownstreamHostAndPorts` array lists **multiple instances of the same service** (e.g. two replicas of LeaveManagement API behind the gateway) — not when it lists two different services.

### Scenario F — Regenerate an NSwag client after changing an API's contract

Normally you don't do anything manually — the post-build target regenerates the client automatically on your next Debug build of the API project. If you need to force it or you're not seeing your changes:

1. Rebuild the **API project whose contract changed** (not just `RestApiClient`) — that's what triggers NSwag's post-build step.
2. If the *consumer* of the client (the other API) doesn't see the new method/DTO yet, **build it a second time** — see the "build twice" gotcha in [NSwag + RestApiClient](#nswag--restapiclient--service-to-service-calls).
3. If NSwag reports it can't find the built DLL, make sure you built in `Debug` configuration (the target is conditioned on `'$(Configuration)' == 'Debug'` — it does **not** run in Release builds) and that `assemblyPaths` in `nswag.json` still matches the project's actual output path.

---

## Testing

```sh
dotnet test Test/HR.LeaveManagement.Application.UnitTests
dotnet test Test/HR.LeaveManagement.Persistence.IntegrationTests
# or, from the repo root, run every test project in the solution:
dotnet test
```

- **Unit tests** (`HR.LeaveManagement.Application.UnitTests`): xUnit + Moq + Shouldly, testing command/query handlers in isolation against mocked repositories (see `Mocks/MockLeaveTypeRepository.cs` for the pattern — build a mock the same way for any new repository interface). Note the `<Compile Remove="Features\LeaveAllocations\**" />`/`LeaveRequests\**` exclusions in this test project mirror the Application project's own exclusions — don't be surprised those folders exist but aren't part of the build.
- **Integration tests** (`HR.LeaveManagement.Persistence.IntegrationTests`): xUnit against `Microsoft.EntityFrameworkCore.InMemory` — no real SQL Server needed to run these.
- There is no test project for `HR.Employee.Api` or `OcelotApiGateway` yet.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `HR.LeaveManagement.Api` fails to start with a SQL connection error | `appsettings.json`'s `Server=SWD-RASEL-L` doesn't exist on your machine | Point the connection string at your own SQL Server instance ([Scenario D](#scenario-d--change-a-connection-string--run-on-a-teammates-machine)) |
| Tables don't exist / "Invalid object name 'LeaveTypes'" | Migration was never applied | `dotnet ef database update` from the Persistence project (see [First-time setup](#first-time-setup)) — don't hand-run the old SQL scripts if the EF migration already exists, or you risk them drifting out of sync |
| Calling `https://localhost:7090/...` sometimes 404s, sometimes works | Ocelot's single catch-all route round-robins between the LeaveManagement API (7091) and the Employee API (7093) on every path | Give each service its own upstream path prefix ([Scenario E](#scenario-e--add-or-change-a-route-in-the-api-gateway)) instead of one shared route |
| `LeaveTypesController.GetEmployee()` or `LeaveController.GetLeave()` intermittently fails/500s | Same root cause as above — `RestApiClient`'s typed clients call the gateway (`localhost:7090`), which may round-robin to the wrong downstream API | Same fix, plus consider pointing `ServiceRegistrationExtensions` at each service directly (bypassing the gateway) for internal service-to-service calls, which is a more common pattern than routing internal calls back through the public gateway |
| A new/changed API method isn't visible on the generated client (`LeaveManagementModuleAPI`/`EmployeeModuleAPI`) | NSwag regenerates the client source on **the API project's** post-build, but the consuming project may have compiled against the *previous* version of that source in the same build pass | Build the changed API project, then build again (or build the solution twice) — see [NSwag + RestApiClient](#nswag--restapiclient--service-to-service-calls) |
| NSwag post-build step throws "could not find assembly" | `assemblyPaths` in `nswag.json` (e.g. `bin/Debug/net8.0/HR.LeaveManagement.Api.dll`) doesn't match because you built `Release`, or a different TFM | The NSwag target only runs for `Configuration == Debug` — build Debug, or update `nswag.json` if you've changed target framework/output path |
| `dotnet tool restore` in the NSwag post-build prints "no manifest found" | There is no `.config/dotnet-tools.json` in this repo | Harmless — `$(NSwagExe_Net80)` comes from the `NSwag.MSBuild` NuGet package itself, not a global/local dotnet tool, so the missing manifest doesn't break the actual code-gen step |
| `LeaveAllocationController`/`LeaveRequestsController` return placeholder `"value1"`/`"value2"` strings | Those controllers were never wired up to MediatR — they're still the default `dotnet new webapi` scaffold | Wire them the same way `LeaveTypesController` is wired (see [What's actually implemented vs. scaffolded](#whats-actually-implemented-vs-scaffolded) and [Scenario A](#scenario-a--add-a-new-cqrs-commandquery-to-an-existing-feature)) |
| Compile error referencing a class under `Features/LeaveRequest/...` | That folder is explicitly excluded from compilation in `HR.LeaveManagement.Application.csproj` (`<Compile Remove="Features\LeaveRequest\**" />`) | Either remove the exclusion once the feature is ready, or keep working in a different folder name that isn't excluded |
| Browser blocks the API call with a cert error | Local HTTPS dev certificate isn't trusted yet | `dotnet dev-certs https --trust` |
| CORS error calling `HR.LeaveManagement.Api` from a browser app on a different origin | `AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader` CORS policy named `"all"` is defined but you must confirm the controller/endpoint actually has `[EnableCors("all")]` or `app.UseCors("all")` applied — check `Program.cs`'s policy registration is actually being applied to the pipeline you're hitting | Add `app.UseCors("all")` to the middleware pipeline if it's missing, or scope the policy down from `AllowAnyOrigin` before shipping this beyond local dev |

---

## Useful links

- [Ocelot documentation](https://ocelot.readthedocs.io/)
- [NSwag documentation](https://github.com/RicoSuter/NSwag)
- [MediatR](https://github.com/jbogard/MediatR)
- [FluentValidation](https://docs.fluentvalidation.net/)
- [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
