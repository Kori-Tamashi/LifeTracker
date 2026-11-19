"""Validate requirement/acceptance links, API schema constraints and render evidence."""
from pathlib import Path
import hashlib
import json
import unittest
import yaml
from jsonschema import Draft4Validator, FormatChecker

ROOT=Path(__file__).resolve().parents[2]
REG=json.loads((ROOT/'docs/requirements/registry.json').read_text())['requirements']
CASES=json.loads((ROOT/'docs/quality/acceptance-cases.json').read_text())['cases']
API=yaml.safe_load((ROOT/'docs/architecture/contracts/openapi.yaml').read_text())

def resolve_schema_refs(value):
    if isinstance(value,dict):
        return {k:(v.replace('#/components/schemas/','#/definitions/') if k=='$ref' else resolve_schema_refs(v)) for k,v in value.items()}
    if isinstance(value,list):return [resolve_schema_refs(v) for v in value]
    return value

class BaselineTests(unittest.TestCase):
    def test_every_requirement_has_existing_design_and_planned_acceptance(self):
        reqs={r['id']:r for r in REG};cases={c['id']:c for c in CASES}
        self.assertEqual(len(REG),len(reqs));self.assertEqual(len(CASES),len(cases))
        for r in REG:
            with self.subTest(requirement=r['id']):
                self.assertTrue(r['design']);self.assertTrue(r['acceptance_cases'])
                for p in r['design']:self.assertTrue((ROOT/p).is_file())
                for c in r['acceptance_cases']:
                    self.assertIn(c,cases);self.assertIn(r['id'],cases[c]['requirements'])
        for case in CASES:
            self.assertEqual('planned',case['status'])
            for key in ['given','when','then','levels']:self.assertTrue(case[key])
            for r in case['requirements']:self.assertIn(r,reqs)

    def test_api_operations_have_unique_ids_auth_errors_and_defined_local_refs(self):
        ids=[]
        def walk(v):
            if isinstance(v,dict):
                if '$ref' in v:
                    self.assertTrue(v['$ref'].startswith('#/components/schemas/'))
                    self.assertIn(v['$ref'].split('/')[-1],API['components']['schemas'])
                for x in v.values():walk(x)
            elif isinstance(v,list):
                for x in v:walk(x)
        walk(API)
        for path,methods in API['paths'].items():
            for method,op in methods.items():
                ids.append(op['operationId'])
                self.assertIn('401',op['responses']);self.assertIn('403',op['responses'])
                if method in ['post','patch','delete','put']:
                    self.assertTrue(any(p['name']=='Idempotency-Key' and p['required'] for p in op['parameters']))
        self.assertEqual(len(ids),len(set(ids)))

    def test_score_contract_rejects_out_of_range_and_wrong_steps(self):
        schema={'definitions':resolve_schema_refs(API['components']['schemas']),'$ref':'#/definitions/AssessmentCreate'}
        validator=Draft4Validator(schema,format_checker=FormatChecker())
        valid={'observed_at':'2026-01-01T10:00:00Z','values':[{'metric':'energy','score':7.5}]}
        self.assertFalse(list(validator.iter_errors(valid)))
        for score in [-.5,10.5,7.1]:
            invalid=dict(valid,values=[{'metric':'energy','score':score}])
            self.assertTrue(list(validator.iter_errors(invalid)))
        self.assertTrue(list(validator.iter_errors(dict(valid,values=[]))))

    def test_diagram_manifests_match_sources_and_outputs(self):
        base=ROOT/'docs/architecture/diagrams'
        manifest=json.loads((base/'render-manifest.json').read_text())
        self.assertEqual({p.name for p in base.glob('*.puml')},{d['source'] for d in manifest['diagrams']})
        for d in manifest['diagrams']:
            self.assertEqual(d['source_sha256'],hashlib.sha256((base/d['source']).read_bytes()).hexdigest())
            self.assertEqual(d['output_sha256'],hashlib.sha256((base/d['output']).read_bytes()).hexdigest())

if __name__=='__main__':unittest.main()
