# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `1.354 ms`
- p95 total: `1.549 ms`
- max total: `1.686 ms`
- avg build: `0.001 ms`
- avg render: `0.000 ms`
- avg fps: `738.3`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `28.059 ms`
- p95 total: `29.912 ms`
- max total: `40.469 ms`
- avg build: `0.177 ms`
- avg render: `26.105 ms`
- avg fps: `35.6`
- alloc per frame: `0.4 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn

- avg total: `16.325 ms`
- p95 total: `18.240 ms`
- max total: `28.112 ms`
- avg build: `3.176 ms`
- avg render: `11.619 ms`
- avg fps: `61.3`
- alloc per frame: `640.8 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `6.188 ms`
- p95 total: `6.722 ms`
- max total: `7.252 ms`
- avg build: `1.030 ms`
- avg render: `4.654 ms`
- avg fps: `161.6`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## value_churn_text_only

- avg total: `8.616 ms`
- p95 total: `9.178 ms`
- max total: `13.833 ms`
- avg build: `1.714 ms`
- avg render: `6.017 ms`
- avg fps: `116.1`
- alloc per frame: `640.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## camera_pan_bars_only

- avg total: `4.015 ms`
- p95 total: `4.298 ms`
- max total: `5.878 ms`
- avg build: `0.038 ms`
- avg render: `3.592 ms`
- avg fps: `249.1`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `23.203 ms`
- p95 total: `23.758 ms`
- max total: `24.034 ms`
- avg build: `0.052 ms`
- avg render: `22.094 ms`
- avg fps: `43.1`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

