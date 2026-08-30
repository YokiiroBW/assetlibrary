import hashlib, os, pathlib, shutil, subprocess, tempfile, time, unittest, uuid

ROOT = pathlib.Path(__file__).resolve().parents[3]
MIGRATIONS = ROOT / 'database' / 'migrations'
RUNTIME = ROOT / '.runtime' / 'sandbox-storage' / 'M0-005'

def run(cmd, **kw):
    try:
        return subprocess.run(cmd, check=True, text=True, capture_output=True, **kw).stdout
    except subprocess.CalledProcessError as exc:
        raise RuntimeError(f'{cmd[0]} failed: {exc.stderr}') from exc

class PostgresSpike(unittest.TestCase):
    @classmethod
    def _cleanup(cls):
        server = getattr(cls, 'server', None)
        if server is not None and server.poll() is None:
            server.terminate(); server.wait(timeout=10)
        for name in ('log', 'restart_log'):
            handle = getattr(cls, name, None)
            if handle and not handle.closed: handle.close()
        work = getattr(cls, 'work', None)
        if work: shutil.rmtree(work, ignore_errors=True)

    @classmethod
    def setUpClass(cls):
        try: cls._setUpClass()
        except BaseException:
            cls._cleanup(); raise

    @classmethod
    def _setUpClass(cls):
        cls.work = pathlib.Path(tempfile.mkdtemp(prefix='m005-pg-'))
        cls.data, cls.sock = cls.work/'data', cls.work/'sock'; cls.data.mkdir(mode=0o700); cls.sock.mkdir(mode=0o700); cls.data.chmod(0o700); cls.sock.chmod(0o700)
        cls.bin = pathlib.Path(os.environ.get('M005_PG_BIN', '/tmp/m005-pg-install/bin'))
        if not (cls.bin/'initdb').exists():
            out = subprocess.check_output([str(ROOT/'tests/spikes/postgres/bootstrap.sh')], text=True)
            cls.bin = pathlib.Path(out.strip().split('M005_PG_BIN=',1)[1].splitlines()[0])
        run([str(cls.bin/'initdb'), '-D', str(cls.data), '--no-locale', '--encoding=UTF8', '--auth=trust'])
        with (cls.data/'postgresql.conf').open('a') as conf: conf.write(f"listen_addresses=''\nunix_socket_directories='{cls.sock}'\nshared_buffers='128MB'\nfsync=on\n")
        cls.log = (cls.work/'server.log').open('w'); cls.server = subprocess.Popen([str(cls.bin/'postgres'),'-D',str(cls.data)], stdout=cls.log, stderr=subprocess.STDOUT)
        for _ in range(30):
            try: run(['psql','-h',str(cls.sock),'-d','postgres','-c','SELECT 1']) ; break
            except (subprocess.CalledProcessError, RuntimeError): time.sleep(.2)
        else:
            cls.server.terminate(); cls.server.wait(timeout=10); cls.log.close(); shutil.rmtree(cls.work, ignore_errors=True); raise RuntimeError('postgres startup timeout')
        cls.db = ['psql','-h',str(cls.sock),'-d','postgres','-v','ON_ERROR_STOP=1','-X','-q','-At']
        run(cls.db + ['-c', 'CREATE DATABASE m005;'])
        cls.db[cls.db.index('postgres')] = 'm005'
        cls.server_version = cls.sql('SHOW server_version;').strip()
        cls.client_version = run(['psql','--version']).strip()
        if not cls.server_version.startswith('16.15') or '16.15' not in cls.client_version: raise RuntimeError(f'version mismatch: {cls.server_version} / {cls.client_version}')
        cls.apply()

    @classmethod
    def tearDownClass(cls):
        cls._cleanup()

    @classmethod
    def sql(cls, text): return run(cls.db, input=text)

    @classmethod
    def apply(cls):
        run(['python3',str(ROOT/'tests/spikes/postgres/migration_runner.py'), '--socket',str(cls.sock),'--database','m005'])

    def test_migrations_restart_tasks_outbox_and_search(self):
        self.apply(); self.assertEqual(self.sql("SELECT count(*) FROM migration.ledger;").strip(), '2')
        runners = [subprocess.Popen(['python3',str(ROOT/'tests/spikes/postgres/migration_runner.py'),'--socket',str(self.sock),'--database','m005'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for _ in range(2)]
        results = [p.communicate(timeout=20) for p in runners]
        self.assertTrue(all(p.returncode == 0 for p in runners), results)
        self.assertEqual(self.sql("SELECT count(*) FROM migration.ledger;").strip(), '2')
        mismatch = self.work/'mismatch'; mismatch.mkdir(); (mismatch/'003.sql').write_text('SELECT 1;\n'); self.sql("INSERT INTO migration.ledger(version,checksum) VALUES (3,'wrong');")
        drifted = subprocess.run(['python3',str(ROOT/'tests/spikes/postgres/migration_runner.py'),'--socket',str(self.sock),'--database','m005','--migration-dir',str(mismatch)], text=True, capture_output=True)
        self.assertNotEqual(drifted.returncode, 0); self.assertIn('checksum drift', drifted.stderr)
        self.sql("DELETE FROM migration.ledger WHERE version=3;")
        drift = self.work/'drift'; drift.mkdir(); (drift/'003_drift.sql').write_text('-- failing migration\nCREATE TABLE migration.partial_probe(x integer); SELECT 1/0;\n')
        bad = subprocess.run(['python3',str(ROOT/'tests/spikes/postgres/migration_runner.py'),'--socket',str(self.sock),'--database','m005','--migration-dir',str(drift)], text=True, capture_output=True)
        self.assertNotEqual(bad.returncode, 0); self.assertEqual(self.sql("SELECT to_regclass('migration.partial_probe') IS NULL;").strip(), 't'); self.assertEqual(self.sql("SELECT count(*) FROM migration.ledger WHERE version=3;").strip(), '0')
        lib, asset = uuid.uuid4(), uuid.uuid4()
        self.sql(f"INSERT INTO library.physical_library VALUES ('{lib}','Synthetic','root-marker'); INSERT INTO library.asset(asset_id,library_id,relative_path,filename,size_bytes,sha256) VALUES ('{asset}','{lib}','2026/a.txt','alpha-report.txt',12,decode(repeat('ab',32),'hex')); INSERT INTO tasks.durable_task(task_id,idempotency_key,task_type,payload) VALUES ('{asset}','idem-1','index','{{}}') ON CONFLICT (idempotency_key) DO NOTHING; INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{uuid.uuid4()}','{asset}','asset.indexed','{{}}');")
        # Atomic state + event is one transaction; duplicate request is harmless.
        self.sql(f"BEGIN; UPDATE library.asset SET filename='alpha-report-v2.txt' WHERE asset_id='{asset}'; INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{uuid.uuid4()}','{asset}','asset.renamed','{{}}'); COMMIT;")
        procs = [subprocess.Popen(self.db + ['-c', f"SELECT task_id FROM tasks.claim_one('worker-{x}',30);"], stdout=subprocess.PIPE, text=True) for x in ('a','b')]
        claims = [p.communicate(timeout=10)[0] for p in procs]
        self.assertEqual(sum(str(asset) in c for c in claims), 1)
        claim = next(c for c in claims if str(asset) in c)
        owner = 'worker-a' if str(asset) in claims[0] else 'worker-b'
        wrong = 'worker-b' if owner == 'worker-a' else 'worker-a'
        self.assertEqual(self.sql(f"SELECT COALESCE(tasks.heartbeat('{asset}','{wrong}',30),false);").strip(), 'f')
        self.assertEqual(self.sql(f"SELECT tasks.heartbeat('{asset}','{owner}',1);").strip(), 't')
        time.sleep(1.2); self.assertEqual(self.sql("SELECT tasks.reclaim_expired();").strip(), '1')
        self.assertEqual(self.sql(f"SELECT state FROM tasks.durable_task WHERE task_id='{asset}';").strip(), 'queued')
        cancel_id = uuid.uuid4()
        self.sql(f"INSERT INTO tasks.durable_task(task_id,idempotency_key,task_type,payload) VALUES ('{cancel_id}','cancel-key','x','{{}}');")
        self.assertEqual(self.sql(f"SELECT tasks.cancel('{cancel_id}');").strip(), 't')
        self.assertEqual(self.sql(f"SELECT state FROM tasks.durable_task WHERE task_id='{cancel_id}';").strip(), 'cancelled')
        self.sql("UPDATE tasks.durable_task SET state='succeeded',lease_owner=NULL,lease_until=NULL WHERE state='queued';")
        leased_cancel = uuid.uuid4(); self.sql(f"INSERT INTO tasks.durable_task(task_id,idempotency_key,task_type,payload) VALUES ('{leased_cancel}','leased-cancel','x','{{}}'); SELECT tasks.claim_one('lease-owner',30);")
        self.assertEqual(self.sql(f"SELECT state||':'||lease_owner FROM tasks.durable_task WHERE task_id='{leased_cancel}';").strip(), 'leased:lease-owner')
        self.assertEqual(self.sql(f"SELECT tasks.cancel('{leased_cancel}');").strip(), 't')
        conflict = subprocess.run(self.db + ['-c', f"INSERT INTO tasks.durable_task(task_id,idempotency_key,task_type,payload) VALUES ('{uuid.uuid4()}','idem-1','index','{{\"different\":true}}');"], text=True, capture_output=True)
        self.assertNotEqual(conflict.returncode, 0)
        fail_id = uuid.uuid4(); self.sql(f"INSERT INTO tasks.durable_task(task_id,idempotency_key,task_type,payload,max_attempts) VALUES ('{fail_id}','fail-key','x','{{}}',1);")
        for _ in range(5):
            claimed_id = self.sql("SELECT task_id FROM tasks.claim_one('crasher',1);").strip()
            if str(fail_id) in claimed_id: break
            if claimed_id: self.sql(f"UPDATE tasks.durable_task SET state='succeeded',lease_owner=NULL,lease_until=NULL WHERE task_id='{claimed_id}';")
        time.sleep(1.2); self.sql("SELECT tasks.reclaim_expired();"); self.assertEqual(self.sql(f"SELECT state FROM tasks.durable_task WHERE task_id='{fail_id}';").strip(), 'failed')
        ev = uuid.uuid4()
        self.sql(f"INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{ev}','{asset}','test','{{}}');")
        claimed = ''
        for _ in range(10):
            claimed = self.sql("SELECT event_id FROM events.claim_one('pub-a',1);")
            if str(ev) in claimed: break
            if claimed.strip(): self.sql(f"SELECT events.mark_published('{claimed.strip()}','pub-a');")
        self.assertIn(str(ev), claimed)
        self.assertEqual(self.sql(f"SELECT events.mark_published('{ev}','pub-b');").strip(), 'f')
        self.assertEqual(self.sql(f"SELECT events.release('{ev}','pub-b','wrong owner');").strip(), 'f')
        time.sleep(1.2)
        self.assertIn(str(ev), self.sql("SELECT event_id FROM events.claim_one('pub-b',30);"))
        self.assertEqual(self.sql(f"SELECT events.mark_published('{ev}','pub-b');").strip(), 't')
        retry_ev = uuid.uuid4(); self.sql(f"INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{retry_ev}','{asset}','retry','{{}}');")
        self.assertIn(str(retry_ev), self.sql("SELECT event_id FROM events.claim_one('pub-f',30);") + self.sql("SELECT event_id FROM events.claim_one('pub-f',30);"))
        self.assertEqual(self.sql(f"SELECT events.release('{retry_ev}','pub-f','transport failed');").strip(), 't')
        self.assertEqual(self.sql(f"SELECT last_error FROM events.outbox WHERE event_id='{retry_ev}';").strip(), 'transport failed')
        self.assertIn(str(retry_ev), self.sql("SELECT event_id FROM events.claim_one('pub-r',30);"))
        self.sql(f"SELECT events.mark_published('{retry_ev}','pub-r');")
        same_ev = uuid.uuid4(); self.sql(f"INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{same_ev}','{asset}','same','{{}}');")
        pubs = [subprocess.Popen(self.db + ['-c', f"SELECT event_id FROM events.claim_one('publisher-{i}',30);"], stdout=subprocess.PIPE, text=True) for i in range(2)]
        pub_results = [p.communicate(timeout=10)[0] for p in pubs]
        self.assertEqual(sum(str(same_ev) in out for out in pub_results), 1)
        self.sql(f"INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{same_ev}','{asset}','same','{{}}') ON CONFLICT (event_id) DO NOTHING;")
        self.assertEqual(self.sql(f"SELECT count(*) FROM events.outbox WHERE event_id='{same_ev}';").strip(), '1')
        conflict_event = subprocess.run(self.db + ['-c', f"INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{same_ev}','{asset}','different','{{}}');"], text=True, capture_output=True)
        self.assertNotEqual(conflict_event.returncode, 0)
        atomic = uuid.uuid4(); failed = subprocess.run(self.db + ['-c', f"BEGIN; UPDATE library.asset SET filename='rolled-back' WHERE asset_id='{asset}'; INSERT INTO events.outbox(event_id,aggregate_id,event_type,payload) VALUES ('{atomic}','{asset}','rollback','{{}}'); SELECT 1/0; COMMIT;"], text=True, capture_output=True)
        self.assertNotEqual(failed.returncode, 0); self.assertEqual(self.sql(f"SELECT count(*) FROM library.asset WHERE asset_id='{asset}' AND filename='rolled-back';").strip(), '0'); self.assertEqual(self.sql(f"SELECT count(*) FROM events.outbox WHERE event_id='{atomic}';").strip(), '0')
        self.sql(f"INSERT INTO library.permission_scope VALUES ('{lib}','alice','read');")
        self.assertEqual(self.sql(f"SELECT count(*) FROM library.asset a JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='alice' AND p.access_level <> 'none';").strip(), '1')
        self.assertEqual(self.sql(f"SELECT count(*) FROM library.asset a JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='bob' AND p.access_level <> 'none';").strip(), '0')
        # failed migration is transactional: sentinel table never survives.
        with self.assertRaises(RuntimeError): self.sql("BEGIN; CREATE TABLE migration.partial_sentinel(x int); SELECT 1/0; COMMIT;")
        self.assertEqual(self.sql("SELECT to_regclass('migration.partial_sentinel') IS NULL;").strip(), 't')
        self.server.terminate(); self.server.wait(timeout=10)
        type(self).restart_log = (self.work/'restart.log').open('w'); type(self).server = subprocess.Popen([str(self.bin/'postgres'),'-D',str(self.data)], stdout=type(self).restart_log, stderr=subprocess.STDOUT)
        for _ in range(30):
            try: run(['psql','-h',str(self.sock),'-d','m005','-c','SELECT 1']); break
            except (subprocess.CalledProcessError, RuntimeError): time.sleep(.2)
        else:
            self.fail('postgres restart timeout')
        self.assertGreaterEqual(int(self.sql("SELECT count(*) FROM library.asset;").strip()), 1)

    def test_500k_profile_keyset_fts_trigram(self):
        self.sql("INSERT INTO library.physical_library VALUES ('00000000-0000-0000-0000-000000000500','Perf','synthetic');")
        self.sql("INSERT INTO library.permission_scope VALUES ('00000000-0000-0000-0000-000000000500','perf-reader','read');")
        self.sql("INSERT INTO library.asset(asset_id,library_id,relative_path,filename,size_bytes,sha256) SELECT md5(g::text)::uuid,'00000000-0000-0000-0000-000000000500',format('batch/%s/item-%s%s.txt',g%1000,g,CASE WHEN g%10=0 THEN ' reportx' ELSE '' END),format('item %s%s.txt',g,CASE WHEN g%10=0 THEN ' reportx' ELSE '' END),g,decode(repeat('cd',32),'hex') FROM generate_series(1,500000) g;")
        fts_count = self.sql("SELECT count(*) FROM library.asset WHERE searchable @@ plainto_tsquery('simple','reportx.txt');").strip()
        self.assertEqual(fts_count, '50000', self.sql("SELECT filename||'|'||searchable::text FROM library.asset WHERE filename LIKE '%reportx%' LIMIT 1;"))
        self.assertEqual(self.sql("SELECT count(*) FROM library.asset WHERE filename ILIKE '%499990 reportx%';").strip(), '1')
        self.assertEqual(self.sql("SELECT count(*) FROM (SELECT a.asset_id FROM library.asset a JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='perf-reader' AND a.asset_id > '00000000-0000-0000-0000-000000000000' ORDER BY a.asset_id LIMIT 50) q;").strip(), '50')
        self.assertEqual(self.sql("SELECT count(*) FROM library.asset a LEFT JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='denied' AND a.asset_id > '00000000-0000-0000-0000-000000000000';").strip(), '0')
        self.assertEqual(self.sql("SELECT count(*) FROM library.asset WHERE relative_path LIKE 'batch/42/%';").strip(), '500')
        self.assertEqual(self.sql("SELECT count(*) FROM library.asset WHERE relative_path LIKE 'missing/%';").strip(), '0')
        timings = {"keyset": [], "fts": [], "trigram": []}
        for _ in range(5):
            for name, query in [("keyset", "SELECT asset_id FROM library.asset a JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='perf-reader' AND a.asset_id > '00000000-0000-0000-0000-000000000000' ORDER BY a.asset_id LIMIT 50"), ("fts", "SELECT count(*) FROM library.asset WHERE searchable @@ plainto_tsquery('simple','reportx.txt')"), ("trigram", "SELECT asset_id FROM library.asset WHERE filename ILIKE '%499990 reportx%'")]:
                start = time.perf_counter(); self.sql(query); timings[name].append((time.perf_counter()-start)*1000)
        plan = self.sql("EXPLAIN (ANALYZE,BUFFERS) SELECT a.asset_id FROM library.asset a JOIN library.permission_scope p USING(library_id) WHERE p.principal_id='perf-reader' AND a.asset_id > '00000000-0000-0000-0000-000000000000' ORDER BY a.asset_id LIMIT 50; EXPLAIN (ANALYZE,BUFFERS) SELECT count(*) FROM library.asset WHERE searchable @@ plainto_tsquery('simple','reportx.txt'); EXPLAIN (ANALYZE,BUFFERS) SELECT asset_id FROM library.asset WHERE filename ILIKE '%499990 reportx%'; SELECT pg_size_pretty(pg_table_size('library.asset')),pg_size_pretty(pg_indexes_size('library.asset')); ")
        (RUNTIME/'500k-plan.txt').parent.mkdir(parents=True, exist_ok=True)
        dist = '\n'.join(f"WARM_{name.upper()}_MS=min:{min(vals):.3f},median:{sorted(vals)[len(vals)//2]:.3f},p95:{sorted(vals)[-1]:.3f},max:{max(vals):.3f}" for name, vals in timings.items())
        (RUNTIME/'500k-plan.txt').write_text(plan + '\n' + dist + '\n')
        self.assertIn('Index', plan); self.assertIn('asset_search_gin', plan); self.assertIn('asset_filename_trgm', plan)

if __name__ == '__main__': unittest.main()
