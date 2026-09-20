# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `0.704 ms`
- p95 total: `0.734 ms`
- max total: `0.926 ms`
- avg build: `0.000 ms`
- avg render: `0.000 ms`
- avg fps: `1421.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `4.653 ms`
- p95 total: `5.499 ms`
- max total: `11.685 ms`
- avg build: `0.062 ms`
- avg render: `4.046 ms`
- avg fps: `214.9`
- alloc per frame: `640.4 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## value_churn

- avg total: `6.906 ms`
- p95 total: `9.830 ms`
- max total: `14.118 ms`
- avg build: `1.571 ms`
- avg render: `4.782 ms`
- avg fps: `144.8`
- alloc per frame: `640.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `2.511 ms`
- p95 total: `2.603 ms`
- max total: `3.315 ms`
- avg build: `0.455 ms`
- avg render: `1.889 ms`
- avg fps: `398.3`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## value_churn_text_only

- avg total: `3.346 ms`
- p95 total: `3.524 ms`
- max total: `4.717 ms`
- avg build: `0.693 ms`
- avg render: `2.387 ms`
- avg fps: `298.9`
- alloc per frame: `640.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_bars_only

- avg total: `1.608 ms`
- p95 total: `1.676 ms`
- max total: `1.716 ms`
- avg build: `0.015 ms`
- avg render: `1.457 ms`
- avg fps: `621.8`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `2.521 ms`
- p95 total: `2.614 ms`
- max total: `2.652 ms`
- avg build: `0.015 ms`
- avg render: `2.311 ms`
- avg fps: `396.7`
- alloc per frame: `640.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

