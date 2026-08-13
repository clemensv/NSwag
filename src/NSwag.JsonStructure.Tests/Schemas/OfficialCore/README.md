# Official Core conformance corpus

The `imported` schemas and instance examples are copied from
`C:\git\json-structure\primer-and-samples\samples\core` (the official Core examples).
`positive/core-v0-all-types.struct.json` is a focused conformance fixture derived directly
from `C:\git\json-structure\meta\core\v0\index.json`; its assertions cover every primitive
enum member and every `CompoundType` choice.

The negative fixtures encode normative Core failures that must be rejected by the parser or
validator. NSwag currently does not include an instance validator, so imported instance files
are retained as source coverage and are not interpreted by these tests.

Known imported-source gaps: the official sample set contains 12 schemas and 34 instance
examples, but some samples use the historical
`https://json-structure.org/meta/core/v0/` URI (without the normative `#` fragment),
reserved-keyword annotations, or type names outside the current Core v0 index. Those source
files are retained for auditability and are not silently treated as passing conformance cases.
