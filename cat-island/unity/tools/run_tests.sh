#!/bin/zsh
# 고양이 섬 Unity: EditMode + PlayMode 테스트 (스크린샷 포함) 배치 실행. 결과: Logs/catisland/, 스크린샷: Shots/
cd "${0:A:h}/.."
U=${UNITY:-/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity}
P=$PWD; L=$P/Logs/catisland; mkdir -p $L
python3 tools/sync_art.py >/dev/null || exit 1
# 가져오기 설정·Animator·동작 정보를 먼저 최신으로 (assets/ 가 바뀌었을 수 있다)
$U -batchmode -quit -projectPath $P -executeMethod CatIsland.EditorTools.CatArtImport.Run -logFile $L/import.log || { echo "Import Art 실패: $L/import.log"; exit 1; }
for plat in EditMode PlayMode; do
  $U -batchmode -projectPath $P -runTests -testPlatform $plat -testResults $L/$plat.xml -logFile $L/$plat.log
  python3 - $L/$plat.xml <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
print(sys.argv[1].split('/')[-1], r.attrib.get('result'), 'passed', r.attrib.get('passed'), '/', r.attrib.get('total'))
for tc in r.iter('test-case'):
    if tc.attrib['result'] not in ('Passed', 'Skipped'):
        m = tc.find('failure/message')
        print('  FAIL', tc.attrib['name'], ((m.text or '').strip().replace('\n', ' | ')[:300] if m is not None else ''))
PY
done
