"""Import the user's Play-format skin pack, filling missing newer sprites."""
from pathlib import Path
import shutil
import json

root = Path(__file__).resolve().parents[2]
pack = Path.home() / 'Downloads/Skins 2025-04-17/Skins'
if not (pack / 'Deluxe/TapSkins/tap.png').is_file():
    raise FileNotFoundError('Downloaded skin pack missing: ' + str(pack))
# Cache the original bundled assets before overlaying the older pack.
fallback = root / '.build/skin-fallback'
if not fallback.exists():
    shutil.copytree(root / 'Assets/StreamingAssets/Skins/default', fallback,
                    ignore=shutil.ignore_patterns('*.meta'))
for source in sorted(pack.iterdir()):
    if not source.is_dir():
        continue
    target = root / 'Assets/StreamingAssets/Skins' / source.name
    # Deluxe has empty Hold/Judge folders. Fill from this pack's companion
    # first; never let the bundled legacy textures mask these omissions.
    sources = [source, pack / (source.name + '-C' if not source.name.endswith('-C') else source.name[:-2])]
    sources += [pack / 'Deluxe-C', pack / 'default-C', pack / 'default']
    selected = {}
    for candidate in sources:
        for asset in candidate.rglob('*.png'):
            selected.setdefault(asset.relative_to(candidate), asset)
    for asset in fallback.rglob('*.png'):
        relative = asset.relative_to(fallback)
        if 'mine' in asset.name.lower():
            selected.setdefault(relative, asset)
    target.mkdir(parents=True, exist_ok=True)
    # These folders are the generated import, not the original downloaded pack.
    assert target.resolve().is_relative_to((root / 'Assets/StreamingAssets/Skins').resolve())
    for asset in target.rglob('*.png'):
        if asset.relative_to(target) not in selected:
            asset.unlink()
    for relative, asset in selected.items():
        output = target / relative
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(asset, output)
    supplied = list(source.rglob('*.png'))
    missing = [str(f.relative_to(fallback)) for f in fallback.rglob('*.png')
               if not (source / f.relative_to(fallback)).exists()]
    (target / 'SOURCE.json').write_text(json.dumps({
        'name': source.name, 'source': str(source),
        'suppliedSprites': len(supplied),
        'resolvedSprites': {str(relative): str(asset) for relative, asset in selected.items()},
        'fallbackSprites': [str(relative) for relative, asset in selected.items() if asset.is_relative_to(fallback)]
    }, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    assert len(supplied) > 50, 'Incomplete skin: ' + source.name
    print(source.name, ':', len(supplied), 'own sprites;', len(selected), 'resolved sprites;',
          sum(asset.is_relative_to(fallback) for asset in selected.values()), 'native Mine fallbacks')
