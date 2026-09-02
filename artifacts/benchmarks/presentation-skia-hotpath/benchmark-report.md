# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `2.296 ms`
- p95 total: `2.878 ms`
- max total: `3.162 ms`
- avg build: `0.001 ms`
- avg render: `0.000 ms`
- avg fps: `435.5`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `16.583 ms`
- p95 total: `18.104 ms`
- max total: `21.649 ms`
- avg build: `0.168 ms`
- avg render: `13.552 ms`
- avg fps: `60.3`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn

- avg total: `24.252 ms`
- p95 total: `27.067 ms`
- max total: `33.879 ms`
- avg build: `3.588 ms`
- avg render: `17.850 ms`
- avg fps: `41.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `5.755 ms`
- p95 total: `6.478 ms`
- max total: `8.432 ms`
- avg build: `1.073 ms`
- avg render: `3.436 ms`
- avg fps: `173.8`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## value_churn_text_only

- avg total: `16.549 ms`
- p95 total: `18.562 ms`
- max total: `21.929 ms`
- avg build: `2.287 ms`
- avg render: `12.841 ms`
- avg fps: `60.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## camera_pan_bars_only

- avg total: `3.165 ms`
- p95 total: `3.999 ms`
- max total: `4.613 ms`
- avg build: `0.081 ms`
- avg render: `1.901 ms`
- avg fps: `315.9`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `13.379 ms`
- p95 total: `16.002 ms`
- max total: `17.886 ms`
- avg build: `0.094 ms`
- avg render: `11.700 ms`
- avg fps: `74.7`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

