#!/bin/zsh
# 앱스토어 스크린샷: StoreShots 테스트로 게임 화면을 찍고(Shots/store_raw), store_shots.py 가 위쪽 크림색 띠에 문구를 얹는다(Shots/store).
cd "${0:A:h}/.."
U=${UNITY:-/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity}
P=$PWD; L=$P/Logs/catisland; mkdir -p $L
python3 tools/sync_art.py >/dev/null || exit 1
$U -batchmode -projectPath $P -runTests -testPlatform PlayMode -testFilter CatIsland.Tests.StoreShots -testResults $L/store.xml -logFile $L/store.log
python3 tools/store_shots.py
