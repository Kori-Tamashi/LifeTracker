"""Model consistency checks, not tests of unimplemented product algorithms."""
from pathlib import Path
import importlib.util
import json
import unittest
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('build_processes',ROOT/'tools/build_processes.py')
builder=importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)
MODELS=json.loads((ROOT/'docs/processes/catalog.json').read_text())
NS=builder.NS


def reachable(initial, edges):
    seen=set(initial)
    stack=list(initial)
    while stack:
        node=stack.pop()
        for dest in edges.get(node, []):
            if dest not in seen:
                seen.add(dest); stack.append(dest)
    return seen


class ProcessModelTests(unittest.TestCase):
    def test_ids_positions_and_lane_ownership(self):
        self.assertEqual(len(MODELS),len({m['id'] for m in MODELS}))
        for m in MODELS:
            with self.subTest(process=m['id']):
                ids=[n['id'] for n in m['nodes']]
                self.assertEqual(len(ids),len(set(ids)))
                cells=[(n['lane'],n['column']) for n in m['nodes']]
                self.assertEqual(len(cells),len(set(cells)))
                self.assertTrue(all(0<=n['lane']<len(m['lanes']) for n in m['nodes']))

    def test_all_paths_have_a_possible_end_and_nodes_are_reachable(self):
        for m in MODELS:
            with self.subTest(process=m['id']):
                nodes={n['id']:n for n in m['nodes']}
                edges={n:[] for n in nodes}; reverse={n:[] for n in nodes}
                for f in m['flows']:
                    self.assertIn(f['source'],nodes); self.assertIn(f['target'],nodes)
                    edges[f['source']].append(f['target']); reverse[f['target']].append(f['source'])
                starts=[n for n in nodes if nodes[n]['type']=='startEvent']
                ends=[n for n in nodes if nodes[n]['type']=='endEvent']
                self.assertEqual(1,len(starts)); self.assertTrue(ends)
                self.assertEqual(set(nodes),reachable(starts,edges))
                self.assertEqual(set(nodes),reachable(ends,reverse))
                for n,node in nodes.items():
                    if node['type']=='startEvent': self.assertFalse(reverse[n])
                    elif node['type']=='endEvent': self.assertFalse(edges[n])
                    else: self.assertTrue(edges[n]); self.assertTrue(reverse[n])

    def test_exclusive_decisions_have_labelled_branches_and_one_default(self):
        for m in MODELS:
            for n in m['nodes']:
                flows=[f for f in m['flows'] if f['source']==n['id']]
                with self.subTest(process=m['id'],node=n['id']):
                    if len(flows)>1:
                        self.assertEqual('exclusiveGateway',n['type'])
                        self.assertEqual(1,sum(f['default'] for f in flows))
                        self.assertTrue(all(f['label'] for f in flows))

    def test_xml_ids_references_and_di(self):
        for m in MODELS:
            with self.subTest(process=m['id']):
                root=ET.fromstring(builder.bpmn(m))
                ids=[e.attrib['id'] for e in root.iter() if 'id' in e.attrib]
                self.assertEqual(len(ids),len(set(ids)))
                for e in root.iter():
                    for key in ['sourceRef','targetRef','bpmnElement','default']:
                        if key in e.attrib: self.assertIn(e.attrib[key],ids)
                shapes=root.findall('.//bpmndi:BPMNShape',NS)
                self.assertEqual(len(m['nodes'])+len(m['lanes']),len(shapes))
                for shape in shapes: self.assertIsNotNone(shape.find('dc:Bounds',NS))
                edges=root.findall('.//bpmndi:BPMNEdge',NS)
                self.assertEqual(len(m['flows']),len(edges))
                for edge in edges: self.assertGreaterEqual(len(edge.findall('di:waypoint',NS)),2)

    def test_committed_artifacts_match_catalog(self):
        for m in MODELS:
            with self.subTest(process=m['id']):
                self.assertEqual(builder.bpmn(m),(ROOT/f'docs/processes/bpmn/{m["id"]}.bpmn').read_text())
                self.assertEqual(builder.svg(m),(ROOT/f'docs/processes/previews/{m["id"]}.svg').read_text())

    def test_routes_do_not_cross_unrelated_task_boxes(self):
        for model in MODELS:
            bounds = builder.layout(model)
            for _, flow, points in builder.paths(model, bounds):
                for node_id, (x, y, width, height) in bounds.items():
                    if node_id in {flow['source'], flow['target']}:
                        continue
                    for a, b in zip(points, points[1:]):
                        with self.subTest(process=model['id'], flow=flow, obstacle=node_id):
                            if a[0] == b[0]:
                                crosses = x < a[0] < x + width and max(min(a[1], b[1]), y) < min(max(a[1], b[1]), y + height)
                            else:
                                self.assertEqual(a[1], b[1])
                                crosses = y < a[1] < y + height and max(min(a[0], b[0]), x) < min(max(a[0], b[0]), x + width)
                            self.assertFalse(crosses)

    def test_unknown_quest_outcome_has_no_path_to_penalty(self):
        model = next(m for m in MODELS if m['id'] == 'BP05')
        edges = {node['id']: [] for node in model['nodes']}
        for flow in model['flows']:
            edges[flow['source']].append(flow['target'])
        self.assertNotIn('decay', reachable(['nodata'], edges))


if __name__=='__main__': unittest.main()
