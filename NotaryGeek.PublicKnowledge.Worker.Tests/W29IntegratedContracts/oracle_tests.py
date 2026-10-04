import copy,itertools,json,tempfile,unittest
from pathlib import Path
from oracle import SourceOracle,Violation
from explore import recipe
from w08_authority import Authority

class OracleTests(unittest.TestCase):
    def test_body_and_path_case_are_independent(self):
        p=recipe();o=SourceOracle(p['expected'])
        o.prompt('case-a','--- SOURCE: https://source.invalid/L\nW29_BODY_A_v1\n')
        for u,b in [('L','W29_BODY_B_v1'),('l','W29_BODY_A_v1')]:
            with self.assertRaisesRegex(Violation,'wrong-source-reuse'):o.prompt('case-a',f'--- SOURCE: https://source.invalid/{u}\n{b}\n')
    def test_rejected_body_never_admitted(self):
        o=SourceOracle(recipe('blocked')['expected']);o.prompt('case-a','no sources')
        with self.assertRaisesRegex(Violation,'wrong-source-reuse'):o.prompt('case-a','--- SOURCE: https://source.invalid/bad\nW29_REJECTED_BODY\n')
    def test_aliases_do_not_erase_original_provenance(self):
        p=recipe('alias');o=SourceOracle(p['expected']);e=p['expected']['case-a'][0]
        result={'regressionCaseId':'case-a','sourceCount':1,'sources':[{'url':e['original'],'finalUrl':e['final'],'ok':True}]}
        o.result(result);result['sources'][0]['url']=p['expected']['case-b'][0]['original']
        with self.assertRaisesRegex(Violation,'source-provenance'):o.result(result)
    def test_duplicate_effect_control(self):
        with tempfile.TemporaryDirectory() as d:
            a=Authority(Path(d)/'a.sqlite');o=SourceOracle(recipe()['expected'])
            a.effect({'job':'j','case':'a'});o.check(a);a.effect({'job':'j','case':'a'})
            with self.assertRaisesRegex(Violation,'provider-repeated'):o.check(a)
    def test_archive_replacement_control(self):
        with tempfile.TemporaryDirectory() as d:
            a=Authority(Path(d)/'a.sqlite');o=SourceOracle(recipe()['expected']);e=recipe()['expected']['case-a'][0]
            v={'result':{'regressionCaseId':'case-a','sourceCount':1,'sources':[{'url':e['original'],'finalUrl':e['final'],'ok':True}]}}
            a.seed('runs/2026/a.json',json.dumps(v));o.check(a);a.seed('runs/2026/a.json',b'changed')
            with self.assertRaisesRegex(Violation,'immutable-overwrite'):o.check(a)
    def test_exhaustive_two_fetch_projection_pruning(self):
        # Abstract domain only: two independent request/response pairs, each response after its request.
        traces=[t for t in itertools.permutations(('a-send','a-body','b-send','b-body')) if t.index('a-send')<t.index('a-body') and t.index('b-send')<t.index('b-body')]
        self.assertEqual(6,len(traces))
        projections={tuple(tuple(x for x in t if x.startswith(c)) for c in ('a','b')) for t in traces}
        self.assertEqual(1,len(projections))
        # Sharing a body is observable despite identical ordering; do not prune based on order alone.
        legal={('a','A'),('b','B')};bad={('a','A'),('b','A')};self.assertNotEqual(legal,bad)
if __name__=='__main__':unittest.main()
