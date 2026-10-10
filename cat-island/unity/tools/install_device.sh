#!/bin/zsh
# 고양이 섬: 연결된 아이폰에 개발용으로 설치하고 실행한다 (Unity 기기 빌드 → Xcode 자동 서명 → 설치).
#   tools/install_device.sh [기기UDID] [--skip-unity]
#   팀 ID 는 TEAM 환경 변수 (기본: 키체인의 Apple Development 인증서 팀)
cd "${0:A:h}/.."
P=$PWD; L=$P/Logs/catisland; B=$P/Builds; mkdir -p $L
DEV=${1:-$(xcrun devicectl list devices 2>/dev/null | awk '/physical/ && /available/ && !/unavailable/ {for(i=1;i<=NF;i++) if ($i ~ /^[0-9A-F]{8}-[0-9A-F]{16}$/) print $i}' | head -1)}
[[ -z "$DEV" ]] && { echo "연결된 아이폰이 없어요 (케이블 연결, 잠금 해제, 이 컴퓨터 신뢰)"; exit 1; }
TEAM=${TEAM:-$(security find-certificate -c "Apple Development" -p 2>/dev/null | openssl x509 -noout -subject 2>/dev/null | sed -n 's/.*OU=\([A-Z0-9]*\).*/\1/p')}
[[ -z "$TEAM" ]] && { echo "Apple Development 인증서가 없어요 (Xcode > Settings > Accounts)"; exit 1; }
[[ "$2" == "--skip-unity" ]] || { zsh tools/build_ios.sh device || exit 1; }
git -C .. checkout -- unity/ProjectSettings/ProjectSettings.asset 2>/dev/null   # (빌드가 바꾸는 SDK 표시는 되돌린다)
cd $B/iOS-Device && xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release -sdk iphoneos -destination "generic/platform=iOS" \
  -derivedDataPath $B/DerivedDev -allowProvisioningUpdates DEVELOPMENT_TEAM=$TEAM CODE_SIGN_STYLE=Automatic build > $L/xcode_dev.log 2>&1 || { grep error: $L/xcode_dev.log | head; exit 1; }
APP=$(find $B/DerivedDev/Build/Products/Release-iphoneos -maxdepth 1 -name "*.app" | head -1)
xcrun devicectl device install app --device $DEV "$APP" >/dev/null && xcrun devicectl device process launch --device $DEV com.rowoon.CatIsland && echo "설치·실행: $DEV"
