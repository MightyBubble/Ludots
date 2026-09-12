# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `4.095 ms`
- p95 total: `6.460 ms`
- max total: `6.679 ms`
- avg build: `0.001 ms`
- avg render: `0.000 ms`
- avg fps: `244.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `31.174 ms`
- p95 total: `38.566 ms`
- max total: `39.676 ms`
- avg build: `0.369 ms`
- avg render: `25.695 ms`
- avg fps: `32.1`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn

- avg total: `40.770 ms`
- p95 total: `53.181 ms`
- max total: `56.567 ms`
- avg build: `6.255 ms`
- avg render: `30.028 ms`
- avg fps: `24.5`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `13.307 ms`
- p95 total: `19.524 ms`
- max total: `21.164 ms`
- avg build: `2.723 ms`
- avg render: `8.069 ms`
- avg fps: `75.1`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_text_only

- avg total: `28.433 ms`
- p95 total: `33.398 ms`
- max total: `36.911 ms`
- avg build: `3.565 ms`
- avg render: `22.580 ms`
- avg fps: `35.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## camera_pan_bars_only

- avg total: `6.127 ms`
- p95 total: `7.509 ms`
- max total: `7.863 ms`
- avg build: `0.181 ms`
- avg render: `3.714 ms`
- avg fps: `163.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `23.920 ms`
- p95 total: `28.829 ms`
- max total: `29.652 ms`
- avg build: `0.173 ms`
- avg render: `21.059 ms`
- avg fps: `41.8`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

