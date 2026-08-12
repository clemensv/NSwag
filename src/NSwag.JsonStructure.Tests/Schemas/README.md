# JSON Structure test corpus

These schemas are ported from `avrotize/test/struct` (the `s2cs` / `s2ts` generator
test suite) and serve as the conformance corpus for `JsonStructureParser`.

## Normalization applied on import

avrotize's corpus predates parts of the JSON Structure core draft and uses a few
Avro-flavoured type aliases that are **not** in the specification's type name
registry (core draft, "JSON Primitive Types" / "Extended Primitive Types"). The
parser is deliberately strict — an unknown type name is an error, not a silent
`any` — so the fixtures were normalized to the canonical spec names on import:

| avrotize name | canonical JSON Structure name |
| --- | --- |
| `bytes` | `binary` |
| `timestamp` | `datetime` |
| `float32` | `float` |
| `float64` | `double` |

Only `"type": "<alias>"` values were rewritten. Property *names* that happen to
be spelled `timestamp` are untouched.

Nothing else was changed. When refreshing from upstream, re-apply the same
mapping rather than relaxing the parser.
