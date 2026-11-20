"""Execute the design DDL against synthetic in-memory SQLite; no real profile/WAL."""
from pathlib import Path
import sqlite3
import unittest

DDL=(Path(__file__).resolve().parents[2]/'docs/architecture/storage/schema-v1.sql').read_text()

class DesignSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db=sqlite3.connect(':memory:')
        self.db.executescript(DDL)
        self.db.execute("INSERT INTO profiles(id) VALUES('owner')")
        self.db.commit()

    def tearDown(self): self.db.close()

    def entity(self,id,kind='test',revision=1):
        self.db.execute('INSERT INTO entities VALUES(?,?,?,?)',('owner',id,kind,revision))
        self.db.execute('INSERT INTO revisions VALUES(?,?,?,?,?,?,?,?)',('owner',id,revision,0,'synthetic','accepted',None,'{}'))

    def source(self,id='event'):
        self.db.execute('INSERT INTO source_events VALUES(?,?,?,?,?,?,?,?)',('owner',id,'synthetic',id,'hash',0,'completed','{}'))

    def test_deferred_current_revision_cannot_be_dangling(self):
        self.db.execute("INSERT INTO entities VALUES('owner','missing','observation',1)")
        with self.assertRaises(sqlite3.IntegrityError): self.db.commit()
        self.db.rollback()
        self.entity('valid');self.db.commit()
        self.assertEqual([],self.db.execute('PRAGMA foreign_key_check').fetchall())

    def test_assessment_range_and_source_constraints(self):
        self.entity('assessment')
        self.db.execute("INSERT INTO observations VALUES('owner','assessment',1,'energy',15,0,'self_report')")
        self.db.commit()
        self.entity('invalid')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO observations VALUES('owner','invalid',1,'energy',21,0,'self_report')")

    def test_duplicate_source_delivery_conflicts(self):
        self.source()
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('INSERT INTO source_events VALUES(?,?,?,?,?,?,?,?)',('owner','different-id','synthetic','event','hash',0,'completed','{}'))

    def test_reminder_quota_is_shared_across_deliveries_and_persistent(self):
        for slot in range(1,4):
            self.db.execute('INSERT INTO notification_events VALUES(?,?,?,?,?,?,?,?)',('owner',f'n{slot}',f'r{slot}','reminder','2026-01-01',slot,1,100))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO notification_events VALUES('owner','n4','r4','reminder','2026-01-01',1,1,100)")
        self.db.execute("UPDATE notification_events SET consumes_quota=0 WHERE id='n1'")
        self.db.execute("INSERT INTO notification_events VALUES('owner','n4','r4','reminder','2026-01-01',1,1,100)")
        for channel in ['macos','telegram']:
            self.db.execute('INSERT INTO deliveries VALUES(?,?,?,?,?,?)',('owner','n4',channel,'pending',0,None))
        self.assertEqual(3,self.db.execute("SELECT count(*) FROM notification_events WHERE consumes_quota=1").fetchone()[0])

    def test_cross_profile_links_rejected(self):
        self.db.execute("INSERT INTO profiles(id,active) VALUES('other',0)")
        self.entity('same')
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO observations VALUES('other','same',1,'energy',10,0,'self_report')")

    def test_unbalanced_journal_and_posted_edits_rejected(self):
        self.source();self.entity('cash');self.entity('category')
        self.db.execute("INSERT INTO ledger_journals VALUES('owner','j','event','expense','draft',0,'1')")
        self.db.execute("INSERT INTO ledger_postings VALUES('owner','j',1,'cash','RUB',-100)")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ledger_journals SET status='posted' WHERE id='j'")
        self.db.execute("INSERT INTO ledger_postings VALUES('owner','j',2,'category','RUB',100)")
        self.db.execute("UPDATE ledger_journals SET status='posted' WHERE id='j'")
        self.db.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ledger_postings SET amount_minor=0 WHERE journal_id='j'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ledger_journals SET status='draft' WHERE id='j'")

    def test_invalid_json_and_unsafe_managed_path_rejected(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE profiles SET settings_json='invalid' WHERE id='owner'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('INSERT INTO media VALUES(?,?,?,?,?,?,?,?,?)',('owner','m','../escape','a'*64,1,'image/png','durable',None,0))

if __name__=='__main__':unittest.main()
