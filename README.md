# Online Cart

A microservices e-commerce backend built on **.NET** and **Rhetos**, fronted by a **YARP** API gateway. The system is split into five independently deployable services, each owning its own SQL Server database (database-per-service).

## Services

| Service        | Project           | Role                                                                  |
| -------------- | ----------------- | --------------------------------------------------------------------- |
| **Auth**       | `AuthService`     | Issues JWTs (login, guest tokens). Token authority for all services.  |
| **Gateway**    | `ApiGateway`      | YARP reverse proxy. Single public entry point; routes other services. |
| **Catalog**    | `CatalogService`  | Products / pricing. Publishes product events.                         |
| **Cart**       | `CartService`     | Shopping carts, checkout. Consumes product events, reserves stock.    |
| **Inventory**  | `InventoryService`| Stock levels / reservations. Consumes reserve calls from Cart.        |

## Ports

Each service binds its own HTTPS/HTTP port (see each project's `Properties/launchSettings.json`). The gateway is the only endpoint clients should call.

| Service    | HTTPS                    | HTTP                    |
| ---------- | ------------------------ | ----------------------- |
| Gateway    | `https://localhost:7000` | `http://localhost:5000` |
| Auth       | `https://localhost:7001` | `http://localhost:5001` |
| Catalog    | `https://localhost:7002` | `http://localhost:5002` |
| Cart       | `https://localhost:7003` | `http://localhost:5003` |
| Inventory  | `https://localhost:7004` | `http://localhost:5004` |

## Configuration

Each service has `appsettings.json` with additional parameters:

- **`ConnectionStrings:RhetosConnectionString`** – service's SQL Server database.
- **`Jwt`** – `Issuer`, `Audience`, `SigningKey` (must match the token-issuing Auth service and the gateway).
- **Gateway** – route table + destination cluster addresses.
- **Cart Inventory:ReserveUrl** – URL of the Inventory reserve endpoint.
- **Outbox:Subscribers** – downstream service inbox URLs for event delivery.

## Build

Building compiles the app **and** runs the Rhetos build pipeline, which generates the ORM, REST endpoints, permission claims, and other artifacts from the `.rhe` DSL scripts:

```
# from each service directory (or the solution root to build all)
dotnet build
```

Run this after any change to a `.rhe` file or the `AfterDeploy` scripts — the generated artifacts and REST API are produced here.

## Database setup

Rhetos applies the schema and seed data (`AfterDeploy/*.sql`) to each service's database. After a successful build AfterDeployExecuter runs the scripts.


## Run

Manually services can be started (each in its own terminal) this way:

```
dotnet run --project AuthService
dotnet run --project CatalogService
dotnet run --project InventoryService
dotnet run --project CartService
dotnet run --project ApiGateway
```

Recommended approach: Startup configuration in VS which runs all 5 servers.


## Authentication & authorization

- **Auth service** issues JWTs; the **gateway** enforces coarse route policies (`anonymous` vs `authenticated`).
- Fine-grained authorization is enforced in each service via **Rhetos claims** (per-role Read/Write on entities and actions).
- Cart enforces **row-level ownership** (`RowPermissions`) so a user can only read/write their own cart and items.
