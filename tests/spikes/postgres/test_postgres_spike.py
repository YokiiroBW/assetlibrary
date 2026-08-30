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
    def setUpClass(cls):
        cls.work = pathlib.Path(tempfile.mkdtemp(prefix='m005-pg-'))
        cls.data, cls.sock = cls.work/'data', cls.work/'sock'; cls.data.mkdir(mode=0o700); cls.sock.mkdir(mode=0o700); cls.data.chmod(0o700); cls.sock.chmod(0o700)
        cls.bin = pathlib.Path(os.environ.get('M005_PG_BIN', '/tmp/m005-pg-install/bin'))
        if not (cls.bin/'initdb').exists():
            out = subprocess.check_output([str(ROOT/'tests/spikes/postgres/bootstrap.sh')], text=True)
            cls.bin = pathlib.Path(out.strip().split('M005_PG_BIN=',1)[1].splitlines()[0])
        run([str(cls.bin/'initdb'), '-D', str(cls.data), '--no-locale', '--encoding=UTF8', '--auth=trust'])
        (cls.data/'postgresql.conf').open('a').write(f"listen_addresses=''\nunix_socket_directories='{cls.sock}'\nshared_buffers='128MB'\nfsync=on\n")
        cls.server = subprocess.Popen([str(cls.bin/'postgres'),'-D',str(cls.data)], stdout=(cls.work/'server.log').open('w'), stderr=subprocess.STDOUT)
        for _ in range(30):
            try: run(['psql','-h',str(cls.sock),'-d','postgres','-c','SELECT 1']) ; break
            except (subprocess.CalledProcessError, RuntimeError): time.sleep(.2)
        cls.db = ['psql','-h',str(cls.sock),'-d','postgres','-v','ON_ERROR_STOP=1','-X','-q','-At']
        run(cls.db + ['-c', 'CREATE DATABASE m005;'])
        cls.db[cls.db.index('postgres')] = 'm005'
        cls.apply()

    @classmethod
    def tearDownClass(cls):
        cls.server.terminate(); cls.server.wait(timeout=10)
        shutil.rmtree(cls.work, ignore_errors=True)

    @classmethod
    def sql(cls, text): return run(cls.db, input=text)

    @classmethod
    def apply(cls):
        # One advisory lock and one transaction per migration makes ownership serialized.
        cls.sql("SELECT pg_advisory_lock(778005);")
        for p in sorted(MIGRATIONS.glob('*.sql')):
            body = p.read_text(); checksum = hashlib.sha256(body.encode()).hexdigest()
            try:
                existing = cls.sql(f"SELECT checksum FROM migration.ledger WHERE version={int(p.name[:3])};").strip()
            except RuntimeError:
                existing = ''
            if existing:
                if existing != checksum: raise AssertionError('migration checksum drift')
                continue
            wrapped = f"BEGIN; SELECT pg_advisory_xact_lock(778005);\n{body}\nINSERT INTO migration.ledger(version,checksum) VALUES ({int(p.name[:3])},'{checksum}'); COMMIT;"
            cls.sql(wrapped)
        cls.sql("SELECT pg_advisory_unlock(778005);")

    def test_migrations_restart_tasks_outbox_and_search(self):
        self.apply(); self.assertEqual(self.sql("SELECT count(*) FROM migration.ledger;").strip(), '2')
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
        # failed migration is transactional: sentinel table never survives.
        with self.assertRaises(RuntimeError): self.sql("BEGIN; CREATE TABLE migration.partial_sentinel(x int); SELECT 1/0; COMMIT;")
        self.assertEqual(self.sql("SELECT to_regclass('migration.partial_sentinel') IS NULL;").strip(), 't')
        self.server.terminate(); self.server.wait(timeout=10)
        self.server = subprocess.Popen([str(self.bin/'postgres'),'-D',str(self.data)], stdout=(self.work/'restart.log').open('w'), stderr=subprocess.STDOUT)
        for _ in range(30):
            try: run(['psql','-h',str(self.sock),'-d','m005','-c','SELECT 1']); break
            except (subprocess.CalledProcessError, RuntimeError): time.sleep(.2)
        self.assertGreaterEqual(int(self.sql("SELECT count(*) FROM library.asset;").strip()), 1)

    def test_500k_profile_keyset_fts_trigram(self):
        self.sql("INSERT INTO library.physical_library VALUES ('00000000-0000-0000-0000-000000000500','Perf','synthetic');")
        self.sql("INSERT INTO library.asset(asset_id,library_id,relative_path,filename,size_bytes,sha256) SELECT md5(g::text)::uuid,'00000000-0000-0000-0000-000000000500',format('batch/%s/item-%s-report.txt',g%1000,g),format('item-%s-report.txt',g),g,decode(repeat('cd',32),'hex') FROM generate_series(1,500000) g;")
        plan = self.sql("EXPLAIN (ANALYZE,BUFFERS) SELECT asset_id FROM library.asset WHERE library_id='00000000-0000-0000-0000-000000000500' AND asset_id > '00000000-0000-0000-0000-000000000000' ORDER BY asset_id LIMIT 50; EXPLAIN (ANALYZE,BUFFERS) SELECT count(*) FROM library.asset WHERE searchable @@ plainto_tsquery('simple','report'); EXPLAIN (ANALYZE,BUFFERS) SELECT count(*) FROM library.asset WHERE filename ILIKE '%report%';")
        (RUNTIME/'500k-plan.txt').parent.mkdir(parents=True, exist_ok=True)
        (RUNTIME/'500k-plan.txt').write_text(plan)
        self.assertIn('Index', plan); self.assertIn('asset_search_gin', plan); self.assertIn('asset_filename_trgm', plan)

if __name__ == '__main__': unittest.main()
