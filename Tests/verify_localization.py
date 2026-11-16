"""Validate shipped Chinese UI catalogs without external packages or private fixtures."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
errors = []
counts = {}
for english_path in root.rglob('Localization/en-US.json'):
    if any(part in {'bin', 'obj', 'artifacts'} for part in english_path.relative_to(root).parts):
        continue
    english = json.loads(english_path.read_text(encoding='utf-8-sig'))
    counts[str(english_path.parent.relative_to(root))] = len(english)
    for locale in ('zh-CN', 'zh-Hant'):
        path = english_path.with_name(locale + '.json')
        translated = json.loads(path.read_text(encoding='utf-8-sig'))
        for key, source in english.items():
            value = translated.get(key)
            label = f'{path.relative_to(root)}:{key}'
            if not value:
                errors.append(label + ': missing translation')
                continue
            tokens = lambda s: sorted(re.findall(r'(?<!\{)\{\d+(?:[^{}]*)?\}(?!\})', s))
            if tokens(source) != tokens(value):
                errors.append(label + ': format placeholders differ')
            if re.search('[a-zA-Z]', source) and source == value and value not in {'X'}:
                errors.append(label + ': untranslated English')

print(json.dumps({'catalogs': counts, 'keys_per_locale': sum(counts.values()), 'errors': errors}, ensure_ascii=False, indent=2))
raise SystemExit(bool(errors))
