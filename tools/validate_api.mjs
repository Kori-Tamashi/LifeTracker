import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const mods=process.env.DESIGN_NODE_MODULES||path.join(here,'node_modules');
const {default:Parser}=await import(pathToFileURL(path.join(mods,'@apidevtools/swagger-parser/lib/index.js')));
const api=await Parser.validate(path.join(here,'../docs/architecture/contracts/openapi.yaml'));
console.log(`OpenAPI ${api.openapi} valid; ${Object.values(api.paths).reduce((n,p)=>n+Object.keys(p).length,0)} operations`);
