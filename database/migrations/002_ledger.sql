-- Applied by the runner after successful transactional execution; ledger is the ownership fence.
COMMENT ON TABLE migration.ledger IS 'Forward-only migration ledger; one runner owns a version, checksum prevents drift.';
