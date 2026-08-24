# Narrative Slices Acceptance — MUD Battle Report

- scenario: activity_execute_condition
- build: headless GameEngine + trigger pipeline
- map: narrative_slices_hub (seed: fixed content, no rng)
- clock: fixed 0.0167s per tick
- executed: 2026-08-25 01:21:38

## Timeline

- [T+001] [map] hub loaded; default camera 'Camera.Profile.Tactical' active
- [T+002] [activity] forced activity Slice.Execute offered through the slice conductor
- [T+003] [activity] opt_go Executable=true with empty BlockReason; opt_wait is the baseline
- [T+004] [activity] resolving executable opt_go ran its task.create effect
- [T+005] [activity] baseline opt_wait resolved the second instance without effects
- [T+006] [activity] full path attempt: execute_condition with an unregistered condition key fails provider validation at load (fail-fast, no fallback)

## Open issues

- execute_condition 在内容侧没有 condition provider 注册途径（引擎缺口，如实暴露，未绕过）：
  activities.json 由 ActivityConfigLoader 在 GameEngine.InitializeWithConfigPipeline 内加载并做 provider 键校验；
  彼时 ProviderServices 仅含 TaskBridgeProviderInstaller.Install 注册的 task.state_changed(source) 与 task.create(effect)，
  condition 注册表为空。生产初始化路径没有任何 condition provider 注册点：
  FixtureProviderInstaller.InstallMinimal（fixture.always_true）只被测试工程引用，
  ProviderGapCatalog.RegisterFrameworkGaps 也只声明 task.create / task.state_changed 两条框架缺口。
  mod 的 GameStart 订阅在 engine.Start() 才触发，晚于配置加载，注册来不及。
  本方法在生产同构的空 ProviderServices 上复现 ValidateAndThrow 对
  execute_condition "task.counter_below" 的 fail-fast（unknown_provider_key）。
  另注：ProviderKey 的域白名单不含内容自定义域（如 slice），内容侧即使声明 slice.counter_below
  也会先撞 provider_domain_not_allowed。
  需要引擎提供初始化期的 condition provider 注册途径（内置条件族或声明式条件），
  之后 opt_go 的 execute_condition 才能真正接入内容。

## Outcome

- PASS: slice 'activity_execute_condition' completed with all anchors observed.

