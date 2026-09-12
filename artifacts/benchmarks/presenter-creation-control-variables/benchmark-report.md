# Presenter Creation Control Variables

- sample size: `30000` entities
- excludes: mesh emit, HUD projection, culling, skia, raylib
- goal: isolate pure creation cost before touching render-path optimization

## 30K Entity Only - Naive Per Entity Create

- created entities: `30000`
- total create time: `33.6189 ms`
- per entity: `0.001121 ms`

## 30K Entity Only - Bulk Allocate Only

- created entities: `30000`
- total create time: `9.1635 ms`
- per entity: `0.000305 ms`

## 30K Entity Only - Bulk Allocate + Component Set

- created entities: `30000`
- total create time: `37.6983 ms`
- per entity: `0.001257 ms`

## 30K Entity Only - Bulk Create With Shared Payload

- created entities: `30000`
- total create time: `21.4210 ms`
- per entity: `0.000714 ms`
- payload path uses Arch generated `Create<T0..Tn>(amount, ...)` overloads

## 30K Entity + Presenter (No Mesh)

- owners are created with the bulk allocate + component set path before timing starts
- created owners: `30000`
- created presenters: `30000`
- presenter active count: `30000`
- total create time: `557.6980 ms`
- per owner: `0.018590 ms`

## Delta

- saved by bulk allocation before component writes: `24.4554 ms`
- component write cost after bulk allocation: `28.5348 ms`
- saved by shared payload bulk create vs naive per-entity create: `12.1979 ms`
- presenter creation only, after owners already exist: `557.6980 ms`
