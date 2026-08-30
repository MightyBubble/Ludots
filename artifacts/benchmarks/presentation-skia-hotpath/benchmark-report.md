# Presentation Skia Hotpath Benchmark

- target: `120 Hz`
- frame budget: `8.33 ms`
- workload: `10240` bars + `10240` text
- viewport: `1280x720`
- measured frames: `120` after warmup

## steady_same_view

- avg total: `3.789 ms`
- p95 total: `5.840 ms`
- max total: `13.002 ms`
- avg build: `0.002 ms`
- avg render: `0.000 ms`
- avg fps: `263.9`
- alloc per frame: `0.0 B`
- avg dirty lanes: `0.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `100.0%`
- 120 Hz pass: `yes`

## camera_pan

- avg total: `24.770 ms`
- p95 total: `37.664 ms`
- max total: `82.206 ms`
- avg build: `0.271 ms`
- avg render: `20.333 ms`
- avg fps: `40.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn

- avg total: `30.793 ms`
- p95 total: `40.294 ms`
- max total: `49.629 ms`
- avg build: `4.965 ms`
- avg render: `22.199 ms`
- avg fps: `32.5`
- alloc per frame: `0.0 B`
- avg dirty lanes: `2.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_bars_only

- avg total: `10.823 ms`
- p95 total: `13.682 ms`
- max total: `36.574 ms`
- avg build: `2.085 ms`
- avg render: `6.815 ms`
- avg fps: `92.4`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## value_churn_text_only

- avg total: `18.804 ms`
- p95 total: `23.464 ms`
- max total: `37.572 ms`
- avg build: `2.832 ms`
- avg render: `14.374 ms`
- avg fps: `53.2`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

## camera_pan_bars_only

- avg total: `3.422 ms`
- p95 total: `4.484 ms`
- max total: `4.805 ms`
- avg build: `0.087 ms`
- avg render: `2.064 ms`
- avg fps: `292.3`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `yes`

## camera_pan_text_only

- avg total: `13.560 ms`
- p95 total: `16.272 ms`
- max total: `18.497 ms`
- avg build: `0.100 ms`
- avg render: `11.828 ms`
- avg fps: `73.7`
- alloc per frame: `0.0 B`
- avg dirty lanes: `1.00`
- avg rebuilt lanes: `0.00`
- composite skip rate: `0.0%`
- 120 Hz pass: `no`

