# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `42.4143 ms`
- per entity: `0.001414 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `6.4686 ms`
- per entity: `0.000216 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `34.1997 ms`
- per entity: `0.001140 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `26.1438 ms`
- per entity: `0.000871 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `556.9039 ms`
- per owner: `0.018563 ms`

## Delta

- saved by bulk allocation before component writes: `35.9457 ms`
- component write cost after bulk allocation: `27.7311 ms`
- saved by shared payload bulk create vs naive per-entity create: `16.2705 ms`
- presenter creation only, after owners already exist: `556.9039 ms`
