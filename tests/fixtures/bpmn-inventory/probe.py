import json, os, subprocess, sys, time, urllib.request, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import elements
exec(open("gen.py").read().split("import elements")[0])   # bring in B, wrap, etc.

BASE="http://localhost:8080/flowable-rest/service"
AUTH="Basic "+base64.b64encode(b"rest-admin:test").decode()
OUT=os.path.join(os.path.dirname(os.path.abspath(__file__)),"fixtures"); os.makedirs(OUT, exist_ok=True)

def curl(args):
    r=subprocess.run(["curl","-s","-u","rest-admin:test"]+args, capture_output=True, text=True, timeout=60)
    return r.stdout

def deploy(key, xml):
    path=os.path.join(OUT, key+".bpmn20.xml"); open(path,"w").write(xml)
    out=curl(["-F", f"file=@{path}", f"{BASE}/repository/deployments"])
    try: d=json.loads(out)
    except Exception: return None, out[:200]
    return (d.get("id"), None) if "id" in d else (None, (d.get("exception") or d.get("errorMessage") or out)[:220])

def start(key):
    out=curl(["-H","Content-Type: application/json","-d",json.dumps({"processDefinitionKey":key,"returnVariables":True}), f"{BASE}/runtime/process-instances"])
    try: d=json.loads(out)
    except Exception: return "?", out[:180]
    if "exception" in d or "errorMessage" in d:
        return "start-failed", (d.get("exception") or d.get("errorMessage"))[:200]
    return "started", d.get("id")

def cleanup(dep):
    if dep: curl(["-X","DELETE", f"{BASE}/repository/deployments/{dep}?cascade=true"])

results=[]
allitems=[(n,k,"supported") for n,k in elements.SUPPORTED]+[(n,k,"coming-soon") for n,k in elements.COMING_SOON]
for name,key,group in allitems:
    xml=B[key](key)
    dep,derr=deploy(key,xml)
    if dep is None:
        results.append({"name":name,"key":key,"group":group,"verdict":"fails-at-deployment","detail":derr})
        continue
    st,info=start(key)
    if st=="start-failed":
        results.append({"name":name,"key":key,"group":group,"verdict":"deploys-start-fails","detail":info})
    else:
        results.append({"name":name,"key":key,"group":group,"verdict":"deploys-and-starts","detail":info})
    cleanup(dep)
    time.sleep(0.05)
json.dump(results, open("results.json","w"), indent=1)
from collections import Counter
print("  probed:", len(results))
for v,c in Counter(r["verdict"] for r in results).items(): print(f"   {v}: {c}")
