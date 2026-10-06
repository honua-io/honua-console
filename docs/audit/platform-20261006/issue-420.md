# Issue 420 audit record

| Finding id | Outcome | Evidence |
|---|---|---|
| CON-001 | fixed | `CON_001_AliasUpdate_PreservesExistingDomain` proves the alias/visibility update reads and carries the existing domain; `CON_007_NumericCodedValues_RoundTripAsNumbers` also proves a domain update carries the existing alias. The operation now fails without issuing a PUT when the prerequisite GET cannot supply the target field. |
| CON-006 | fixed | `CON_006_DefaultValueUpdate_FailsClosedWithoutPut` and `CON_006_ClearDefaultValue_FailsClosedWithoutPut` prove both unsupported default-value intents return `Unsupported` and issue no PUT rather than reporting a false success. Observable behavior changes from a success-shaped response to an explicit unsupported result. |
| CON-007 | fixed | `CON_007_NumericCodedValues_RoundTripAsNumbers` proves a numeric JSON code can be read into editor text and is emitted again as a JSON number for a numeric field. `HonuaAdminCodedValue.Code` now matches the server's scalar JSON contract, while invalid numeric codes fail locally. |
