#!/bin/zsh
# 고양이 섬 Unity: iOS 빌드.
#   tools/build_ios.sh sim [시뮬레이터UDID]   시뮬레이터 빌드 → 설치 → 실행
#   tools/build_ios.sh device                 실기기용 Xcode 프로젝트 (Builds/iOS-Device, 서명은 Xcode에서)
cd "${0:A:h}/.."
U=${UNITY:-/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity}
P=$PWD; L=$P/Logs/catisland; B=$P/Builds; mkdir -p $L
python3 tools/sync_art.py >/dev/null || exit 1
if [[ "$1" == "device" ]]; then
  $U -batchmode -projectPath $P -executeMethod CatIsland.EditorTools.CatIslandBuild.BuildIOSDevice -logFile $L/build_device.log || { echo "Unity 빌드 실패: $L/build_device.log"; exit 1; }
  echo "Xcode 프로젝트: $B/iOS-Device/Unity-iPhone.xcodeproj"; exit 0
fi
DEV=${2:-$(xcrun simctl list devices booted | grep -m1 -oE '[0-9A-F-]{36}')}
$U -batchmode -projectPath $P -executeMethod CatIsland.EditorTools.CatIslandBuild.BuildIOSSimulator -logFile $L/build_sim.log || { echo "Unity 빌드 실패: $L/build_sim.log"; exit 1; }
cd $B/iOS-Simulator && xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release -sdk iphonesimulator \
  -destination "id=$DEV" -derivedDataPath $B/DerivedSim CODE_SIGNING_ALLOWED=NO build > $L/xcode_sim.log 2>&1 || { grep error: $L/xcode_sim.log | head; exit 1; }
APP=$(find $B/DerivedSim/Build/Products/Release-iphonesimulator -maxdepth 1 -name "*.app" | head -1)
xcrun simctl terminate $DEV com.rowoon.CatIsland 2>/dev/null
xcrun simctl install $DEV "$APP" && xcrun simctl launch $DEV com.rowoon.CatIsland
