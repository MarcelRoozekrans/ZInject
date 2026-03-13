# Sample App: E-Commerce Redesign

**Date:** 2026-03-12
**Status:** Approved

## Goal

Replace the single-file sample with a structured e-commerce demo that exercises all ZeroInject features naturally: basic lifetimes, decorator pattern, open generics, and the standalone container.

## Domain

**Models** (`Domain.cs`):
- `record Product(int Id, string Name, decimal Price)`
- `record Order(int Id, int ProductId, int Quantity)`

**Services** (`Services.cs`) — all ZeroInject-annotated:
- `[Scoped] OrderService : IOrderService` — depends on `IProductRepository<Product>`, `IPaymentProcessor`
- `[Scoped] ProductRepository<T> : IProductRepository<T>` — open generic
- `[Transient] StripePaymentProcessor : IPaymentProcessor`
- `[Singleton] ConsoleLogger : ILogger`
- `[Decorator] LoggingOrderService : IOrderService` — wraps `IOrderService inner`, also takes `ILogger`

## File Structure

```
samples/ZeroInject.Sample/
├── Program.cs                     # Runs all 4 use cases with headers
├── Domain.cs                      # Product, Order records
├── Services.cs                    # All annotated services + decorator
└── UseCases/
    ├── 01_BasicRegistration.cs    # Transient/scoped/singleton lifetime demo
    ├── 02_Decorators.cs           # LoggingOrderService wraps OrderService
    ├── 03_OpenGenerics.cs         # IProductRepository<Product> + <Order>
    └── 04_StandaloneContainer.cs  # Same resolutions via standalone provider
```

## Use Cases

### 01 — Basic Registration
- Resolve `IPaymentProcessor` twice → different instances (transient)
- Resolve `ILogger` twice → same instance (singleton)
- Resolve `IOrderService` in two separate scopes → different instances per scope

### 02 — Decorators
- Resolve `IOrderService` → returns `LoggingOrderService` wrapping `OrderService`
- Call `PlaceOrder("Laptop")` → output shows logging prefix then inner result
- Print resolved type name to confirm decoration is active

### 03 — Open Generics
- Resolve `IProductRepository<Product>` → show `Find(1)` returning a product
- Resolve `IProductRepository<Order>` → same open generic, different type arg
- Both return `ProductRepository<T>` implementation

### 04 — Standalone Container
- Create `new ZeroInjectSampleStandaloneServiceProvider()` directly
- Resolve `IOrderService` (decorated) and call `PlaceOrder`
- Resolve `IProductRepository<Product>` via open-generic runtime map
- Show scoped resolution via `CreateScope()`

## Benchmarks to Add

In `benchmarks/ZeroInject.Benchmarks/`:

**New services in `Services.cs`:**
- `[Transient] DecoratedService : IDecoratedService`
- `[Decorator] LoggingDecoratedService : IDecoratedService`
- `[Scoped] GenericRepo<T> : IGenericRepo<T>`

**New benchmarks in `ResolutionBenchmarks.cs`:**
- `Resolve_Decorated_Transient` — all 3 providers
- `Resolve_OpenGeneric_Scoped` (standalone only, since hybrid/MS DI handle this natively)
