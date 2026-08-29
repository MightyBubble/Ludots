# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `79.5626 ms`
- per entity: `0.002652 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `4.0979 ms`
- per entity: `0.000137 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `32.4915 ms`
- per entity: `0.001083 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `22.7924 ms`
- per entity: `0.000760 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `543.6557 ms`
- per owner: `0.018122 ms`

## Delta

- saved by bulk allocation before component writes: `75.4647 ms`
- component write cost after bulk allocation: `28.3936 ms`
- saved by shared payload bulk create vs naive per-entity create: `56.7702 ms`
- presenter creation only, after owners already exist: `543.6557 ms`
