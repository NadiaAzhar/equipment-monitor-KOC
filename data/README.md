# Data dictionary

All files are synthetic and were generated for this hackathon. They contain no real sites, equipment or operating data. The site names are fictional, and the limits are illustrative.

| File | Rows | One row per |
|---|---:|---|
| [`sensor_readings.csv`](sensor_readings.csv) | 51,866 | Sensor reading, 14 October 2026 (UTC) |
| [`equipment.csv`](equipment.csv) | 12 | Pump or compressor |
| [`sensor_limits.csv`](sensor_limits.csv) | 36 | Sensor on a pump or compressor |

Every file is a UTF-8, comma-separated CSV with a header row.

## sensor_readings.csv

Each of the 36 sensors sends one reading a minute, on the minute, from 00:00 to 23:59 UTC.

| Column | Type | Example | Notes |
|---|---|---|---|
| `timestamp` | ISO 8601 date and time in UTC | `2026-10-14T06:15:00Z` | When the reading was taken. See [Things to know](#things-to-know). |
| `equipment_id` | Text | `K-201` | The machine. Joins to `equipment.csv`. |
| `sensor` | Text | `VIBRATION` | `VIBRATION`, `TEMPERATURE` or `PRESSURE`. With `equipment_id`, joins to `sensor_limits.csv`. |
| `value` | Decimal | `3.441` | The reading, in `unit`. |
| `unit` | Text | `mm/s` | `mm/s`, `degC`, `bar` or `psi`. Compare it with the unit of the sensor's limits. |
| `quality` | `GOOD` or `BAD` | `GOOD` | The quality flag from the SCADA system. A `BAD` reading isn't a trustworthy measurement. |

## equipment.csv

| Column | Type | Example | Notes |
|---|---|---|---|
| `equipment_id` | Text | `P-101` | Unique key. `P-` is a pump and `K-` is a compressor. |
| `equipment_type` | Text | `PUMP` | `PUMP` or `COMPRESSOR` |
| `description` | Text | `Crude transfer pump` | What the machine does. |
| `site_id` | Text | `GC-01` | `GC-01`, `GC-02` or `BS-01` |
| `site_name` | Text | `Gathering Centre 01` | Fictional. |
| `criticality` | Text | `A` | `A`, `B` or `C`. `A` machines matter most to production and safety. Useful for sorting the dashboard. |

## sensor_limits.csv

| Column | Type | Example | Notes |
|---|---|---|---|
| `equipment_id` | Text | `P-103` | Joins to `equipment.csv`. |
| `sensor` | Text | `TEMPERATURE` | `VIBRATION`, `TEMPERATURE` or `PRESSURE` |
| `measures` | Text | `Bearing temperature` | What the sensor measures. |
| `unit` | Text | `degC` | The unit of the three limits. |
| `warning_high` | Decimal | `85.0` | The limit for `HIGH_WARNING`. |
| `alarm_high` | Decimal | `95.0` | The limit for `HIGH_ALARM`. |
| `rolling_avg_high` | Decimal | `80.0` | The limit for `ROLLING_AVERAGE_HIGH`. |

The limits are suggested defaults for this hackathon. They're illustrative, not from a standard, a manufacturer or a real facility. In a real facility, the equipment owner sets the limits, and changes go through a management of change process.

## Things to know

Real SCADA feeds are messy, so this one is too. The readings file deliberately contains a small number of:

- **BAD readings:** some readings have quality `BAD` and a fault value such as 999.9, which isn't a measurement.
- **Gaps:** some sensors send no readings for a while, for example when communications are lost.
- **Late-arriving readings:** a backfill sent after a communications outage was added at the end of the file, out of time order.
- **Duplicate readings:** some readings were sent twice, as exact copies.
- **A different unit:** one sensor reports in a different unit from its limits. 1 bar = 14.5038 psi.
- **UTC timestamps:** every timestamp is in UTC (ending in `Z`), but operators expect local time. Kuwait time is UTC+03:00, with no daylight saving time, so the file covers 03:00 on 14 October to 02:59 on 15 October in Kuwait time.

These issues can change which alerts you raise. Decide how your app handles each one, record the decision in your plan and test it.

Compare readings with limits at full precision. Rounding first can move a reading across a limit: 7.06 mm/s rounded to one decimal place is 7.1 mm/s.
