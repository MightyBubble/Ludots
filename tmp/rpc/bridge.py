import json
from pathlib import Path
import sys
import urllib.request

method = sys.argv[1]
params = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}
body = json.dumps(dict(jsonrpc='2.0', id=1, method='ludots.'+method, params=params)).encode()
request = urllib.request.Request('http://127.0.0.1:47931/rpc', data=body, headers={'Content-Type':'application/json'})
with urllib.request.urlopen(request, timeout=30) as response:
    result = json.load(response)
if len(sys.argv) > 3:
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=True))
if 'error' in result:
    sys.exit(1)
