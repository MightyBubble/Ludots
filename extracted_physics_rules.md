# Extracted Physics Architecture Rules and Constraints

## Overview
This document extracts all rules and constraints from the Physics Architecture documentation, organized by category.

---

## 1. NUMERIC PRECISION RULES BY LAYER

### Layer 1: Logic Simulation Layer
**Fixed-Point/Integer Usage:**
- ✅ **MUST use integer coordinates** for all spatial operations
- ✅ Grid coordinates: `int2 GridPosition` (integer X, Y)
- ✅ Grid cell size: `1m x 1m` (discrete integer grid units)
- ✅ Static object positions: `int2 GridPosition` (integer)
- ✅ Static object sizes: `int2 Size` (integer grid units)
- ✅ **Avoids floating-point operations** for spatial queries
- ⚠️ **Exception**: `ProxyComponent.Speed` is `float` (meters/second) - used for movement calculations but positions remain integer

**Constraints:**
- All spatial data structures use integer coordinates
- Underlying implementation: 2D array or Tiled Hash Grid with integer indexing
- Integer coordinates enable highly efficient spatial queries

### Layer 2: Detailed Simulation Layer 2D
**Floating-Point Usage:**
- ✅ **MUST use floating-point** for all calculations
- ✅ Precision: **centimeter-level**
- ✅ `TransformComponent`: floating-point positions
- ✅ `VelocityComponent`: floating-point
- ✅ All collision detection: floating-point
- ✅ All physics calculations: floating-point

**Constraints:**
- All high-precision 2D physics systems operate in floating-point space
- Update frequency: Fixed high frequency (e.g., 20 Hz)

### Layer 3: Visual Simulation Layer 3D
**Precision:**
- Variable precision (serves visual effects only)
- No core logic constraints

---

## 2. COORDINATE CONVERSION BOUNDARIES

### Materialization (Layer 1 → Layer 2)
**Conversion Rules:**
1. **Input**: `ProxyComponent.GridPosition` (int2) - integer grid coordinates
2. **Input**: `ProxyComponent.RepresentedCount` (int) - number of entities to spawn
3. **Process**: 
   - Read integer `GridPosition`
   - Convert to floating-point space for `TransformComponent`
   - Spawn entities in "continuous space" around the grid position
4. **Output**: Multiple `DetailedBody2DTag` entities with floating-point `TransformComponent`

**Constraints:**
- Conversion happens at the boundary between integer grid and floating-point space
- Entities are spawned "around" the grid position in continuous space
- State inheritance: If Proxy is `Moving`, entities continue toward floating-point target

### Dematerialization (Layer 2 → Layer 1)
**Conversion Rules:**
1. **Input**: Multiple `DetailedBody2DTag` entities with floating-point positions
2. **Process**:
   - Calculate **centroid** (average position) of all floating-point entities
   - **Convert centroid to integer grid coordinates** → new `GridPosition`
   - Count remaining entities → new `RepresentedCount` (int)
   - Aggregate average behavior → new `ProxyState`
3. **Output**: Updated `ProxyComponent` with integer `GridPosition`

**Critical Constraints:**
- ✅ **MUST convert floating-point centroid back to integer grid coordinates**
- ✅ Position conversion: float → int (rounding/truncation occurs here)
- ✅ Count aggregation: integer count of remaining entities
- ✅ State aggregation: derived from average behavior of floating-point entities

---

## 3. DETERMINISM REQUIREMENTS

### Layer 1: Logic Simulation Layer
**Determinism:**
- ✅ **Fixed update frequency**: `2.0 Hz` (every 500ms)
- ✅ **Discrete integer grid**: Ensures deterministic spatial queries
- ✅ **Integer-based operations**: Avoids floating-point determinism issues
- ✅ **Global consistency**: Logic layer "never gets cut" - ensures world consistency

**Constraints:**
- Update frequency is fixed and low to reduce CPU load
- All spatial operations use deterministic integer arithmetic
- State machine updates are deterministic based on integer grid positions

### Layer 2: Detailed Simulation Layer 2D
**Determinism:**
- ✅ **Fixed update frequency**: `20 Hz` (high frequency for stability)
- ⚠️ **Floating-point calculations**: May have determinism concerns depending on implementation
- ✅ **Performance budget**: Maximum entities per region (e.g., 10,000)

**Constraints:**
- High frequency required for physical stability
- Floating-point precision: centimeter-level

### Layer 3: Visual Simulation Layer 3D
**Determinism:**
- No determinism requirements (visual-only, does not affect logic)

---

## 4. DATA FLOW BETWEEN LAYERS

### Upward Flow (Layer 1 → Layer 2)
**Trigger Conditions:**
- Logic Proxy enters "High-Fidelity Hotspot"
- Logic Proxy enters camera view frustum
- Logic Proxy enters critical event region

**Data Transfer:**
1. Read `ProxyComponent`:
   - `RepresentedCount` (int)
   - `GridPosition` (int2)
   - `GoalPosition` (int2)
   - `State` (enum)
   - `Speed` (float)
2. Add `InDetailedModeTag` to Proxy (disables LogicSimulationSystem)
3. Create `DetailedBody2DTag` entities with:
   - Floating-point `TransformComponent`
   - `VelocityComponent`
   - `Collider2DComponent`
   - `MassComponent`
   - Other 2D physics components

**Constraints:**
- Proxy logic is **suspended** (not deleted) during detailed simulation
- `InDetailedModeTag` marks Proxy as "in detailed mode"
- Multiple physical entities created from single Proxy

### Downward Flow (Layer 2 → Layer 1)
**Trigger Conditions:**
- High-Fidelity Hotspot disappears
- Camera view frustum no longer covers region
- Critical event region deactivates

**Data Transfer:**
1. Query all `DetailedBody2DTag` entities in region
2. Aggregate state:
   - Count entities → `RepresentedCount` (int)
   - Calculate centroid (float) → convert to `GridPosition` (int2)
   - Derive average behavior → `ProxyState`
3. Update `ProxyComponent` with aggregated values
4. Remove `InDetailedModeTag` from Proxy
5. Destroy all `DetailedBody2DTag` entities
6. Re-enable LogicSimulationSystem for Proxy

**Constraints:**
- **State must be aggregated** before conversion
- **Position conversion is lossy**: float centroid → int grid position
- Count may decrease (entities may be destroyed during detailed simulation)
- Proxy resumes low-frequency logic simulation

### Layer 2 → Layer 3 Flow
**Trigger:**
- `OnDetailedBodyDestroyedEvent` published by Layer 2

**Data Transfer:**
- Event contains: `DestroyedBodyEntity`, `Position` (float), `ImpactForce`
- Layer 3 creates visual effect entities (e.g., Ragdoll) if in camera view

**Constraints:**
- Only visual effects, no logic impact
- Event-driven (not continuous)

---

## 5. TYPE CONSTRAINTS (Fix64 vs float vs int)

### Integer Types (int, int2)
**Usage:**
- ✅ **Layer 1 ONLY**: All spatial coordinates
- ✅ `ProxyComponent.GridPosition`: `int2`
- ✅ `ProxyComponent.GoalPosition`: `int2`
- ✅ `StaticObjectComponent.GridPosition`: `int2`
- ✅ `StaticObjectComponent.Size`: `int2`
- ✅ `ProxyComponent.RepresentedCount`: `int`
- ✅ Grid cell indexing: integer

**Constraints:**
- Integer types are **exclusive to Layer 1** for spatial representation
- Used for all grid-based operations
- Used for entity counting

### Floating-Point Types (float, float2)
**Usage:**
- ✅ **Layer 2 ONLY**: All physics calculations
- ✅ `TransformComponent`: floating-point positions
- ✅ `VelocityComponent`: floating-point
- ✅ All collision detection: floating-point
- ✅ All physics constraints: floating-point
- ⚠️ **Layer 1 exception**: `ProxyComponent.Speed`: `float` (but positions remain int)

**Constraints:**
- Floating-point types are **exclusive to Layer 2** for spatial representation
- Centimeter-level precision
- Used for all continuous physics calculations

### Fix64 (Not Mentioned)
**Status:**
- ❌ **NOT used** in any layer according to documentation
- Documentation specifies:
  - Layer 1: integers
  - Layer 2: floating-point
  - No mention of fixed-point arithmetic (Fix64)

---

## 6. SYSTEM ACTIVATION RULES

### Layer 1: Always Active
- ✅ **Never disabled** - runs continuously at low frequency
- ✅ Global scope - covers entire world
- ✅ Logic consistency requires continuous operation

### Layer 2: Conditionally Active
**Activation Conditions (OR):**
- Camera view frustum covers region
- High-Fidelity Hotspot active (even if off-screen)
- Critical event region active

**Deactivation Conditions:**
- All activation conditions become false

**Constraints:**
- Controlled by `SimulationLODManager`
- Can enable/disable entire `DetailedPhysics2DGroup`
- Performance budget: max entities per region

### Layer 3: Event-Driven
- ✅ Only activates when visual effect events occur
- ✅ Only creates entities in camera view
- ✅ No continuous simulation

---

## 7. SPATIAL BOUNDARIES

### Sector-Based Management
- ✅ World divided into `Sector` units (e.g., `1km x 1km`)
- ✅ Each Sector has state: `LogicOnly` or `DetailedActive`
- ✅ `SimulationLODManager` monitors Sector states
- ✅ State transitions trigger materialization/dematerialization

**Constraints:**
- Sector boundaries define activation regions
- State changes propagate to all entities in Sector

---

## 8. COMPONENT DESIGN RULES

### Tag Components
- ✅ `LogicProxyTag`: Marks Layer 1 entities
- ✅ `DetailedBody2DTag`: Marks Layer 2 entities
- ✅ `VisualEffectTag3D`: Marks Layer 3 entities
- ✅ `InDetailedModeTag`: Temporary tag indicating Proxy is materialized

**Constraints:**
- Tags used for efficient ECS queries
- `InDetailedModeTag` prevents double-processing of Proxy entities

### Component Queries
**Layer 1 System:**
- Query: `[LogicProxyTag] & ! [InDetailedModeTag]`
- Excludes materialized proxies

**Layer 2 System:**
- Query: `[DetailedBody2DTag]`
- Only processes detailed physics entities

---

## 9. EVENT SYSTEM RULES

### Core Events
1. **`SectorStateChangedEvent`**
   - Publisher: `SimulationLODManager`
   - Content: `SectorID`, `NewState`
   - Subscribers: Rendering, Audio systems

2. **`BodyMaterializedEvent`**
   - Publisher: `SimulationLODManager`
   - Content: `SourceProxyEntity`, `MaterializedBodyEntities[]`
   - Subscribers: Rendering, AI/Business systems

3. **`OnDetailedBodyDestroyedEvent`**
   - Publisher: Layer 2 business logic systems
   - Content: `DestroyedBodyEntity`, `Position`, `ImpactForce`
   - Subscribers: Layer 3 visual effects, `SimulationLODManager`

4. **`ProximityEvent`**
   - Publisher: `LogicSimulationSystem`
   - Content: `EntityA`, `EntityB`, `ProximityType.Entered`
   - Subscribers: Business systems (combat, resource gathering)

**Constraints:**
- Events decouple systems
- Physical systems detect proximity, business systems handle logic
- Layer 2 → Layer 3 communication via events only

---

## 10. CRITICAL ARCHITECTURAL CONSTRAINTS

### Separation of Concerns
- ✅ **Logic never compromises**: Layer 1 ensures global consistency
- ✅ **Presentation on demand**: Layer 2 only activates when needed
- ✅ **Visual effects isolated**: Layer 3 does not affect logic

### State Consistency
- ✅ Proxy state suspended (not deleted) during materialization
- ✅ State aggregation required during dematerialization
- ✅ Position conversion is lossy (float → int) but acceptable for macro-scale

### Performance Constraints
- ✅ Layer 1: Low frequency (2 Hz) for performance
- ✅ Layer 2: High frequency (20 Hz) for stability
- ✅ Layer 2: Entity limit per region (e.g., 10,000)
- ✅ Layer 2: Can be completely disabled when not needed

### Data Integrity
- ✅ `InDetailedModeTag` prevents double-processing
- ✅ Materialization preserves Proxy entity
- ✅ Dematerialization updates Proxy with aggregated state
- ✅ Count may decrease during detailed simulation (entities destroyed)

---

## SUMMARY TABLE

| Aspect | Layer 1 (Logic) | Layer 2 (Detailed 2D) | Layer 3 (Visual 3D) |
|--------|----------------|----------------------|---------------------|
| **Coordinate Type** | Integer (int2) | Floating-point (float) | Floating-point (float) |
| **Precision** | Meter-level (1m grid) | Centimeter-level | Variable |
| **Update Frequency** | 2 Hz (low) | 20 Hz (high) | Event-driven |
| **Scope** | Global (always) | Local (conditional) | Camera view only |
| **Determinism** | High (integer ops) | Medium (float ops) | Not required |
| **Spatial Structure** | Integer grid | Continuous space | Continuous space |
| **Activation** | Always active | Conditional | Event-driven |
| **Entity Type** | Proxy (aggregated) | Individual Bodies | Visual effects |
