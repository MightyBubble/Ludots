# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `20.2754 ms`
- per entity: `0.000676 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `2.9292 ms`
- per entity: `0.000098 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `20.2636 ms`
- per entity: `0.000675 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `15.3685 ms`
- per entity: `0.000512 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `298.3894 ms`
- per owner: `0.009946 ms`

## Delta

- saved by bulk allocation before component writes: `17.3462 ms`
- component write cost after bulk allocation: `17.3344 ms`
- saved by shared payload bulk create vs naive per-entity create: `4.9069 ms`
- presenter creation only, after owners already exist: `298.3894 ms`
