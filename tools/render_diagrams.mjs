// Local-only PlantUML rendering with official @plantuml/core and installed Chrome.
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import {createHash} from 'node:crypto';
import {fileURLToPath,pathToFileURL} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const mods=process.env.DESIGN_NODE_MODULES || path.join(root,'tools/node_modules');
const {chromium}=await import(pathToFileURL(path.join(mods,'playwright-core/index.mjs')));
const engine=path.join(mods,'@plantuml/core');
const server=http.createServer((req,res)=>{
 const name=decodeURIComponent(new URL(req.url,'http://localhost').pathname).slice(1);
 if(!name){res.setHeader('Content-Type','text/html');res.end('<!doctype html><meta charset="utf-8"><script src="/viz-global.js"></script><script type="module">import {renderToString} from "/plantuml.js";window.renderDiagram=(source)=>new Promise((ok,fail)=>renderToString(source.split(String.fromCharCode(10)),ok,fail));</script>');return;}
 if(!/^[a-zA-Z0-9_.-]+\.js$/.test(name)){res.writeHead(404);res.end();return;}
 const p=path.join(engine,name);
 if(!fs.existsSync(p)){res.writeHead(404);res.end();return;}
 res.setHeader('Content-Type','text/javascript');res.end(fs.readFileSync(p));
});
await new Promise(ok=>server.listen(0,'127.0.0.1',ok));
let browser;
try{
 browser=await chromium.launch({executablePath:process.env.DESIGN_CHROME_PATH || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true});
 const page=await browser.newPage();
 page.on("pageerror",e=>console.error("Renderer page error:",e.message));
 const origin=`http://127.0.0.1:${server.address().port}`;
 await page.route('**/*',route=>route.request().url().startsWith(origin+'/')?route.continue():route.abort());
 await page.goto(origin);await page.waitForFunction(()=>typeof window.renderDiagram==='function');
 const sourceDir=path.join(root,'docs/architecture/diagrams');
 const target=path.join(sourceDir,'rendered');fs.mkdirSync(target,{recursive:true});
 const manifest={engine:'@plantuml/core',version:JSON.parse(fs.readFileSync(path.join(engine,'package.json'),'utf8')).version,diagrams:[]};
 for(const name of fs.readdirSync(sourceDir).filter(n=>n.endsWith('.puml')).sort()){
  const source=fs.readFileSync(path.join(sourceDir,name),'utf8');
  const svg=await page.evaluate(s=>window.renderDiagram(s),source);
  if(!svg.includes('<svg') || /Syntax Error|An error has occured|An error has occurred/i.test(svg)){console.error(svg.replace(/<[^>]+>/g,' ').slice(0,1500));throw Error('Render failed '+name);}
  fs.writeFileSync(path.join(target,name.replace('.puml','.svg')),svg);console.log('Rendered',name);
  manifest.diagrams.push({source:name,source_sha256:createHash('sha256').update(source).digest('hex'),output:'rendered/'+name.replace('.puml','.svg'),output_sha256:createHash('sha256').update(svg).digest('hex')});
 }
 fs.writeFileSync(path.join(sourceDir,'render-manifest.json'),JSON.stringify(manifest,null,2)+'\n');
}finally{if(browser)await browser.close();await new Promise(ok=>server.close(ok));}
