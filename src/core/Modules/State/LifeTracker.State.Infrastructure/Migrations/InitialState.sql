-- R1 subset of the normative schema-v1.sql. Remaining tables arrive with their modules.
CREATE TABLE profiles (
 id TEXT PRIMARY KEY, timezone TEXT NOT NULL DEFAULT 'Europe/Moscow',
 active INTEGER NOT NULL DEFAULT 1 CHECK(active IN(0,1)),
 settings_json TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(settings_json))
) STRICT;
CREATE TABLE entities (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL,
 kind TEXT NOT NULL, current_revision INTEGER NOT NULL CHECK(current_revision>0),
 PRIMARY KEY(profile_id,id),
 FOREIGN KEY(profile_id,id,current_revision) REFERENCES revisions(profile_id,entity_id,revision)
 DEFERRABLE INITIALLY DEFERRED
) STRICT;
CREATE TABLE revisions (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>0),
 recorded_ms INTEGER NOT NULL CHECK(recorded_ms>=0), actor TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status IN('pending','accepted','rejected','stale','superseded','deleted')),
 reason TEXT, body_json TEXT NOT NULL CHECK(json_valid(body_json)),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id) REFERENCES entities(profile_id,id) DEFERRABLE INITIALLY DEFERRED
) STRICT;
CREATE TABLE source_events (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, source TEXT NOT NULL,
 external_id TEXT NOT NULL, payload_hash TEXT NOT NULL, received_ms INTEGER NOT NULL,
 state TEXT NOT NULL CHECK(state IN('accepted','processing','completed','failed')),
 receipt_json TEXT NOT NULL CHECK(json_valid(receipt_json)),
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,source,external_id)
) STRICT;
CREATE TABLE observations (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 metric TEXT NOT NULL CHECK(metric IN('energy','mood','anxiety','physical_wellbeing')),
 score_half INTEGER NOT NULL CHECK(score_half BETWEEN 0 AND 20), observed_ms INTEGER NOT NULL,
 source_kind TEXT NOT NULL CHECK(source_kind IN('self_report','ai_estimate')),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE UNIQUE INDEX one_active_profile ON profiles(active) WHERE active=1;
CREATE INDEX observations_selection ON observations(profile_id,metric,source_kind,observed_ms DESC);
PRAGMA user_version=1;
