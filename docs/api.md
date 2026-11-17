# API contract: visualisation endpoints

Conventions are in `.github/copilot-instructions.md`: JSON in snake_case, lists as `{"items","total","limit","offset"}`, times in ISO 8601 with `+03:00`, errors as `{"error":{"code","message","details"}}`.

### GET /api/equipment/{equipment_id}/readings

Returns a sensor's readings for the trend chart, grouped into fixed time buckets, ordered by sensor then time.

- **Query:** `sensor` (optional, `VIBRATION`, `TEMPERATURE` or `PRESSURE`), `from` and `to` (optional ISO 8601, `to` is exclusive), `bucket_minutes` (1–120, default 1), `limit` (1–5000, default 1500), `offset` (default 0).
- **Response 200:** `{"items":[{"timestamp":"2026-10-14T06:15:00+03:00","sensor":"PRESSURE","unit":"bar","value":12.31,"min":12.2,"max":12.4,"good_count":5,"bad_count":0}],"total":288,"limit":1500,"offset":0}`
  - `value`, `min` and `max` use `GOOD` readings only, converted to the unit of the sensor's limits (`unit`). They are `null` when a bucket has no `GOOD` reading, for example only `BAD` readings. Buckets with no readings at all are omitted, so gaps stay visible.
- **Errors:** `404 EQUIPMENT_NOT_FOUND` for an unknown id; `400 VALIDATION_FAILED` for an unknown sensor, an unparsable or reversed time range, or an out-of-range `bucket_minutes`, `limit` or `offset`.

### GET /api/alerts/summary

Returns alert counts per equipment per local hour, for the alerts-per-hour strip and the heatmap. Hours with no alerts are omitted.

- **Query:** `site_id` (optional)
- **Response 200:** `{"items":[{"equipment_id":"P-103","site_id":"GC-01","hour":"2026-10-14T09:00:00+03:00","alarm_count":4,"warning_count":12}],"total":1,"limit":1,"offset":0}`
- **Errors:** `400 VALIDATION_FAILED` for an unknown `site_id`
