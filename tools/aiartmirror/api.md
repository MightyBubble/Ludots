# AI ArtMirror GPT Image 2 API

Source: https://www.aiartmirror.com/docs/gpt-image-2 and its docs API.

## Base

- Base URL: `https://www.aiartmirror.com/v1`
- Auth: `Authorization: Bearer <YOUR_TOKEN>`
- Model: `gpt-image-2`
- Timeout: at least 120 seconds.

## Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/v1/models` | GET | List models; free according to docs |
| `/v1/images/generations` | POST | Text to image |
| `/v1/images/edits` | POST | Reference image edit/style transfer |

## Text To Image

`POST /v1/images/generations` with `application/json`.

| Field | Required | Values |
|---|---:|---|
| `model` | yes | `gpt-image-2` |
| `prompt` | yes | non-empty string |
| `n` | no | integer 1-10, default 1 |
| `size` | no | `auto`, `1024x1024`, `1024x1536`, `1536x1024` |
| `quality` | no | `auto`, `low`, `medium`, `high` |

The response uses OpenAI Images style:

```json
{
  "created": 1777407264,
  "data": [{ "b64_json": "..." }],
  "model": "gpt-image-2",
  "usage": {}
}
```

## Reference Image Edit

`POST /v1/images/edits` with `multipart/form-data`.

| Field | Required | Values |
|---|---:|---|
| `image` | yes | local reference image file, PNG recommended |
| `model` | yes | `gpt-image-2` |
| `prompt` | yes | edit instruction |
| `n` | no | integer 1-10 |
| `size` | no | same as generations |
| `quality` | no | same as generations |

## Billing Notes

- Successful HTTP 200 `generations` and `edits` calls are billable.
- `4xx` failures and `503 model_not_found` are documented as non-billable.
- `n > 1` is billed by successfully returned image count and increases latency.

## Error Policy

| Condition | HTTP | Code / Hint | Action |
|---|---:|---|---|
| Invalid token | 401 | `Invalid token` | Do not retry; ask for a valid key |
| Empty prompt | 400 | `empty string` | Fix prompt |
| Wrong model | 503 | `model_not_found` | Use `gpt-image-2` |
| Bad size | 422 | `bad_size` | Use documented size |
| Malformed JSON | 400 | `invalid character` | Fix request body |

For other `5xx`, retry at most 1-2 times with backoff.
