# Varying fields

A varying field is a field of a message's JSON body that gets a new value each time it is sent, such as
a timestamp or a reading. Everything else in the body is sent as written, and the file itself does not
change.

Add them with **Add field** in the [message's settings](help:mqtt-message-settings). They apply to
**Send now** as well as to scheduled sends. **Preview** shows the result.

## Naming a field

A field is named by its path from the top of the body, and must already be there. Its value in the file
is only a placeholder.

| Path | Changes |
| --- | --- |
| `ts` | A field at the top |
| `sensor.value` | A field inside another |
| `readings[0].value` | The `value` of the first reading only |
| `readings[*].ts` | The `ts` of every reading |
| `readings[*]` | Every item of `readings` itself |
| `lines[*].points[*]` | Every item of every `points` in `lines` |
| `[*].ts` | The `ts` of every item, when the body is an array |

## Types

| Type | Value sent | File name |
| --- | --- | --- |
| Unix time (seconds) | The current time, as a number | `unixSeconds` |
| Unix time (milliseconds) | The current time, as a number | `unixMilliseconds` |
| ISO timestamp | The current time in UTC, as text | `isoTimestamp` |
| GUID | A new random GUID | `guid` |
| Counter | **Start**, then up by **Step** each send | `counter` |
| Random integer | A whole number, **Minimum** to **Maximum** | `randomInteger` |
| Random number | The same, rounded to **Decimals** places | `randomNumber` |
| One of, at random | One of the listed values | `oneOf` |
| Each in turn | The listed values in turn | `cycle` |
| Fixed value | The same value every time | `fixed` |

The last column is the type's name when the settings file is edited by hand.

## How values are given out

- All time-based fields in one message use the same instant, taken at the moment of sending.
- With `[*]`, each item gets a value of its own: a counter starting at 1 gives `1`, `2`, `3` to three
  items, and `4`, `5`, `6` on the next send. Time-based types give every item the same instant.
- A counter, or the place in an **Each in turn** list, starts again when the message's schedule does.
- Rules apply in the order listed, so `readings[*].value` followed by `readings[0].value` gives the
  first item its own rule and the rest a shared one.
- An item without the field is left alone. A rule is invalid only if it reaches no item at all, such as
  when the array is empty in the file.

## Fixed values

**Fixed value** overrides the value in the file without changing the file. Its **Type** starts as the type
of the value in the body:

- **Text**, **Number** or **JSON** take a box to type it in.
- **True or false** takes a switch, and **Null** nothing.
- **From a list** takes the allowed values, and a dropdown to pick one from.

## How the body is sent

A message with no varying fields and no array sizes is sent exactly as the file is written. One with
either is sent as compact JSON, without the file's line breaks and indentation.

A value that must stay the same across sends, such as an ID, is simply written in the file.
