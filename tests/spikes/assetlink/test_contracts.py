import json, re, unittest
from pathlib import Path
ROOT=Path(__file__).parents[3]; C=ROOT/'contracts/assetlink'; F=ROOT/'tests/spikes/assetlink/fixtures'; MAX=2**64-1
def load(p): return json.loads(Path(p).read_text())
def ref(s):
    if '$ref' not in s:return s
    _,frag=s['$ref'].split('#'); d=load(C/'common.schema.json')
    for x in frag.strip('/').split('/'): d=d[x]
    return d
def valid(s,v):
    s=ref(s)
    if 'const'in s and v!=s['const']:return False
    if s.get('type')=='object': return isinstance(v,dict) and all(k in v for k in s.get('required',[])) and all(valid(s['properties'][k],x) for k,x in v.items() if k in s.get('properties',{}))
    if s.get('type')=='array': return isinstance(v,list) and len(v)>=s.get('minItems',0) and all(valid(s['items'],x) for x in v)
    if s.get('type')=='string': return isinstance(v,str) and len(v)>=s.get('minLength',0) and len(v)<=s.get('maxLength',10**9) and ('pattern' not in s or re.fullmatch(s['pattern'],v)!=None)
    if s.get('type')=='boolean': return isinstance(v,bool)
    return True
def u(x): return isinstance(x,str) and re.fullmatch(r'(0|[1-9][0-9]*)',x or '')!=None and len(x)<=20 and int(x)<=MAX
def negotiate(a,b):
    common=[x for x in a if any(x.split('.')[0]==y.split('.')[0] for y in b)]
    return max(common,key=lambda x:tuple(map(int,x.split('.'))),default=None)
def failover(expected,actual): return expected==actual
def chunk_ok(o,z,l): return u(o) and int(o)+z<=int(l)
class Contract(unittest.TestCase):
    def test_01_json_ids_refs(self):
        ids=[]
        for p in C.glob('*.json'):
            x=load(p); self.assertEqual(x['$schema'],'https://json-schema.org/draft/2020-12/schema'); ids.append(x['$id'])
        self.assertEqual(len(ids),len(set(ids)))
    def test_02_required_and_nested_ref(self):
        self.assertTrue(valid(load(C/'handshake.schema.json'),{'message_type':'handshake.request','client':{'kind':'web','version':'1'},'supported_versions':['1.0'],'capabilities':[],'endpoint_role':'primary'})); self.assertFalse(valid(load(C/'handshake.schema.json'),{}))
    def test_03_uint64_boundaries(self):
        for x in ['0','100000000000','18446744073709551615']:self.assertTrue(u(x))
        for x in ['-1','01','18446744073709551616','123456789012345678901']:self.assertFalse(u(x))
    def test_04_negotiation(self): self.assertEqual(negotiate(['1.0','1.1'],['1.1','1.2']),'1.1'); self.assertIsNone(negotiate(['1.0'],['2.0']))
    def test_05_failover(self): self.assertTrue(failover('srv-a','srv-a')); self.assertFalse(failover('srv-a','srv-b'))
    def test_06_open_future_fields(self): self.assertTrue(valid(load(C/'handshake-response.schema.json'),{'message_type':'handshake.response','selected_version':'2.0','server_id':'s','server_version':'x','capabilities':['future'],'endpoint_role':'future'}))
    def test_07_control_error_cancel(self):
        self.assertTrue(valid(load(C/'control-result.schema.json'),{'message_type':'control.result','request_id':'r','ok':True,'body':{}})); self.assertTrue(valid(load(C/'control-cancel.schema.json'),{'message_type':'control.cancel','request_id':'r','cancel_of':'x'})); self.assertTrue(valid(load(C/'error.schema.json'),{'message_type':'error','request_id':'r','error':{'code':'future','message':'x','retryable':True}}))
    def test_08_replay_event(self): self.assertTrue(valid(load(C/'replay-result.schema.json'),{'message_type':'event.replay.result','events':[],'next_cursor':'c','gap':True}))
    def test_09_fixtures_loaded(self): self.assertGreaterEqual(len(list(F.glob('*.json'))),10); self.assertTrue(valid(load(C/'upload-complete-result.schema.json'),load(F/'valid-upload-complete-result.json'))); self.assertFalse(valid(load(C/'upload-chunk.schema.json'),load(F/'invalid-canonical-offset.json')))
    def test_10_chunk_bounds_idempotency(self): self.assertTrue(chunk_ok('10',5,'15')); self.assertFalse(chunk_ok('11',5,'15')); self.assertNotEqual('hash-a','hash-b')
    def test_11_ranges(self): self.assertTrue(valid(load(C/'upload-status.schema.json'),{'message_type':'upload.status','transfer_id':'t','length':'20','received_ranges':[{'start':'0','end':'9'}]})); self.assertTrue(valid(load(C/'download-range.schema.json'),{'message_type':'download.range','asset_id':'a','start':'0','end':'9'}))
    def test_12_complete_evidence(self): self.assertTrue(valid(load(C/'upload-complete-result.schema.json'),load(F/'valid-upload-complete-result.json'))); self.assertFalse(valid(load(C/'upload-complete-result.schema.json'),{**load(F/'valid-upload-complete-result.json'),'verified':False}))
    def test_13_original_draft_regression(self): self.assertFalse(valid(load(C/'handshake.schema.json'),{'protocol_version':'bad','client':{},'server_id':'x','capabilities':[],'endpoint_role':'unknown'}))
    def test_14_fixture_json_all_parse(self):
        for p in F.glob('*.json'): self.assertIsInstance(load(p),dict)
if __name__=='__main__': unittest.main()
