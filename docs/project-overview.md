# LogiTrack API Project Overview

## Purpose

LogiTrack is an ASP.NET Core Web API for inventory and order management. The project is built on .NET 10, uses Entity Framework Core with SQLite, and exposes HTTP endpoints that can be explored through the development Swagger UI.

## Key Features

### Inventory management

The inventory API lists items, retrieves an item by ID, creates inventory items, and deletes them. Inventory items contain a name, quantity, location, and optional order relationship. The list endpoint caches its response in process memory for 30 seconds.

### Order management

The order API lists orders, retrieves an order with its items, creates an order with multiple new inventory items, and deletes an order. Deleting an order detaches its items first so they remain in inventory.

### Identity and JWT authentication

ASP.NET Core Identity stores users and roles in SQLite. Users register and log in through the auth API. Successful login returns a signed JWT, which clients send as a bearer token. Any authenticated user can read inventory and orders; creating and deleting require the `Manager` role.

### Persistent state and API exploration

SQLite stores inventory, orders, users, and roles across application restarts when the same database file is retained. OpenAPI describes the routes, and the development Swagger UI supports trying them with a bearer token.

## Technologies

- ASP.NET Core MVC controllers and dependency injection.
- Entity Framework Core and SQLite.
- ASP.NET Core Identity and JWT bearer authentication.
- `IMemoryCache` for the inventory list.
- Built-in OpenAPI support and Swagger UI for development.

## Current Scope and Limitations

Orders are not associated with individual users or tenants. The inventory-list cache can be stale for up to 30 seconds after a write. The API has not yet implemented comprehensive request limits, pagination, or production hosting configuration. See the [security overview](security-overview.md) and [production-readiness review](production-readiness-review.md).