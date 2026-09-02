# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `3.027 ms`
- p95 total: `3.794 ms`
- max total: `5.899 ms`
- avg build: `0.002 ms`
- avg render: `0.000 ms`
- avg fps: `330.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `19.176 ms`
- p95 total: `25.137 ms`
- max total: `30.073 ms`
- avg build: `0.209 ms`
- avg render: `15.595 ms`
- avg fps: `52.1`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn

- avg total: `28.243 ms`
- p95 total: `34.981 ms`
- max total: `38.526 ms`
- avg build: `4.331 ms`
- avg render: `20.752 ms`
- avg fps: `35.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `7.864 ms`
- p95 total: `13.025 ms`
- max total: `14.369 ms`
- avg build: `1.554 ms`
- avg render: `4.800 ms`
- avg fps: `127.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_text_only

- avg total: `19.464 ms`
- p95 total: `25.033 ms`
- max total: `30.560 ms`
- avg build: `2.842 ms`
- avg render: `15.006 ms`
- avg fps: `51.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## camera_pan_bars_only

- avg total: `3.161 ms`
- p95 total: `3.959 ms`
- max total: `4.836 ms`
- avg build: `0.084 ms`
- avg render: `1.888 ms`
- avg fps: `316.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `12.254 ms`
- p95 total: `13.077 ms`
- max total: `15.624 ms`
- avg build: `0.088 ms`
- avg render: `10.735 ms`
- avg fps: `81.6`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

