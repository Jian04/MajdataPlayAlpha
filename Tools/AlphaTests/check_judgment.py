"""Verify upstream judgment methods against the recorded Alpha baseline."""
from pathlib import Path
import json
import re
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
git = shutil.which('git')
if not git:
    raise SystemExit('Git must be available on PATH.')
baseline = json.loads((root / 'ALPHA_BASELINE.json').read_text(encoding='utf-8'))['commit']

def methods(source):
    result = {}
    pattern = r'\b(?:void|bool|float|SensorCheckResult)\s+((?:Judge|Mine|Autoplay|DJAutoplay|SlideDJAuto|SensorCheck|TooLate|Check|HeadCheck|BodyCheck|ForceEnd|ConvertJudge|ConvertTo)[A-Za-z0-9_]*)\s*\([^;{}]*\)\s*\{'
    for match in re.finditer(pattern, source):
        end = match.end()
        depth = 1
        while depth:
            if source[end] == '{':
                depth += 1
            elif source[end] == '}':
                depth -= 1
            end += 1
        body = re.sub(r'\s+', '', source[match.start():end])
        result.setdefault(match.group(1), []).append(body)
    return result

checked = 0
for path in (root / 'Assets/Scripts/Scenes/Game/NoteBehaviours').glob('*.cs'):
    relative = path.relative_to(root).as_posix()
    original = subprocess.check_output([git, '-C', str(root), 'show', baseline + ':' + relative]).decode('utf-8-sig')
    modified = methods(path.read_text(encoding='utf-8-sig'))
    for name, bodies in methods(original).items():
        if modified.get(name) != bodies:
            raise SystemExit(f'Upstream judgment changed: {relative} {name}')
        checked += len(bodies)
print(f'PASS: {checked} upstream judgment, mine, autoplay and DJAuto methods unchanged.')
