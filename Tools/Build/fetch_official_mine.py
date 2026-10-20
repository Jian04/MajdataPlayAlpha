"""Fetch only MajPlay's own default Mine assets, pinned to its build commit."""
from pathlib import Path
import json, urllib.request, concurrent.futures, hashlib
root = Path(__file__).resolve().parents[2]
tree = json.loads((root/'Temp/official-build-tree.json').read_text(encoding='utf-8-sig'))
items = [x for x in tree['tree'] if x['path'].startswith('Skins/default/') and 'mine' in x['path'].lower() and x['path'].endswith('.png')]
def fetch(item):
    relative = item['path'][len('Skins/default/'):]
    target = root/'Assets/StreamingAssets/Skins/default'/relative
    target.parent.mkdir(parents=True, exist_ok=True)
    url = 'https://raw.githubusercontent.com/TeamMajdata/MajdataPlay_Build/'+tree['sha']+'/'+item['path']
    data = urllib.request.urlopen(url, timeout=45).read()
    assert hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()==item['sha']
    target.write_bytes(data)
    return relative
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
    files = list(executor.map(fetch, items))
(root/'Tools/Build/official-mine-assets.json').write_text(json.dumps({'repository':'TeamMajdata/MajdataPlay_Build','commit':tree['sha'],'files':files},indent=2)+'\n')
print('Verified official Mine assets:',len(files))
