# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `50.2607 ms`
- per entity: `0.001675 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `3.0920 ms`
- per entity: `0.000103 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `21.9416 ms`
- per entity: `0.000731 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `13.8645 ms`
- per entity: `0.000462 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `344.3919 ms`
- per owner: `0.011480 ms`

## Delta

- saved by bulk allocation before component writes: `47.1687 ms`
- component write cost after bulk allocation: `18.8496 ms`
- saved by shared payload bulk create vs naive per-entity create: `36.3962 ms`
- presenter creation only, after owners already exist: `344.3919 ms`
