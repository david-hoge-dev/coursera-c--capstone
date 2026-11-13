# Development Challenges and Solutions

This document records the main implementation challenges encountered while building LogiTrack and the approaches used to address them.

## Relating Orders to Inventory

An order can contain multiple inventory items, but the API must avoid returning an EF Core entity graph directly. The implementation accepts an order request containing an `items` collection, creates the order and new items together, and maps the result to response records. The order detail endpoint returns the related items.

Deleting an order introduced a separate concern: inventory items should survive, but the database relationship must be cleared first. The delete action bulk-updates matching items to set `OrderId` to `null`, then deletes the order in a transaction. The transaction prevents the detachment from being committed if the order delete fails.

## Adding Identity to an Existing Data Context

Identity needs its own user, role, claim, login, and token tables alongside the application's existing tables. The context was changed to inherit from `IdentityDbContext<ApplicationUser>`, and the Identity EF store was registered in dependency injection. An `AddIdentity` migration added those tables while preserving the existing inventory and order schema.

## Protecting Manager Operations

Role authorization only works when the authenticated principal contains role claims. The login action therefore reads a user's Identity roles and adds them to the JWT. A local manager-assignment command grants the role to an existing account; public registration does not let a caller choose their own privileged role. Users must sign in again after promotion so their next token contains the updated claim.

## Avoiding Cache/Data Confusion

The inventory list is cached for 30 seconds, but SQLite remains the durable source of truth. This creates a deliberate freshness tradeoff: writes may not appear in the list until expiration. The benchmark and persistence checks use isolated temporary databases so they can test stale data, refresh, and restart behavior without modifying the developer's database.

## Measuring Instead of Guessing

EF Core diagnostics showed the order detail read was already one SQL query, so adding `Include` made eager loading explicit rather than reducing query count. The original order deletion issued one update per item; replacing those with a bulk update and delete reduced the observed command count from five to two for an order with three items. The repeatable cache benchmark compares the uncached query with a warmed cache and checks that cache hits issue no SQL.

## Remaining Work

An isolated API test exposed missing domain validation: blank names and negative quantities were accepted, and a nonexistent inventory `OrderId` caused an unhandled database error. These are known issues, not solved challenges. Add request constraints and predictable error mapping before production use.