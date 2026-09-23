# HR Management System — Clean Architecture, CQRS & API Gateway (.NET 8) — Developer Guide

A microservices-based HR Management System built with **.NET 8**, following **Clean Architecture** with **CQRS**/**MediatR**, sitting behind an **Ocelot API Gateway**, with **NSwag**-generated typed clients for service-to-service calls.

This guide is written for a developer who has just cloned the repo: how the solution is laid out, how to run it end-to-end, how the architecture actually works layer by layer, and — critically — the non-obvious gotchas already baked into this codebase that will otherwise cost you an afternoon.

---

## Table of contents

1. [Solution architecture](#solution-architecture)
2. [Solution / folder map](#solution--folder-map)
    - [Tech stack & package versions](#tech-stack--package-versions)
3. [Prerequisites](#prerequisites)
4. [Ports & URLs reference](#ports--urls-reference)
5. [First-time setup](#first-time-setup)
    - [Database schema](#database-schema)
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
                         │   (one route per controller) │
                         └──────────────┬────────────┘
                                        │ /api/LeaveTypes, /api/LeaveAllocation, /api/LeaveRequests → 7091
                                        │ /api/Leave, /api/Registration                          → 7093
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

- **OcelotApiGateway** is the single public entry point in front of both APIs. Each controller has its own route, so a path always goes to the service that owns it.
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
│   └── RestApiClient/               # NSwag-generated clients + DI registration (shared by both APIs).
│                                    # Its csproj is OutputType=Exe with a placeholder Program.cs; it's only used as a library.
├── ServiceApplications/
│   ├── LeaveManagement/
│   │   ├── Core/
│   │   │   ├── HR.LeaveManagement.Domain/        # Entities (LeaveType, LeaveAllocation, LeaveRequest), BaseEntity
│   │   │   └── HR.LeaveManagement.Application/   # CQRS Features, validators, DTOs, contracts (interfaces)
│   │   ├── Infrastructure/
│   │   │   ├── HR.LeaveManagement.Persistence/   # DbContext, EF configurations, migrations, repositories
│   │   │   └── HR.LeaveManagement.Infrastructure/# Email sender (SendGrid), logger adapter
│   │   └── API/
│   │       └── HR.LeaveManagement.Api/           # Controllers, MIddleware/ (sic), Models/, HostDocumentFilter, Program.cs, nswag.json
│   └── Employee/
│       └── API/
│           └── HR.Employee.Api/                  # Controllers, Program.cs, nswag.json (no Domain/Application/Infra yet)
└── Test/
    ├── HR.LeaveManagement.Application.UnitTests/       # xUnit + Moq + Shouldly
    └── HR.LeaveManagement.Persistence.IntegrationTests/ # xUnit + EF Core InMemory
```

The `.sln` solution folders mirror the physical tree:

```
ApiGateway/        → OcelotApiGateway
RestApiClient/     → RestApiClient
ServiceApplications/
├── LeaveManagement/
│   ├── API/            → HR.LeaveManagement.Api
│   ├── Core/           → HR.LeaveManagement.Domain, HR.LeaveManagement.Application
│   ├── Infrastructure/ → HR.LeaveManagement.Persistence, HR.LeaveManagement.Infrastructure
│   └── UI/             → (empty: the Angular microfrontend lives in the sibling angular17-microfrontend folder)
└── Employee/
    └── API/            → HR.Employee.Api
Test/              → both test projects
```

### Tech stack & package versions

| Area | Package | Version |
|---|---|---|
| Runtime | .NET SDK (`global.json`, `rollForward: latestFeature`) | 8.0.200 |
| Gateway | Ocelot | 23.3.3 |
| CQRS | MediatR | 12.2.0 |
| Mapping | AutoMapper | 13.0.1 |
| Validation | FluentValidation | 11.9.1 |
| Data | Microsoft.EntityFrameworkCore.SqlServer / .Tools | 8.0.4 |
| OpenAPI | NSwag.AspNetCore / NSwag.MSBuild | 14.1.0 |
| OpenAPI | Swashbuckle.AspNetCore | 6.4.0 |
| Email | SendGrid | 9.29.3 |
| Tests | xUnit 2.5.3, Moq 4.20.70, Shouldly 4.2.1, EF Core InMemory 8.0.4 | |

---

## Prerequisites

- **.NET 8 SDK 8.0.200** or compatible — pinned in [`global.json`](global.json). If `dotnet --version` reports an older/newer major, install the matching SDK or the build will fail to resolve.
- **SQL Server** (LocalDB, Express, or full) reachable with Windows/Trusted authentication, or adjust the connection string to use SQL auth.
- A REST client for exploring the APIs. Swagger UI is built in (see below), or use the `.http` file in each API project (Visual Studio's HTTP editor, Rider, or the VS Code REST Client extension). `HR.LeaveManagement.Api.http` covers every LeaveTypes endpoint plus the placeholder controllers. `HR.Employee.Api.http` covers `Registration` and `Leave`. Both call the API directly and have a commented-out line to switch to the gateway on 7090.
- No Node/npm or frontend tooling is required for this repository — the `UI` solution folder is a placeholder.

---

## Ports & URLs reference

| Project | HTTPS | HTTP | Swagger UI |
|---|---|---|---|
| OcelotApiGateway | `https://localhost:7090` | — | n/a (gateway only) |

The IIS Express ports in each `launchSettings.json` (29237, 49044, 6989) aren't used by anything else in the solution. `ocelot.json`'s `GlobalConfiguration.BaseUrl` (`https://localhost:7090`) must match the gateway's own URL; it's used for URLs Ocelot generates, not for routing.
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
  "HrDatabaseConnectionString": "Server=DPM2562;Database=db_hr_leavemanagement;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"
}
```

`Server=DPM2562` is the last committer's machine name — **you must change this** to your own SQL Server instance (e.g. `.`, `localhost`, `(localdb)\mssqllocaldb`, or a named instance) before the API will start.

The same `appsettings.json` also has an `EmailSettings` section (`ApiKey: "SendGrid-Key"` placeholder, `FromAddress`, `FromName`) used by the SendGrid `EmailSender`. Nothing calls the email sender yet, so the placeholder key doesn't stop the API from starting.

EF Core migrations are checked in under `HR.LeaveManagement.Persistence/Migrations/` (see [Database schema](#database-schema) for the list), including seed data for a "Vacation" leave type (`LeaveTypeConfiguration.HasData`). **Prefer applying that migration over the raw SQL scripts** that used to be pasted in this README:

```sh
dotnet tool install --global dotnet-ef   # once, if you don't have it
cd ServiceApplications/LeaveManagement/API/HR.LeaveManagement.Api
dotnet ef database update --project ../../Infrastructure/HR.LeaveManagement.Persistence/HR.LeaveManagement.Persistence.csproj
```

This creates `db_hr_leavemanagement` with `LeaveTypes`, `LeaveAllocations`, and `LeaveRequests` tables (matching what the old manual SQL scripts did, but versioned and repeatable).

### Database schema

Current schema of `db_hr_leavemanagement` (scripted from SQL Server, 2024-09-16). Every table has an `int IDENTITY(1,1)` clustered primary key `Id`, plus nullable `DateCreated`/`DateModified` audit columns stamped by `HrDatabaseContext.SaveChangesAsync`.

**`dbo.LeaveTypes`**

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | `int` IDENTITY(1,1) | NOT NULL | PK (`PK_LeaveTypes`) |
| `Name` | `nvarchar(100)` | NOT NULL | |
| `DefaultDays` | `int` | NOT NULL | |
| `DateCreated` | `datetime2(7)` | NULL | |
| `DateModified` | `datetime2(7)` | NULL | |

**`dbo.LeaveRequests`**

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | `int` IDENTITY(1,1) | NOT NULL | PK (`PK_LeaveRequests`) |
| `StartDate` | `datetime2(7)` | NOT NULL | |
| `EndDate` | `datetime2(7)` | NOT NULL | |
| `DateRequested` | `datetime2(7)` | NOT NULL | |
| `RequestComments` | `nvarchar(max)` | NULL | |
| `Approved` | `bit` | NULL | `NULL` = pending |
| `Cancelled` | `bit` | NOT NULL | |
| `RequestingEmployeeId` | `nvarchar(max)` | NOT NULL | |
| `LeaveTypeId` | `int` | NOT NULL | FK → `LeaveTypes.Id`, `ON DELETE CASCADE` |
| `DateCreated` | `datetime2(7)` | NULL | |
| `DateModified` | `datetime2(7)` | NULL | |

**`dbo.LeaveAllocations`**

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | `int` IDENTITY(1,1) | NOT NULL | PK (`PK_LeaveAllocations`) |
| `NumberOfDays` | `int` | NOT NULL | |
| `Period` | `int` | NOT NULL | |
| `LeaveTypeId` | `int` | NOT NULL | FK → `LeaveTypes.Id`, `ON DELETE CASCADE` |
| `DateCreated` | `datetime2(7)` | NULL | |
| `DateModified` | `datetime2(7)` | NULL | |
| `EmployeeId` | `nvarchar(max)` | NOT NULL | Default `N''` |

**Relationships**

```
LeaveTypes (1) ──< (many) LeaveRequests     FK_LeaveRequests_LeaveTypes_LeaveTypeId     ON DELETE CASCADE
LeaveTypes (1) ──< (many) LeaveAllocations  FK_LeaveAllocations_LeaveTypes_LeaveTypeId  ON DELETE CASCADE
```

Deleting a leave type also deletes every request and allocation that uses it.

**Migrations that build this schema:**

| Migration | What it does |
|---|---|
| `20240501025419_InitialMigration` | Creates all three tables, both foreign keys, and seeds the "Vacation" leave type |
| `20260923093000_AddEmployeeIdToLeaveAllocation` | Adds `LeaveAllocations.EmployeeId` (`nvarchar(max) NOT NULL DEFAULT N''`) |

> ⚠️ **If your database already has `LeaveAllocations.EmployeeId`** (it was added outside the migrations before `AddEmployeeIdToLeaveAllocation` existed), `dotnet ef database update` will fail with "Column names in each table must be unique". Record the migration as applied instead of running it:
>
> ```sql
> INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
> VALUES (N'20260923093000_AddEmployeeIdToLeaveAllocation', N'8.0.4');
> ```

<details>
<summary>Full SQL Server DDL script</summary>

```sql
USE [db_hr_leavemanagement]
GO

CREATE TABLE [dbo].[LeaveTypes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[DefaultDays] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
 CONSTRAINT [PK_LeaveTypes] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY]
GO

CREATE TABLE [dbo].[LeaveRequests](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StartDate] [datetime2](7) NOT NULL,
	[EndDate] [datetime2](7) NOT NULL,
	[DateRequested] [datetime2](7) NOT NULL,
	[RequestComments] [nvarchar](max) NULL,
	[Approved] [bit] NULL,
	[Cancelled] [bit] NOT NULL,
	[RequestingEmployeeId] [nvarchar](max) NOT NULL,
	[LeaveTypeId] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
 CONSTRAINT [PK_LeaveRequests] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[LeaveRequests] WITH CHECK ADD CONSTRAINT [FK_LeaveRequests_LeaveTypes_LeaveTypeId]
	FOREIGN KEY([LeaveTypeId]) REFERENCES [dbo].[LeaveTypes] ([Id]) ON DELETE CASCADE
GO

CREATE TABLE [dbo].[LeaveAllocations](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[NumberOfDays] [int] NOT NULL,
	[Period] [int] NOT NULL,
	[LeaveTypeId] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
	[EmployeeId] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_LeaveAllocations] PRIMARY KEY CLUSTERED ([Id] ASC)
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[LeaveAllocations] ADD DEFAULT (N'') FOR [EmployeeId]
GO

ALTER TABLE [dbo].[LeaveAllocations] WITH CHECK ADD CONSTRAINT [FK_LeaveAllocations_LeaveTypes_LeaveTypeId]
	FOREIGN KEY([LeaveTypeId]) REFERENCES [dbo].[LeaveTypes] ([Id]) ON DELETE CASCADE
GO
```

</details>

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
- `https://localhost:7090/api/LeaveTypes` — LeaveTypes through the gateway
- `https://localhost:7090/api/Leave` — Employee API → gateway → LeaveManagement API (a cross-service call)

The gateway has no UI. `Program.cs` maps `GET /` to `Hello World!`, but only after `await app.UseOcelot()`. Ocelot handles every request first, and it has no route for `/`, so the gateway root returns a 404 rather than `Hello World!`.

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

Both APIs call `RestApiClientGatewayServices()`, even though each only uses one of the two clients it registers.

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

`LeaveTypesController` just forwards HTTP verbs to `IMediator.Send(...)` — it contains **no business logic**. Its endpoints:

| Verb | Route | Sends | Returns |
|---|---|---|---|
| GET | `api/LeaveTypes` | `GetLeaveTypesQuery` | `List<LeaveTypeDto>` |
| GET | `api/LeaveTypes/{id}` | `GetLeaveTypeDetailsQuery(id)` | `LeaveTypeDetailsDto` (404 via `NotFoundException`) |
| POST | `api/LeaveTypes` | `CreateLeaveTypeCommand` (body) | 201 `CreatedAtAction` with the new id |
| PUT | `api/LeaveTypes` | `UpdateLeaveTypeCommand` (body; the id is in the body, not the route) | `200 OK` + `true` (not 204, even though the attribute says 204) |
| DELETE | `api/LeaveTypes/{id}` | `DeleteLeaveTypeCommand` | `200 OK` + `true` |
| GET | `api/LeaveTypes/GetEmployee` | — (calls `EmployeeModuleAPI.Registration_RegistrationAsync()`) | string from the Employee API |



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
- `LeaveTypeConfiguration` is the only configuration class so far. It makes `Name` required with max length 100 and seeds the "Vacation" row (Id 1, 10 days). The seed sets `DateCreated`/`DateModified` to `DateTime.Now`, which changes on every run. So each new `dotnet ef migrations add` will also include an `UpdateData` for that row. Switch it to a fixed date if that becomes noise.
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

The middleware is in `HR.LeaveManagement.Api/MIddleware/ExceptionMiddleware.cs`; the folder and namespace really are spelled `MIddleware`. Every handled exception is also logged via `ILogger<ExceptionMiddleware>` as a JSON-serialized problem object. `HR.Employee.Api` does **not** have this middleware yet — unhandled exceptions there fall back to the ASP.NET Core default developer exception page/500 response.

---

## Ocelot API Gateway

`Program.cs` loads `ocelot.json` (`reloadOnChange: true`, so route edits apply without a restart) and calls `app.UseOcelot()`. `ocelot.json` defines **two routes per controller**: one for the bare path and one for `/{everything}` beneath it. Ocelot's `{everything}` placeholder doesn't match an empty segment, so both are needed. Upstream and downstream paths are identical; the gateway only picks the host.

| Upstream (gateway) | Downstream service |
|---|---|
| `/api/LeaveTypes`, `/api/LeaveTypes/{everything}` | LeaveManagement API `https://localhost:7091` |
| `/api/LeaveAllocation`, `/api/LeaveAllocation/{everything}` | LeaveManagement API `https://localhost:7091` |
| `/api/LeaveRequests`, `/api/LeaveRequests/{everything}` | LeaveManagement API `https://localhost:7091` |
| `/api/Leave`, `/api/Leave/{everything}` | Employee API `https://localhost:7093` |
| `/api/Registration`, `/api/Registration/{everything}` | Employee API `https://localhost:7093` |

One pair looks like this:

```json
{
  "UpstreamPathTemplate": "/api/LeaveTypes",
  "DownstreamPathTemplate": "/api/LeaveTypes",
  "DownstreamScheme": "https",
  "DownstreamHostAndPorts": [ { "Host": "localhost", "Port": 7091 } ]
},
{
  "UpstreamPathTemplate": "/api/LeaveTypes/{everything}",
  "DownstreamPathTemplate": "/api/LeaveTypes/{everything}",
  "DownstreamScheme": "https",
  "DownstreamHostAndPorts": [ { "Host": "localhost", "Port": 7091 } ]
}
```

Things to know:
- **Any path not in the table returns 404 from the gateway.** That includes `/`, `/swagger` and a new controller you haven't added a route for. Adding a controller means adding its two routes (see [Scenario E](#scenario-e--add-or-change-a-route-in-the-api-gateway)).
- No `UpstreamHttpMethod` is set, so every HTTP verb is forwarded.
- There's no load balancing, auth, rate limiting or caching configured; each route has a single downstream host.
- Upstream matching is case-insensitive by default, so `/api/leavetypes` also works.

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

The generated DTOs go in a second file next to each client (`EmployeeModuleContacts.cs`, `LeaveManagementModuleContacts.cs`; "Contacts" is the spelling used in the code, in the `...Module.Contacts` namespaces). The clients use `SingleClientFromOperationId`, so method names are `<Controller>_<Action>Async`, e.g. `LeaveTypes_GetAsync()` and `Registration_RegistrationAsync()`.

`RestApiClient/ServiceRegistrationExtensions.cs` (`RestApiClientGatewayServices()`) then registers both generated clients, and **both are hardcoded to base URL `https://localhost:7090`**. So cross-service calls go through the **Ocelot gateway**, not directly service-to-service:

- `LeaveTypesController.GetEmployee()` → gateway `/api/Registration` → Employee API
- `LeaveController.GetLeave()` → gateway `/api/LeaveTypes` → LeaveManagement API

This means the gateway must be running for either cross-service call to work, and the route the client calls must exist in `ocelot.json`.

Each client is registered twice: `AddHttpClient<T>` sets a typed client's `BaseAddress`, then `AddScoped<T>` replaces it with a factory that builds the client from the plain `HttpClient` and passes the base URL explicitly. The `AddScoped` registration is the one that wins. If you change the gateway URL, change **all four** hardcoded strings.

**The "why is my new client method missing" gotcha:** because the generated `.cs` files live in `RestApiClient` but are *produced* by a post-build step of a project that already references `RestApiClient` (`ProjectReference`), the dependency graph is effectively: build `RestApiClient` (with whatever was generated last time) → build the API project → *then* regenerate `RestApiClient`'s source for next time. If you add/change a controller action and immediately reference the new client method elsewhere, **you may need to build twice** — once to regenerate the client source, once more to actually compile against it.

---

## Swagger / OpenAPI

Both APIs run **both** Swashbuckle (`AddSwaggerGen`/`UseSwaggerUI`, serves interactive `/swagger` UI) and NSwag (`AddOpenApiDocument`/`UseOpenApi`, serves the raw OpenAPI document NSwag's own client generator consumes) side by side — they're independent and redundant but both are wired up; don't be surprised to see two different Swagger-ish middlewares in the same `Program.cs`.

`HostDocumentFilter` on `HR.LeaveManagement.Api` hardcodes the Swashbuckle document's server URL to `https://localhost:7091`. If you change that API's HTTPS port, update this filter too, or Swagger UI's "Try it out" will call the wrong host. It doesn't affect the NSwag-generated clients: NSwag builds its own document from the compiled assembly, and the clients get their base URL from `ServiceRegistrationExtensions`. `HR.Employee.Api` has no such filter.

Swagger UI is only served in the `Development` environment (`if (app.Environment.IsDevelopment())`). The NSwag `UseOpenApi()` document is served in every environment.

---

## What's actually implemented vs. scaffolded

Don't assume every controller is a real, working feature — this is a partially-built sample:

| Feature | Domain entity | Application (CQRS) | Repository | Controller | Status |
|---|---|---|---|---|---|
| Leave **Types** | ✅ `LeaveType` | ✅ full Create/Update/Delete/GetAll/GetDetails + validator | ✅ `LeaveTypeRepository` | ✅ wired to MediatR | **Reference implementation** — copy this pattern |
| Leave **Allocations** | ✅ `LeaveAllocation` | ⚠️ Create/Update/Delete command + handler classes exist, but the handlers are **empty stubs** (only comments; `CreateLeaveAllocationCommandHandler` returns `null`, so awaiting it throws). No queries: the `Queries/GetLeaveAllocations` and `Queries/GetLeaveAllocationDetails` folders are declared in the csproj but not on disk. `LeaveAllocationProfile` exists. | ✅ `LeaveAllocationRepository` (generic CRUD only) | ⚠️ `LeaveAllocationController` is still the **default scaffolded WebAPI template** (`Get`/`Post`/`Put`/`Delete` returning `"value1"`/`"value2"` strings), not wired to MediatR | Skeleton only: handlers and controller both still to write |
| Leave **Requests** | ✅ `LeaveRequest` domain entity + `ILeaveRequestRepository`/`LeaveRequestRepository` exist | ❌ No `Features/LeaveRequest` folder yet. The Application csproj already has `<Compile Remove="Features\LeaveRequest\**" />`, so anything you add there **won't compile** until you remove that line. `LeaveRequestProfile` exists. | ✅ repository exists (generic CRUD only) | ⚠️ `LeaveRequestsController` is also the default scaffolded template | Least complete |
| **Employee** service | — | — | — | `RegistrationController`: `GET api/Registration` and `GET api/Registration/Login_1`, both return fixed strings. `LeaveController`: `GET api/Leave` calls the LeaveManagement API. | Thin proof-of-concept for cross-service calls, not a real Employee domain yet |

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
8. **Gateway**: add the controller's two routes (`/api/Xyz` and `/api/Xyz/{everything}` → 7091) to `ocelot.json`, or it won't be reachable through the gateway ([Scenario E](#scenario-e--add-or-change-a-route-in-the-api-gateway)).
9. **Tests**: add a `Mock<IXyzRepository>` helper under `Test/HR.LeaveManagement.Application.UnitTests/Mocks/` (see `MockLeaveTypeRepository`) and handler tests under `Features/Xyz/...` in the unit test project; add EF InMemory integration tests under the Persistence integration test project if the feature has non-trivial querying.

### Scenario C — Add a brand-new microservice

Mirror `HR.Employee.Api`'s folder shape (or grow it into the full 4-layer shape from Scenario B if the new service has real business logic):

1. `dotnet new webapi -n HR.<Name>.Api` under `ServiceApplications/<Name>/API/`.
2. Add it to `HR.Management.Clean.sln` (`dotnet sln add ... --solution-folder ServiceApplications/<Name>/API`, or drag it in Visual Studio/Rider) so it sits under `ServiceApplications/<Name>/` like the existing services.
3. Pick two free ports for its `launchSettings.json` (`https`/`http`) — don't reuse 7090–7094.
4. If it needs to call another service, reference `RestApiClient` and add a new NSwag config (`nswag.json`) + post-build target copied from `HR.Employee.Api.csproj`, generating into a new subfolder of `RestApiClient/RestApiClient/<Name>/`.
5. Register its generated client in `RestApiClient/ServiceRegistrationExtensions.cs`, following the existing two.
6. Add a pair of routes per controller to `ocelot.json`, pointing at the new port (see Scenario E).

### Scenario D — Change a connection string / run on a teammate's machine

Edit `ServiceApplications/LeaveManagement/API/HR.LeaveManagement.Api/appsettings.json` → `ConnectionStrings:HrDatabaseConnectionString`, replacing `Server=DPM2562` with your own SQL Server instance. This value has already changed between committers once, which is a sign it shouldn't live in the checked-in file. For per-developer overrides, prefer `appsettings.Development.json` (already present; it only has `Logging` settings) or a user secret (`dotnet user-secrets set "ConnectionStrings:HrDatabaseConnectionString" "..."`) instead of committing your machine name.

### Scenario E — Add or change a route in the API Gateway

Every new controller needs **two** entries in `ocelot.json`'s `Routes` array: the bare path and the `/{everything}` path. For example, a new `EmployeesController` on the Employee API:

```json
{
  "UpstreamPathTemplate": "/api/Employees",
  "DownstreamPathTemplate": "/api/Employees",
  "DownstreamScheme": "https",
  "DownstreamHostAndPorts": [ { "Host": "localhost", "Port": 7093 } ]
},
{
  "UpstreamPathTemplate": "/api/Employees/{everything}",
  "DownstreamPathTemplate": "/api/Employees/{everything}",
  "DownstreamScheme": "https",
  "DownstreamHostAndPorts": [ { "Host": "localhost", "Port": 7093 } ]
}
```

Things to watch:
- **Prefix collisions:** `/api/Leave` (Employee API) is a string prefix of `/api/LeaveTypes`, `/api/LeaveAllocation` and `/api/LeaveRequests` (LeaveManagement API). They don't collide today, because Ocelot matches whole path segments and ranks more specific templates first. Still, a request to `/api/Leave/...` always goes to the Employee API. Pick names that don't start with an existing controller name.
- The gateway reloads `ocelot.json` on save, so no restart is needed.
- Use `LoadBalancerOptions` (e.g. `RoundRobin`) only when one `DownstreamHostAndPorts` array lists **several instances of the same service**, never to spread one route across different services.
- If the route must be callable by the *other* service through `RestApiClient`, the route must exist here, because those clients call the gateway.

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

- **Unit tests** (`HR.LeaveManagement.Application.UnitTests`): xUnit + Moq + Shouldly, testing command/query handlers in isolation against mocked repositories (see `Mocks/MockLeaveTypeRepository.cs` for the pattern — build a mock the same way for any new repository interface). Current tests: `CreateLeaveTypeCommandHandlerTests` and `GetLeaveTypesQueryHandlerTests` (both under `Features/LeaveTypes/`). The test csproj also has `<Compile Remove="Features\LeaveAllocations\**" />` and `Features\LeaveRequests\**`. Those folders don't exist yet, but tests you add there **won't compile** until you remove the matching line.
- **Integration tests** (`HR.LeaveManagement.Persistence.IntegrationTests`): xUnit against `Microsoft.EntityFrameworkCore.InMemory` — no real SQL Server needed to run these.
- There is no test project for `HR.Employee.Api` or `OcelotApiGateway` yet.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `HR.LeaveManagement.Api` fails to start with a SQL connection error | `appsettings.json`'s `Server=DPM2562` doesn't exist on your machine | Point the connection string at your own SQL Server instance ([Scenario D](#scenario-d--change-a-connection-string--run-on-a-teammates-machine)) |
| Tables don't exist / "Invalid object name 'LeaveTypes'" | Migration was never applied | `dotnet ef database update` from the Persistence project (see [First-time setup](#first-time-setup)) — don't hand-run the old SQL scripts if the EF migration already exists, or you risk them drifting out of sync |
| `https://localhost:7090/<path>` returns 404 but the same path works on 7091/7093 | `ocelot.json` has no route for that path (new controller, `/`, `/swagger`, or only the bare route was added without its `/{everything}` twin) | Add both routes ([Scenario E](#scenario-e--add-or-change-a-route-in-the-api-gateway)) |
| `https://localhost:7090/...` returns 502 Bad Gateway | The downstream API for that route isn't running, or its HTTPS dev cert isn't trusted | Start the API (7091 or 7093) and run `dotnet dev-certs https --trust` |
| `GET api/LeaveTypes/GetEmployee` or `GET api/Leave` fails with an `HttpRequestException`/500 | `RestApiClient`'s clients call the **gateway** (`localhost:7090`), not the other service directly, so the gateway and the target API must both be running | Start all three apps. For internal calls, consider pointing `ServiceRegistrationExtensions` at each service directly (bypassing the gateway), which is a more common pattern than routing internal calls back through the public gateway |
| Any `LeaveAllocations` query fails with "Invalid column name 'EmployeeId'" | `AddEmployeeIdToLeaveAllocation` hasn't been applied to your database | `dotnet ef database update` ([First-time setup](#first-time-setup)) |
| `dotnet ef database update` fails with "Column names in each table must be unique" on `EmployeeId` | Your database got the column before the migration existed | Mark the migration as applied ([Database schema](#database-schema)) |
| A new/changed API method isn't visible on the generated client (`LeaveManagementModuleAPI`/`EmployeeModuleAPI`) | NSwag regenerates the client source on **the API project's** post-build, but the consuming project may have compiled against the *previous* version of that source in the same build pass | Build the changed API project, then build again (or build the solution twice) — see [NSwag + RestApiClient](#nswag--restapiclient--service-to-service-calls) |
| NSwag post-build step throws "could not find assembly" | `assemblyPaths` in `nswag.json` (e.g. `bin/Debug/net8.0/HR.LeaveManagement.Api.dll`) doesn't match because you built `Release`, or a different TFM | The NSwag target only runs for `Configuration == Debug` — build Debug, or update `nswag.json` if you've changed target framework/output path |
| `dotnet tool restore` in the NSwag post-build prints "no manifest found" | There is no `.config/dotnet-tools.json` in this repo | Harmless — `$(NSwagExe_Net80)` comes from the `NSwag.MSBuild` NuGet package itself, not a global/local dotnet tool, so the missing manifest doesn't break the actual code-gen step |
| `LeaveAllocationController`/`LeaveRequestsController` return placeholder `"value1"`/`"value2"` strings | Those controllers were never wired up to MediatR — they're still the default `dotnet new webapi` scaffold | Wire them the same way `LeaveTypesController` is wired (see [What's actually implemented vs. scaffolded](#whats-actually-implemented-vs-scaffolded) and [Scenario A](#scenario-a--add-a-new-cqrs-commandquery-to-an-existing-feature)) |
| Classes you add under `Features/LeaveRequest/...` aren't found (CS0246) | That path is excluded from compilation in `HR.LeaveManagement.Application.csproj` (`<Compile Remove="Features\LeaveRequest\**" />`, plus matching `EmbeddedResource`/`None` lines) | Remove those three lines when you start the LeaveRequest feature |
| Browser blocks the API call with a cert error | Local HTTPS dev certificate isn't trusted yet | `dotnet dev-certs https --trust` |
| CORS error calling an API **through the gateway** (`localhost:7090`) from a browser app | `HR.LeaveManagement.Api` applies its `"all"` policy (`AllowAnyOrigin/AnyMethod/AnyHeader`) with `app.UseCors("all")` before HTTPS redirection, so direct calls to 7091 work. But the gateway has no CORS setup, and **`HR.Employee.Api` has none at all**, so preflight `OPTIONS` requests to 7090 or 7093 get no CORS headers. | Add `AddCors`/`UseCors` to `OcelotApiGateway/Program.cs` (before `UseOcelot()`) and to `HR.Employee.Api`. Scope `AllowAnyOrigin` down (the commented-out `.WithOrigins("https://localhost:4201", "http://localhost:4202")` is a starting point) before shipping beyond local dev. |

---

## Useful links

- [Ocelot documentation](https://ocelot.readthedocs.io/)
- [NSwag documentation](https://github.com/RicoSuter/NSwag)
- [MediatR](https://github.com/jbogard/MediatR)
- [FluentValidation](https://docs.fluentvalidation.net/)
- [EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
