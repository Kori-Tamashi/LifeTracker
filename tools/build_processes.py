#!/usr/bin/env python3
"""Build BPMN 2.0 + DI and SVG previews from a synthetic process catalog."""
from pathlib import Path
import argparse
import html
import heapq
import json
import textwrap
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NS = {'bpmn': 'http://www.omg.org/spec/BPMN/20100524/MODEL',
      'bpmndi': 'http://www.omg.org/spec/BPMN/20100524/DI',
      'dc': 'http://www.omg.org/spec/DD/20100524/DC',
      'di': 'http://www.omg.org/spec/DD/20100524/DI',
      'xsi': 'http://www.w3.org/2001/XMLSchema-instance'}
for prefix, uri in NS.items():
    ET.register_namespace(prefix, uri)


def el(parent, tag, attrib=None, text=None):
    prefix, local = tag.split(':')
    node = ET.SubElement(parent, '{'+NS[prefix]+'}'+local, attrib or {})
    if text is not None:
        node.text = text
    return node


def layout(model):
    bounds = {}
    for node in model['nodes']:
        kind = node['type']
        w, h = (38, 38) if 'Event' in kind else ((52, 52) if kind.endswith('Gateway') else (154, 70))
        bounds[node['id']] = (215+node['column']*205+(154-w)/2, 112+node['lane']*160+(70-h)/2, w, h)
    return bounds


def paths(model, bounds):
    # Orthogonal routing around actual nodes; crossings of edges are permitted.
    boxes=[(x-8,y-8,x+w+8,y+h+8) for x,y,w,h in bounds.values()]
    xs=sorted({v for x,y,w,h in bounds.values() for v in [x-24,x+w+24,x+w,x,x+w/2]})
    ys=sorted({v for x,y,w,h in bounds.values() for v in [y-30,y-24,y+h+24,y+h+45,y+h/2]} | {90.0})
    def clear(a,b):
        for l,t,r,d in boxes:
            if a[0]==b[0]:
                if l<a[0]<r and max(min(a[1],b[1]),t)<min(max(a[1],b[1]),d): return False
            elif t<a[1]<d and max(min(a[0],b[0]),l)<min(max(a[0],b[0]),r): return False
        return True
    result=[]
    for i,flow in enumerate(model['flows']):
        x,y,w,h=bounds[flow['source']]; a,b,c,d=bounds[flow['target']]
        start=(x+w,y+h/2); end=(a,b+d/2)
        first=(x+w+24,y+h/2); last=(a-24,b+d/2)
        outgoing=[f for f in model['flows'] if f['source']==flow['source']]
        if len(outgoing)>1:
            if b+d/2 < y+h/2:
                start=(x+w/2,y); first=(x+w/2,y-24)
            elif b+d/2 > y+h/2:
                start=(x+w/2,y+h); first=(x+w/2,y+h+24)
            elif outgoing.index(flow)>0:
                start=(x+w/2,y); first=(x+w/2,y-24)

        xi=sorted(set(xs+[first[0],last[0]])); yi=sorted(set(ys+[first[1],last[1]]))
        begin=(xi.index(first[0]),yi.index(first[1]),0)
        goal=(xi.index(last[0]),yi.index(last[1]))
        queue=[(0,begin)]; costs={begin:0}; prev={}; found=None
        while queue:
            cost,state=heapq.heappop(queue)
            if cost!=costs[state]: continue
            ix,iy,direction=state
            if (ix,iy)==goal: found=state; break
            for nx,ny,nd in [(ix-1,iy,1),(ix+1,iy,1),(ix,iy-1,2),(ix,iy+1,2)]:
                if not (0<=nx<len(xi) and 0<=ny<len(yi)): continue
                p0=(xi[ix],yi[iy]); p1=(xi[nx],yi[ny])
                if not clear(p0,p1): continue
                nc=cost+abs(p1[0]-p0[0])+abs(p1[1]-p0[1])+(35 if direction and direction!=nd else 0)
                ns=(nx,ny,nd)
                if nc<costs.get(ns,float('inf')):
                    costs[ns]=nc;prev[ns]=state;heapq.heappush(queue,(nc,ns))
        if found is None: raise ValueError('No diagram route '+flow['source']+' -> '+flow['target'])
        middle=[];state=found
        while True:
            middle.append((xi[state[0]],yi[state[1]]))
            if state==begin: break
            state=prev[state]
        pts=[start]+list(reversed(middle))+[end]
        simplified=[]
        for point in pts:
            if simplified and point==simplified[-1]: continue
            while len(simplified)>=2 and ((simplified[-2][0]==simplified[-1][0]==point[0]) or (simplified[-2][1]==simplified[-1][1]==point[1])):
                simplified.pop()
            simplified.append(point)
        result.append((f"{model['id']}_F{i+1}",flow,simplified))
    return result


def bpmn(model):
    pid = model['id']
    root=ET.Element('{'+NS['bpmn']+'}definitions', {'id':pid+'_Definitions', 'targetNamespace':'https://lifetracker.local/processes', 'exporter':'LifeTracker process builder', 'exporterVersion':'0.1'})
    proc=el(root,'bpmn:process',{'id':pid,'name':model['title'],'isExecutable':'false'})
    el(proc,'bpmn:documentation',text='Design model; proposed rules; algorithms '+', '.join(model['algorithms']))
    lane_set=el(proc,'bpmn:laneSet',{'id':pid+'_LaneSet'})
    for i,name in enumerate(model['lanes']):
        lane=el(lane_set,'bpmn:lane',{'id':f'{pid}_Lane{i}','name':name})
        for n in model['nodes']:
            if n['lane']==i:
                el(lane,'bpmn:flowNodeRef',text=pid+'_'+n['id'])
    bounds=layout(model); routes=paths(model,bounds)
    for n in model['nodes']:
        attrs={'id':pid+'_'+n['id'],'name':n['name']}
        outgoing=[(fid,f) for fid,f,_ in routes if f['source']==n['id']]
        incoming=[fid for fid,f,_ in routes if f['target']==n['id']]
        if n['type']=='exclusiveGateway':
            attrs['gatewayDirection']='Diverging' if len(outgoing)>1 else 'Converging'
            default=next((fid for fid,f in outgoing if f['default']),None)
            if default: attrs['default']=default
        node=el(proc,'bpmn:'+n['type'],attrs)
        for fid in incoming: el(node,'bpmn:incoming',text=fid)
        for fid,_ in outgoing: el(node,'bpmn:outgoing',text=fid)
        if n['type']=='intermediateCatchEvent':
            timer=el(node,'bpmn:timerEventDefinition',{'id':pid+'_'+n['id']+'_Timer'})
            el(timer,'bpmn:timeDate',{'{'+NS['xsi']+'}type':'bpmn:tFormalExpression'},text='${next_allowed_at}')
    nodes={n['id']:n for n in model['nodes']}
    for fid,f,_ in routes:
        attrs={'id':fid,'sourceRef':pid+'_'+f['source'],'targetRef':pid+'_'+f['target']}
        if f['label']: attrs['name']=f['label']
        flow=el(proc,'bpmn:sequenceFlow',attrs)
        if nodes[f['source']]['type']=='exclusiveGateway' and not f['default']:
            el(flow,'bpmn:conditionExpression',{'{'+NS['xsi']+'}type':'bpmn:tFormalExpression'},text=f['label'])
    diagram=el(root,'bpmndi:BPMNDiagram',{'id':pid+'_Diagram'})
    plane=el(diagram,'bpmndi:BPMNPlane',{'id':pid+'_Plane','bpmnElement':pid})
    width=max(x+w for x,y,w,h in bounds.values())+70
    for i in range(len(model['lanes'])):
        shape=el(plane,'bpmndi:BPMNShape',{'id':f'{pid}_Lane{i}_DI','bpmnElement':f'{pid}_Lane{i}','isHorizontal':'true'})
        el(shape,'dc:Bounds',dict(x='20',y=str(80+i*160),width=str(width-30),height='160'))
    for n in model['nodes']:
        attrs={'id':pid+'_'+n['id']+'_DI','bpmnElement':pid+'_'+n['id']}
        if n['type']=='exclusiveGateway': attrs['isMarkerVisible']='true'
        shape=el(plane,'bpmndi:BPMNShape',attrs)
        x,y,w,h=bounds[n['id']]
        el(shape,'dc:Bounds',dict(x=str(x),y=str(y),width=str(w),height=str(h)))
        if 'Event' in n['type'] or n['type'].endswith('Gateway'):
            label=el(shape,'bpmndi:BPMNLabel')
            el(label,'dc:Bounds',dict(x=str(x-45),y=str(y+h+7),width=str(w+90),height='40'))
    for fid,f,pts in routes:
        edge=el(plane,'bpmndi:BPMNEdge',{'id':fid+'_DI','bpmnElement':fid})
        for x,y in pts: el(edge,'di:waypoint',dict(x=str(x),y=str(y)))
        if f['label']:
            label=el(edge,'bpmndi:BPMNLabel')
            x,y=pts[0]
            el(label,'dc:Bounds',dict(x=str(x+4),y=str(y-24),width='140',height='20'))
    ET.indent(root)
    return ET.tostring(root,encoding='utf-8',xml_declaration=True).decode()+'\n'


def svg(model):
    bounds=layout(model); routes=paths(model,bounds)
    width=int(max(x+w for x,y,w,h in bounds.values())+75)
    height=100+len(model['lanes'])*160+45
    out=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img">',
         '<title>'+html.escape(model['title'])+'</title>',
         '<defs><marker id="arrow" markerWidth="9" markerHeight="9" refX="8" refY="4" orient="auto"><path d="M0 0 L8 4 L0 8 Z" fill="#334155"/></marker></defs>',
         '<rect width="100%" height="100%" fill="white"/>',
         f'<text x="22" y="30" font-family="Arial, sans-serif" font-size="21" font-weight="bold">{html.escape(model["id"]+" — "+model["title"])}</text>',
         '<text x="22" y="55" font-family="Arial, sans-serif" font-size="13" fill="#475569">BPMN 2.0 • проект • '+' / '.join(model['algorithms'])+'</text>']
    for i,name in enumerate(model['lanes']):
        y=80+i*160
        out.append(f'<rect x="20" y="{y}" width="{width-40}" height="160" fill="'+('#f8fafc' if i%2==0 else '#fff')+'" stroke="#cbd5e1"/>')
        out.append(f'<rect x="20" y="{y}" width="175" height="160" fill="#e2e8f0" stroke="#cbd5e1"/>')
        for j,line in enumerate(textwrap.wrap(name,18)):
            out.append(f'<text x="32" y="{y+70+j*18}" font-family="Arial, sans-serif" font-size="14">{html.escape(line)}</text>')
    for fid,f,pts in routes:
        out.append('<polyline points="'+' '.join(f'{x},{y}' for x,y in pts)+'" fill="none" stroke="#334155" stroke-width="1.4" marker-end="url(#arrow)"/>')
        if f['label']:
            x,y=pts[0]
            if len(pts)>1 and pts[1][1]>y: y += 30; x += 55
            out.append(f'<text x="{x+4}" y="{y-10}" font-family="Arial, sans-serif" font-size="11" fill="#334155">{html.escape(f["label"])}</text>')
    for n in model['nodes']:
        x,y,w,h=bounds[n['id']]; kind=n['type']; cx=x+w/2; cy=y+h/2
        if 'Event' in kind:
            out.append(f'<circle cx="{cx}" cy="{cy}" r="18" fill="#fff" stroke="#0f172a" stroke-width="'+('3.5' if kind=='endEvent' else '1.5')+'"/>')
            if kind=='intermediateCatchEvent':
                out.append(f'<circle cx="{cx}" cy="{cy}" r="14" fill="none" stroke="#0f172a"/><path d="M{cx} {cy-9} V{cy} H{cx+7}" fill="none" stroke="#0f172a"/>')
        elif kind.endswith('Gateway'):
            out.append(f'<polygon points="{cx},{y} {x+w},{cy} {cx},{y+h} {x},{cy}" fill="#fef3c7" stroke="#0f172a"/><path d="M{cx-6} {cy-6} L{cx+6} {cy+6} M{cx+6} {cy-6} L{cx-6} {cy+6}" stroke="#0f172a" stroke-width="2"/>')
        else:
            out.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="9" fill="#eff6ff" stroke="#334155"/>')
            marker={'userTask':'U','serviceTask':'S','businessRuleTask':'R'}.get(kind,'')
            out.append(f'<text x="{x+8}" y="{y+15}" font-family="Arial, sans-serif" font-size="11" fill="#64748b">{marker}</text>')
        lines=textwrap.wrap(n['name'],23)
        labely=(cy-(len(lines)-1)*8+4) if 'Task' in kind else y+h+17
        for j,line in enumerate(lines):
            out.append(f'<text x="{cx}" y="{labely+j*16}" text-anchor="middle" font-family="Arial, sans-serif" font-size="12">{html.escape(line)}</text>')
    out.append(f'<text x="22" y="{height-12}" font-family="Arial, sans-serif" font-size="12" fill="#64748b">U — user task; S — service task; R — business rule task. Полная BPMN-семантика и DI — в .bpmn.</text>')
    return '\n'.join(out+['</svg>'])+'\n'


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--check',action='store_true'); args=parser.parse_args()
    models=json.loads((ROOT/'docs/processes/catalog.json').read_text())
    stale=[]
    for m in models:
        for rel,content in [(f'docs/processes/bpmn/{m["id"]}.bpmn',bpmn(m)),(f'docs/processes/previews/{m["id"]}.svg',svg(m))]:
            p=ROOT/rel
            if args.check:
                if not p.exists() or p.read_text()!=content: stale.append(rel)
            else:
                p.parent.mkdir(parents=True,exist_ok=True); p.write_text(content)
    if stale:
        raise SystemExit('Outdated process artifacts: '+', '.join(stale))
    print(f'{len(models)} BPMN + SVG pairs '+('consistent' if args.check else 'built'))


if __name__=='__main__': main()
