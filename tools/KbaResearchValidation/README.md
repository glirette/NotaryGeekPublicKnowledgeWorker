# Isolated KBA research validation

Run `python3 tools/KbaResearchValidation/validate.py` from the repository.

This checks JSON/reference integrity, source-role separation for legal claims,
required hostile and affirmative semantic cases, empty empirical measurements,
draft-only discovery status, local Markdown links and whitespace. Six in-memory
negative controls corrupt these boundaries and must be rejected.

The examples are proposed answer-quality regressions, not a runtime evaluator or
live model benchmark. No actual provider behavior, identity-method performance,
legal outcome or live model answer is tested.

For the publication screen, use the unchanged checker from public PR #39 at
`5deafd015029752b8619e9f9e7cae55151516f18`, invoking
`check_text(path, text)` over only the changed files. It recognizes decoded JSON
keys/string values and case-insensitive JSON extensions. Manually review the
whole delta and public PR text too; this heuristic is not a source-truth or
complete secrets checker. No shared checker or discovery file is changed.

Run `git diff --check`. Refresh mutable sources before customer publication.
Full application build is omitted for isolated docs/data and standalone Python
validation; no runtime, configuration or registered case is changed.
