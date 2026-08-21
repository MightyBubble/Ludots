# Exchange Architecture

Exchange is the neutral Core module for rule-constrained settlement of inputs into outputs, effects, or state changes.

The module exists because item commerce, item conversion, crafting, barter, and future 4X value exchange share the same runtime shape: validate participants and inputs, reserve or consume those inputs, settle outputs atomically, and publish gameplay effects through GAS. Content may present these flows as a merchant, forge, market, recipe, diplomacy deal, city conversion, or trade route payout, but those names are not Core architecture names.

## Core Semantic

An `ExchangeOperationDefinition` describes one settlement operation:

* `Inputs`: required quantities from an actor slot.
* `Outputs`: item creation, item movement, or GAS effect requests.
* `ExchangeExecutionContext`: runtime actor slots for source, target, and context.
* `ExchangeOperationKey`: runtime lookup key made from `operationId` and optional `scopeKey`.

The domain sentence is:

> Exchange settles configured inputs into configured outputs under a runtime context.

That sentence deliberately avoids scenario words. A purchase, sale, crafting recipe, diplomacy trade, or resource conversion is content mapped onto this shared settlement model.

## Identity Model

Exchange uses two IDs:

* `operationId`: the registered semantic operation, allocated by `ExchangeOperationRegistry` through `StringIntRegistry`.
* `scopeKey`: an optional runtime scope for dynamic operations.

Lookup order is:

```text
(operationId, scopeKey) scoped runtime definition
operationId static config definition
```

Static/template operations use only `operationId`. Dynamic operations, such as generated offers or 4X negotiations, use `operationId + scopeKey`. The `scopeKey` is never the operation identity by itself; it scopes a runtime definition under the operation kind. This prevents unrelated dynamic operations from colliding when they happen to share the same scope key.

`ExchangeScopedOperationStore` only indexes runtime definitions by the pair `(operationId, scopeKey)`. A `scopeKey` alone is not a valid Exchange identity, because unrelated operation kinds may reuse the same runtime scope.

When a call site already has an `ExchangeOperationKey`, that key is the sole operation lookup identity. `ExchangeExecutionContext.ScopeKey` exists for the convenience overload `TryExecute(int operationId, context)`, which derives the lookup key from the context when no explicit key was supplied.

## Runtime Settlement

`ExchangeRuntime` owns the settlement sequence:

1. Resolve a scoped operation first, then the static operation registry.
2. Validate every input and output before mutation.
3. Consume item stack inputs through `InventoryRuntimeService`.
4. Create or move item outputs through `InventoryRuntimeService`.
5. Roll back consumed, created, and moved items if any item output fails.
6. Publish GAS effect outputs only after item settlement succeeds.

Output validation is cumulative. Exchange reserves each validated item placement against later outputs in the same operation, so two outputs targeting the same container cannot both pass against the same empty cell or named slot. This keeps output rejection in validation instead of relying on apply-time failure for ordinary placement conflicts.

Inventory remains the ECS SSOT for item instances, containers, and locations. Exchange never stores a second inventory snapshot or a private container model.

## GAS Integration

GAS invokes Exchange through the `Exchange` preset and `ExecuteExchange` built-in handler.

The effect parameters are:

* `_ep.exchangeOperationId`: required `operationId`.
* `_ep.exchangeScopeKey`: optional runtime scope key.

This keeps ability, requirement, effect, and future progression integration on the existing GAS effect pipeline. Exchange does not create a second ability/effect runtime.

## Config Pipeline

Exchange operations are loaded through `ConfigPipeline` and `config_catalog.json` at `Exchange/operations.json`.

The load order is important:

1. Items load first so Exchange can resolve item definition IDs.
2. Exchange operation IDs load before GAS effects so effects can reference operation IDs.
3. GAS effects load and validate `Exchange` presets.
4. Exchange operation definitions load with item and effect references resolved.

Config files describe neutral operation inputs and outputs. Scenario naming belongs in the operation IDs, mod files, UI labels, or display text, not in Core type names.

## Scenario Mapping

Common game features map onto Exchange without new Core concepts:

* Merchant purchase: source pays an item stack; output creates or moves an item to the source container.
* Sale: source consumes or moves an item; output publishes a value effect or creates currency.
* Crafting recipe: source consumes ingredient item stacks; output creates a crafted item.
* 4X conversion: source spends resource item/value effects; output publishes settlement effects or moves generated goods to target/context-owned containers.

The showcase currently uses operation IDs such as `item_showcase.buy_ap_ammo`, `item_showcase.sell_artifact`, and `item_showcase.forge_crimson_gem`. Those are content IDs. The Core module still sees only Exchange inputs, outputs, actors, and settlement.

## Hot Path Rules

Exchange runtime code must stay allocation-conscious after warmup:

* Reuse runtime scratch lists owned by `ExchangeRuntime` and `InventoryRuntimeService`.
* Do not use LINQ, iterator blocks, or per-execution transient collections in settlement paths.
* Use registered integer IDs instead of string comparisons during execution.
* Keep ECS components blittable and keep scenario-specific data outside Core components.

Architecture guard tests enforce the absence of obvious scenario words and allocation-heavy patterns in the Core Exchange hot path.

## Related Paths

* `src/Core/Gameplay/Exchange/`
* `src/Core/Gameplay/Items/InventoryRuntimeService.cs`
* `src/Core/Gameplay/GAS/BuiltinHandlers.cs`
* `assets/Configs/Exchange/operations.json`
* `mods/showcases/item_system/ItemSystemShowcaseMod/assets/Exchange/operations.json`
* `docs/adr/ADR-0005-exchange-operation-scope-key.md`
