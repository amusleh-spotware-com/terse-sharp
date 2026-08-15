# Fixture notes

A markdown file whose whole content is a table, so `read_text columns=` has something to project
that `headings=true` cannot narrow. The prose here exists so a whole-file read is measurably more
expensive than the projection, which is what the column budget asserts.

## Open

| Finding | Tool | Proposed change | Expected saving |
|---|---|---|---|
| **F1** first row | read_text | project the table down to the named columns | most of the file |
| **F2** second row | analyze | fold records sharing an id and a message | about 60 % of a wide pass |
| **F3** third row | changed_files | fold an untracked directory into one row | ~400 tokens per first call |

## Closed

| Finding | Tool | Change | Outcome |
|---|---|---|---|
| **F0** older row | build | answer a success in one line | shipped, asserted by the token budget |
