import os,json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
private=json.loads((root/'LocalSettings.json').read_text(encoding='utf-8-sig'))
needles=[os.environ['USERNAME'],os.environ['USERPROFILE'],private['Uid'],private['GamePath'],private['TemplatePath'],'D:'+chr(92)+'TeyvatGuide']
failures=[]
public=root/'release/default'
for p in public.rglob('*'):
    if not p.is_file():continue
    if p.suffix.lower() in {'.pdb','.xlsx','.lnk'} or p.name in {'LocalSettings.json','template-map.json'}:
        failures.append((str(p.relative_to(public)),'不应发布的文件'))
    data=p.read_bytes()
    for term in needles:
        if term and any(term.encode(enc) in data for enc in ['utf-8','utf-16le']):
            failures.append((str(p.relative_to(public)),'个人信息标记'))
sources=list(root.glob('*.cs'))+list(root.glob('*.xaml'))+list(root.glob('*.csproj'))+list(root.glob('*.manifest'))+list(root.glob('*.md'))+list((root/'checks').glob('*.cs'))+list((root/'checks').glob('*.ps1'))+list((root/'checks').glob('*.py'))
sources+=[root/'Data/source-info.json']
for p in sources:
    text=p.read_text(encoding='utf-8-sig')
    if any(n and n in text for n in needles):failures.append((str(p.relative_to(root)),'源码中的个人信息标记'))
    if re.search(r'gh[pousr]_[A-Za-z0-9]{30,}|AKIA[A-Z0-9]{16}|sk-proj-[A-Za-z0-9_-]{30,}',text):
        failures.append((str(p.relative_to(root)),'疑似密钥'))
print('公开版文件数量:',sum(p.is_file() for p in public.rglob('*')))
print('源码审计文件数量:',len(sources))
print('发现项:',failures)
assert not failures
archive=json.loads((root/'release/archive/LocalSettings.json').read_text(encoding='utf-8-sig'))
assert archive==private
print('PASS：公开版无已知个人路径、UID、模板、快照或 PDB；archive 个人设置保留')
