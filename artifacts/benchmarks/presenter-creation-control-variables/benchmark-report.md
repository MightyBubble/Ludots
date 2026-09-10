# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `6.5422 ms`
- per entity: `0.000218 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `5.1785 ms`
- per entity: `0.000173 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `7.5879 ms`
- per entity: `0.000253 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `13.1009 ms`
- per entity: `0.000437 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `115.7946 ms`
- per owner: `0.003860 ms`

## Delta

- saved by bulk allocation before component writes: `1.3637 ms`
- component write cost after bulk allocation: `2.4094 ms`
- saved by shared payload bulk create vs naive per-entity create: `-6.5587 ms`
- presenter creation only, after owners already exist: `115.7946 ms`
