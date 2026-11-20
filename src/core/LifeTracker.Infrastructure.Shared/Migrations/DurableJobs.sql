CREATE TABLE jobs (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, kind TEXT NOT NULL,
 idempotency_key TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN('queued','running','retry_wait','completed','failed','cancelled')),
 ready_ms INTEGER NOT NULL, attempts INTEGER NOT NULL DEFAULT 0 CHECK(attempts>=0),
 lease_owner TEXT, lease_until_ms INTEGER, fence INTEGER NOT NULL DEFAULT 0 CHECK(fence>=0),
 input_json TEXT NOT NULL CHECK(json_valid(input_json)), result_json TEXT CHECK(result_json IS NULL OR json_valid(result_json)),
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,kind,idempotency_key),
 CHECK(state<>'running' OR (lease_owner IS NOT NULL AND lease_until_ms IS NOT NULL))
) STRICT;
CREATE TABLE outbox (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, reason_key TEXT NOT NULL,
 topic TEXT NOT NULL, payload_json TEXT NOT NULL CHECK(json_valid(payload_json)),
 created_ms INTEGER NOT NULL, dispatched_ms INTEGER,
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,topic,reason_key)
) STRICT;
CREATE INDEX jobs_due ON jobs(state,ready_ms);
