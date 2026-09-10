# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `9.7431 ms`
- per entity: `0.000325 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `6.0912 ms`
- per entity: `0.000203 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `10.5769 ms`
- per entity: `0.000353 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `16.2683 ms`
- per entity: `0.000542 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `210.6182 ms`
- per owner: `0.007021 ms`

## Delta

- saved by bulk allocation before component writes: `3.6519 ms`
- component write cost after bulk allocation: `4.4857 ms`
- saved by shared payload bulk create vs naive per-entity create: `-6.5252 ms`
- presenter creation only, after owners already exist: `210.6182 ms`
