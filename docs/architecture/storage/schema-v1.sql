-- Life Tracker design schema v1; not an applied application migration.
-- Runtime floor: SQLite >=3.51.3; tests here use in-memory SQL only.
PRAGMA foreign_keys=ON;
CREATE TABLE profiles (
 id TEXT PRIMARY KEY, timezone TEXT NOT NULL DEFAULT 'Europe/Moscow',
 active INTEGER NOT NULL DEFAULT 1 CHECK(active IN(0,1)),
 settings_json TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(settings_json))
) STRICT;
CREATE UNIQUE INDEX one_active_profile ON profiles(active) WHERE active=1;
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
CREATE TABLE media (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL,
 relative_path TEXT NOT NULL CHECK(relative_path NOT LIKE '/%' AND instr(relative_path,'..')=0),
 sha256 TEXT NOT NULL CHECK(length(sha256)=64), bytes INTEGER NOT NULL CHECK(bytes>=0),
 mime TEXT NOT NULL, lifecycle TEXT NOT NULL CHECK(lifecycle IN('durable','deleting','deleted')),
 retention_after_ms INTEGER, pin_count INTEGER NOT NULL DEFAULT 0 CHECK(pin_count>=0),
 PRIMARY KEY(profile_id,id)
) STRICT;
CREATE TABLE evidence (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL,
 episode_id TEXT NOT NULL, source_event_id TEXT, media_id TEXT, excerpt TEXT,
 original_available INTEGER NOT NULL CHECK(original_available IN(0,1)),
 PRIMARY KEY(profile_id,id),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id),
 FOREIGN KEY(profile_id,episode_id) REFERENCES entities(profile_id,id),
 FOREIGN KEY(profile_id,media_id) REFERENCES media(profile_id,id)
) STRICT;
CREATE TABLE revision_evidence (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL, evidence_id TEXT NOT NULL,
 PRIMARY KEY(profile_id,entity_id,revision,evidence_id),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,evidence_id) REFERENCES evidence(profile_id,id)
) STRICT;
CREATE TABLE dependencies (
 profile_id TEXT NOT NULL, dependent_id TEXT NOT NULL, dependent_revision INTEGER NOT NULL,
 basis_id TEXT NOT NULL, basis_revision INTEGER NOT NULL,
 CHECK(dependent_id<>basis_id),
 PRIMARY KEY(profile_id,dependent_id,dependent_revision,basis_id,basis_revision),
 FOREIGN KEY(profile_id,dependent_id,dependent_revision) REFERENCES revisions(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,basis_id,basis_revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE observations (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 metric TEXT NOT NULL CHECK(metric IN('energy','mood','anxiety','physical_wellbeing')),
 score_half INTEGER NOT NULL CHECK(score_half BETWEEN 0 AND 20), observed_ms INTEGER NOT NULL,
 source_kind TEXT NOT NULL CHECK(source_kind IN('self_report','ai_estimate')),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE persons (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 display_name TEXT NOT NULL, aliases_json TEXT NOT NULL DEFAULT '[]' CHECK(json_valid(aliases_json)),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE episodes (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 occurred_ms INTEGER, precision_kind TEXT NOT NULL CHECK(precision_kind IN('instant','day','range','unknown')),
 summary TEXT NOT NULL,
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE conversations (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 title TEXT, opened_ms INTEGER NOT NULL, closed_ms INTEGER,
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE messages (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 conversation_id TEXT NOT NULL, role TEXT NOT NULL CHECK(role IN('owner','assistant')),
 source_event_id TEXT, text_body TEXT, media_id TEXT,
 FOREIGN KEY(profile_id,conversation_id) REFERENCES entities(profile_id,id),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id),
 FOREIGN KEY(profile_id,media_id) REFERENCES media(profile_id,id),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE journal_entries (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 conversation_id TEXT NOT NULL, summary TEXT NOT NULL,
 FOREIGN KEY(profile_id,conversation_id) REFERENCES entities(profile_id,id),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE interpretations (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 summary TEXT NOT NULL, confidence TEXT NOT NULL CHECK(confidence IN('low','medium','high')),
 rule_version TEXT NOT NULL, model_manifest_id TEXT NOT NULL, unique_episode_count INTEGER NOT NULL CHECK(unique_episode_count>=0),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE extractions (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 source_event_id TEXT NOT NULL, acceptance TEXT NOT NULL CHECK(acceptance IN('pending','accepted','rejected')),
 model_manifest_id TEXT NOT NULL, candidates_json TEXT NOT NULL CHECK(json_valid(candidates_json)),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE meals (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 occurred_ms INTEGER NOT NULL, name TEXT NOT NULL, portion_json TEXT NOT NULL CHECK(json_valid(portion_json)),
 nutrients_json TEXT CHECK(nutrients_json IS NULL OR json_valid(nutrients_json)),
 estimate_status TEXT NOT NULL CHECK(estimate_status IN('unknown','estimated','confirmed_portion')),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE quest_templates (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 goal TEXT NOT NULL, rule_version TEXT NOT NULL, attribute TEXT NOT NULL,
 recurrence_json TEXT NOT NULL CHECK(json_valid(recurrence_json)),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE quest_occurrences (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 template_id TEXT NOT NULL, start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL CHECK(end_ms>start_ms),
 load_points INTEGER NOT NULL CHECK(load_points BETWEEN 1 AND 3),
 outcome TEXT NOT NULL CHECK(outcome IN('assigned','active','completed','skipped','unknown','paused','excused','cancelled')),
 optional INTEGER NOT NULL CHECK(optional IN(0,1)), rule_version TEXT NOT NULL,
 FOREIGN KEY(profile_id,template_id) REFERENCES entities(profile_id,id),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE accounts (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 source TEXT NOT NULL, external_id TEXT NOT NULL, currency TEXT NOT NULL CHECK(length(currency)=3),
 kind TEXT NOT NULL CHECK(kind IN('own_liquid','savings','deposit','credit','brokerage','category','external')),
 UNIQUE(profile_id,source,external_id,revision),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE budgets (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 month TEXT NOT NULL CHECK(length(month)=7), currency TEXT NOT NULL CHECK(length(currency)=3),
 approval TEXT NOT NULL CHECK(approval IN('draft','proposed','approved','superseded')),
 expected_minor INTEGER NOT NULL CHECK(expected_minor>=0),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE instruments (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 source TEXT NOT NULL, external_id TEXT NOT NULL, currency TEXT NOT NULL CHECK(length(currency)=3),
 instrument_kind TEXT NOT NULL, metadata_json TEXT NOT NULL CHECK(json_valid(metadata_json)),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE portfolio_snapshots (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 account_id TEXT NOT NULL, observed_ms INTEGER NOT NULL,
 positions_json TEXT NOT NULL CHECK(json_valid(positions_json)), coverage TEXT NOT NULL CHECK(coverage IN('complete','partial','unavailable')),
 FOREIGN KEY(profile_id,account_id) REFERENCES entities(profile_id,id),
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE measurements (
 profile_id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 metric TEXT NOT NULL CHECK(metric IN('sleep','steps','workout','resting_heart_rate','screen_time')),
 start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL CHECK(end_ms>=start_ms),
 value_decimal TEXT, unit TEXT NOT NULL, source_event_id TEXT NOT NULL,
 category TEXT, source_device TEXT,
 PRIMARY KEY(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id)
) STRICT;
CREATE INDEX measurements_range ON measurements(profile_id,metric,start_ms,end_ms);
CREATE INDEX observations_selection ON observations(profile_id,metric,source_kind,observed_ms DESC);
CREATE TABLE person_mentions (
 profile_id TEXT NOT NULL, episode_id TEXT NOT NULL, person_id TEXT NOT NULL,
 PRIMARY KEY(profile_id,episode_id,person_id),
 FOREIGN KEY(profile_id,episode_id) REFERENCES entities(profile_id,id),
 FOREIGN KEY(profile_id,person_id) REFERENCES entities(profile_id,id)
) STRICT;
CREATE TABLE disagreements (
 profile_id TEXT NOT NULL, id TEXT NOT NULL, entity_id TEXT NOT NULL, revision INTEGER NOT NULL,
 note TEXT NOT NULL, recorded_ms INTEGER NOT NULL, PRIMARY KEY(profile_id,id),
 FOREIGN KEY(profile_id,entity_id,revision) REFERENCES revisions(profile_id,entity_id,revision)
) STRICT;
CREATE TABLE jobs (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, kind TEXT NOT NULL,
 idempotency_key TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN('queued','running','retry_wait','completed','failed','cancelled')),
 ready_ms INTEGER NOT NULL, attempts INTEGER NOT NULL DEFAULT 0 CHECK(attempts>=0),
 lease_owner TEXT, lease_until_ms INTEGER, fence INTEGER NOT NULL DEFAULT 0 CHECK(fence>=0),
 input_json TEXT NOT NULL CHECK(json_valid(input_json)), result_json TEXT CHECK(result_json IS NULL OR json_valid(result_json)),
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,kind,idempotency_key),
 CHECK(state<>'running' OR (lease_owner IS NOT NULL AND lease_until_ms IS NOT NULL))
) STRICT;
CREATE INDEX jobs_due ON jobs(state,ready_ms);
CREATE TABLE outbox (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, reason_key TEXT NOT NULL,
 topic TEXT NOT NULL, payload_json TEXT NOT NULL CHECK(json_valid(payload_json)),
 created_ms INTEGER NOT NULL, dispatched_ms INTEGER,
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,topic,reason_key)
) STRICT;
CREATE TABLE integration_state (
 profile_id TEXT NOT NULL REFERENCES profiles(id), provider TEXT NOT NULL,
 capability TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN('unqualified','enabled','unavailable','degraded','disabled')),
 checkpoint TEXT, last_success_ms INTEGER, error_code TEXT,
 PRIMARY KEY(profile_id,provider,capability)
) STRICT;
CREATE TABLE notification_events (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, reason_key TEXT NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN('reminder','finance','reply')), local_day TEXT,
 slot INTEGER CHECK(slot BETWEEN 1 AND 3), consumes_quota INTEGER NOT NULL DEFAULT 1 CHECK(consumes_quota IN(0,1)), expires_ms INTEGER NOT NULL,
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,reason_key),
 CHECK((kind='reminder' AND slot IS NOT NULL AND local_day IS NOT NULL) OR kind<>'reminder')
) STRICT;
CREATE UNIQUE INDEX reminder_quota ON notification_events(profile_id,local_day,slot) WHERE kind='reminder' AND consumes_quota=1;
CREATE TABLE deliveries (
 profile_id TEXT NOT NULL, event_id TEXT NOT NULL, channel TEXT NOT NULL CHECK(channel IN('macos','telegram')),
 state TEXT NOT NULL CHECK(state IN('pending','sending','delivered','ambiguous','failed','expired')),
 attempts INTEGER NOT NULL DEFAULT 0, external_message_id TEXT,
 PRIMARY KEY(profile_id,event_id,channel),
 FOREIGN KEY(profile_id,event_id) REFERENCES notification_events(profile_id,id)
) STRICT;
CREATE TABLE game_effects (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, occurrence_id TEXT NOT NULL,
 outcome_revision INTEGER NOT NULL, attribute TEXT NOT NULL, effect_kind TEXT NOT NULL CHECK(effect_kind IN('award','penalty','reversal')), delta INTEGER NOT NULL, xp_delta INTEGER NOT NULL,
 reversal_of TEXT, rule_version TEXT NOT NULL,
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,occurrence_id,outcome_revision,attribute,effect_kind,rule_version),
 FOREIGN KEY(profile_id,occurrence_id,outcome_revision) REFERENCES revisions(profile_id,entity_id,revision),
 FOREIGN KEY(profile_id,reversal_of) REFERENCES game_effects(profile_id,id),
 CHECK(reversal_of IS NOT NULL OR xp_delta>=0),
 CHECK((effect_kind='reversal' AND reversal_of IS NOT NULL) OR (effect_kind<>'reversal' AND reversal_of IS NULL))
) STRICT;
CREATE UNIQUE INDEX single_reversal ON game_effects(profile_id,reversal_of) WHERE reversal_of IS NOT NULL;
CREATE TABLE ledger_journals (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, source_event_id TEXT NOT NULL,
 kind TEXT NOT NULL, status TEXT NOT NULL CHECK(status IN('draft','posted','reversed')),
 occurred_ms INTEGER NOT NULL, source_revision TEXT NOT NULL,
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,source_event_id,source_revision),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id)
) STRICT;
CREATE TABLE ledger_postings (
 profile_id TEXT NOT NULL, journal_id TEXT NOT NULL, line_no INTEGER NOT NULL,
 account_id TEXT NOT NULL, currency TEXT NOT NULL CHECK(length(currency)=3),
 amount_minor INTEGER NOT NULL CHECK(amount_minor BETWEEN -9000000000000000 AND 9000000000000000),
 PRIMARY KEY(profile_id,journal_id,line_no),
 FOREIGN KEY(profile_id,journal_id) REFERENCES ledger_journals(profile_id,id),
 FOREIGN KEY(profile_id,account_id) REFERENCES entities(profile_id,id)
) STRICT;
CREATE TRIGGER no_posted_line_insert BEFORE INSERT ON ledger_postings
 WHEN (SELECT status FROM ledger_journals WHERE profile_id=NEW.profile_id AND id=NEW.journal_id)<>'draft'
 BEGIN SELECT RAISE(ABORT,'journal is immutable'); END;
CREATE TRIGGER no_posted_line_update BEFORE UPDATE ON ledger_postings
 WHEN (SELECT status FROM ledger_journals WHERE profile_id=OLD.profile_id AND id=OLD.journal_id)<>'draft'
 BEGIN SELECT RAISE(ABORT,'journal is immutable'); END;
CREATE TRIGGER no_posted_line_delete BEFORE DELETE ON ledger_postings
 WHEN (SELECT status FROM ledger_journals WHERE profile_id=OLD.profile_id AND id=OLD.journal_id)<>'draft'
 BEGIN SELECT RAISE(ABORT,'journal is immutable'); END;
CREATE TRIGGER posted_balanced BEFORE UPDATE OF status ON ledger_journals WHEN NEW.status='posted'
 BEGIN
 SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM ledger_postings WHERE profile_id=NEW.profile_id AND journal_id=NEW.id)
 OR EXISTS(SELECT currency FROM ledger_postings WHERE profile_id=NEW.profile_id AND journal_id=NEW.id GROUP BY currency HAVING sum(amount_minor)<>0)
 THEN RAISE(ABORT,'unbalanced journal') END;
 END;
CREATE TRIGGER journals_begin_draft BEFORE INSERT ON ledger_journals WHEN NEW.status<>'draft'
 BEGIN SELECT RAISE(ABORT,'journal must begin draft'); END;
CREATE TRIGGER no_journal_reopen BEFORE UPDATE OF status ON ledger_journals WHEN OLD.status<>'draft' AND NEW.status='draft'
 BEGIN SELECT RAISE(ABORT,'cannot reopen journal'); END;
CREATE TABLE budget_lines (
 profile_id TEXT NOT NULL, budget_id TEXT NOT NULL, budget_revision INTEGER NOT NULL, id TEXT NOT NULL,
 line_kind TEXT NOT NULL CHECK(line_kind IN('income','obligation','spending','savings')),
 category TEXT NOT NULL, amount_minor INTEGER NOT NULL CHECK(amount_minor>=0), percent_basis_points INTEGER,
 due_day TEXT, PRIMARY KEY(profile_id,budget_id,budget_revision,id),
 FOREIGN KEY(profile_id,budget_id,budget_revision) REFERENCES budgets(profile_id,entity_id,revision),
 CHECK(percent_basis_points IS NULL OR percent_basis_points BETWEEN 0 AND 10000)
) STRICT;
CREATE TABLE reserves (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, budget_id TEXT NOT NULL, budget_revision INTEGER NOT NULL, budget_line_id TEXT NOT NULL,
 backing_account_id TEXT, remaining_minor INTEGER NOT NULL CHECK(remaining_minor>=0),
 currency TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN('active','paid','released')),
 PRIMARY KEY(profile_id,id),
 FOREIGN KEY(profile_id,budget_id,budget_revision,budget_line_id) REFERENCES budget_lines(profile_id,budget_id,budget_revision,id),
 FOREIGN KEY(profile_id,backing_account_id) REFERENCES entities(profile_id,id)
) STRICT;
CREATE TABLE closing_records (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, month TEXT NOT NULL,
 version INTEGER NOT NULL CHECK(version>0), previous_id TEXT, status TEXT NOT NULL CHECK(status IN('provisional','closed','restated')),
 savings_adjustment_minor INTEGER NOT NULL, input_json TEXT NOT NULL CHECK(json_valid(input_json)),
 PRIMARY KEY(profile_id,id), UNIQUE(profile_id,month,version),
 FOREIGN KEY(profile_id,previous_id) REFERENCES closing_records(profile_id,id)
) STRICT;
CREATE TABLE investment_events (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, account_id TEXT NOT NULL,
 instrument_id TEXT, source_event_id TEXT NOT NULL, occurred_ms INTEGER NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN('trade','fee','dividend','coupon','tax','cash_flow','corporate_action','cost_basis')),
 values_json TEXT NOT NULL CHECK(json_valid(values_json)), PRIMARY KEY(profile_id,id),
 FOREIGN KEY(profile_id,account_id) REFERENCES entities(profile_id,id),
 FOREIGN KEY(profile_id,instrument_id) REFERENCES entities(profile_id,id),
 FOREIGN KEY(profile_id,source_event_id) REFERENCES source_events(profile_id,id)
) STRICT;
CREATE TABLE calculation_runs (
 profile_id TEXT NOT NULL REFERENCES profiles(id), id TEXT NOT NULL, method TEXT NOT NULL, method_version TEXT NOT NULL,
 input_json TEXT NOT NULL CHECK(json_valid(input_json)), result_json TEXT NOT NULL CHECK(json_valid(result_json)),
 coverage TEXT NOT NULL CHECK(coverage IN('complete','partial','unavailable','ambiguous')),
 PRIMARY KEY(profile_id,id)
) STRICT;
CREATE TABLE backup_manifests (
 id TEXT PRIMARY KEY, state TEXT NOT NULL CHECK(state IN('building','completed','failed','verified')),
 created_ms INTEGER NOT NULL, schema_version INTEGER NOT NULL,
 relative_path TEXT NOT NULL, sha256 TEXT, verification_ms INTEGER
) STRICT;
CREATE VIRTUAL TABLE search_entries USING fts5(profile_id UNINDEXED,entity_id UNINDEXED,revision UNINDEXED,episode_id UNINDEXED,content,tokenize='unicode61');
PRAGMA user_version=1;
