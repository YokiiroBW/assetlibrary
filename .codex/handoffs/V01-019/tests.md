# V01-019 verification checkpoint — 2026-09-07

- `python -I -B -m unittest discover -s tests/release -p test_read_only_trial_package.py -v`: 5 passed, 0 skipped; `ASSETLIBRARY_TEST_TRIAL_POSTGRES_BIN` configured to PostgreSQL16.15 and `ASSETLIBRARY_TEST_TRIAL_REQUIRED=1`.
- Native test: new private temporary PostgreSQL cluster, 14 forward migrations, backup SHA-256 verified, six distinct module LOGIN roles, initialization replay preserves credentials/backup, repeated start, stop/restart, forged process creation time rejected, wrong cluster system identifier rejected.
- `python -I -B scripts/validate_architecture_baseline.py`: passed (131 governed inputs).
- `python -I -B scripts/generate_assetlink_sdks.py --check`: 7 generated files current.
- PowerShell parser passed.

Initial failures: PowerShell test harness argument passing was corrected to a temporary script; Windows pg_ctl inherited pipe handles caused a wait, corrected to no inherited capture pipes for pg_ctl; deployment LOGIN identifiers were changed to the existing migration tool's assetlibrary_ namespace. The owned test PostgreSQL was identity-checked and stopped, then the complete five-test set passed.

Pending: actual same-source Host/Web package build and complete PowerShell initialize/operator/start/status/stop flows after coordinator Host integration. No native evidence is inferred from static checks.
