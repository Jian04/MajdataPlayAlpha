"""Prepare a separate Unity 6000.6 build copy; never edit pinned vendor sources."""
from pathlib import Path
import json
import re
import shutil

root = Path(__file__).resolve().parents[2]
check = root / '.build/unity-check'
if not (check / 'Assets').exists():
    for name in ('Assets', 'ProjectSettings', 'Packages'):
        shutil.copytree(root / name, check / name,
                        ignore=shutil.ignore_patterns('.git', '._*', 'bin', 'obj'))
    manifest = check / 'Packages/manifest.json'
    data = json.loads(manifest.read_text(encoding='utf-8'))
    for package in ('com.unity.sdk.linux-x86_64', 'com.unity.toolchain.linux-x86_64-linux',
                    'com.unity.toolchain.win-x86_64-linux'):
        data['dependencies'].pop(package, None)
    manifest.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')

for name in ('README.md', 'ALPHA_COMPATIBILITY.md', 'FILTER_CANDIDATES.md', 'MOBILE_RELEASE.md'):
    shutil.copyfile(root / name, check / name)

shutil.copytree(root / 'Assets/Scripts', check / 'Assets/Scripts', dirs_exist_ok=True)
shutil.copytree(root / 'Assets/Rendering', check / 'Assets/Rendering', dirs_exist_ok=True)
shutil.copytree(root / 'Assets/Plugins/MajSimai/Runtime', check / 'Assets/Plugins/MajSimai/Runtime', dirs_exist_ok=True)
shutil.copytree(root / 'Assets/StreamingAssets/Skins', check / 'Assets/StreamingAssets/Skins', dirs_exist_ok=True)
for asset in (check / 'Assets/StreamingAssets/Skins').rglob('*.png'):
    if not (root / asset.relative_to(check)).exists():
        asset.unlink()
        meta = asset.with_suffix(asset.suffix + '.meta')
        if meta.exists():
            meta.unlink()
shutil.copyfile(root / 'ProjectSettings/ProjectSettings.asset', check / 'ProjectSettings/ProjectSettings.asset')
for path in (root / 'Assets/Resources').glob('Alpha*'):
    shutil.copyfile(path, check / 'Assets/Resources' / path.name)
shutil.copytree(root / 'Assets/Resources/NoiseZone', check / 'Assets/Resources/NoiseZone', dirs_exist_ok=True)
shutil.copyfile(root / 'Assets/Plugins/MajdataPlay/UnsafeKit/RefDisposable.cs',
                check / 'Assets/Plugins/MajdataPlay/UnsafeKit/RefDisposable.cs')
shutil.copyfile(root / 'Assets/Plugins/MajdataPlay/Net/Curl/Core/PInvoke/LibCurl.cs',
                check / 'Assets/Plugins/MajdataPlay/Net/Curl/Core/PInvoke/LibCurl.cs')

# The editor update changed identity APIs, tree views and layout/text interfaces.
# These adapters apply consistently throughout the temporary copy only.
for path in (check / 'Assets').rglob('*.cs'):
    try:
        source = path.read_text(encoding='utf-8-sig')
    except UnicodeDecodeError:
        continue
    changed = source.replace('AssetDatabase.GetAssetPath(activeObject.GetInstanceID())',
                             'AssetDatabase.GetAssetPath(activeObject)')
    changed = changed.replace('GetInstanceID()', 'GetEntityId().GetHashCode()')
    changed = changed.replace('.colliderInstanceID', '.colliderEntityId.GetHashCode()')
    if path.name == 'MultiHttpTest.cs':
        changed = changed.replace('UnityHttpMessageHandler', 'MajdataPlay.Net.Handlers.UnityHttpMessageHandler')
    if path.name == 'TMPMarqueeText.cs':
        changed = changed.replace('_sourceText.enableWordWrapping;',
                                  '(_sourceText.textWrappingMode != TextWrappingModes.NoWrap);')
        changed = changed.replace('_sourceText.enableWordWrapping = _sourceWasWordWrapping;',
                                  '_sourceText.textWrappingMode = _sourceWasWordWrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;')
        changed = re.sub(r'(\w+)\.enableWordWrapping = false;',
                         r'\1.textWrappingMode = TextWrappingModes.NoWrap;', changed)
    if 'UniTask/src/Editor' in path.as_posix() and 'using TreeView =' not in changed:
        aliases = '\nusing TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;\nusing TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;\nusing TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;\n'
        changed = changed.replace('using UnityEngine;', 'using UnityEngine;' + aliases)
    if path.name in ('HB_TEXTBlock.cs', 'SVGLoader.cs') and 'maxWidth =>' not in changed:
        changed = re.sub(r'(class \w+ : MonoBehaviour, ILayoutElement \{)',
                         r'\1\n float ILayoutElement.maxWidth => -1; float ILayoutElement.maxHeight => -1;', changed)
    if changed != source:
        path.write_text(changed, encoding='utf-8')
print('Ready:', check)
