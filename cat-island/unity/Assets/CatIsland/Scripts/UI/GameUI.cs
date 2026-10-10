using System;
using System.Collections.Generic;
using System.Linq;
using CatIsland.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CatIsland.UI
{
    /// <summary>화면이 3D 섬에 부탁하는 일 (꾸미기 배치, 사진, 구역 이동). 테스트에서는 비워 둔다.</summary>
    public interface IWorldHooks
    {
        void BeginPlace(string item, Action<bool> done);      // 섬 위에서 자리를 고르게 한다 (놓으면 true)
        void ShowZone(Zone z);
        void Capture(Action<string> savedFile);              // 사진 모드 촬영 → 기기 안 파일 이름
        void Refresh();                                        // 상태가 바뀌었다: 용품·고양이 다시 맞추기
        Vector2? CatScreenPos(string uid);                     // (말풍선 위치)
    }

    /// <summary>
    /// 게임 화면 전체. 가운데 70 %는 섬, 버튼은 위아래 띠에만 (기획서 3부 7장). 모든 창은 아래에서 올라오는 둥근 카드.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        public CatIsland.Game.Game G;
        public IWorldHooks World;
        public static GameUI Instance { get; private set; }
        public RectTransform Root { get; private set; }        // 안전 영역
        public RectTransform SheetLayer { get; private set; }
        Text coinText, jellyText, hintText; RectTransform idleBtn, guestBtn, zoneBtn, hintCard; Text idleAmt; Image tasksBadge, catsBadge, decoBadge;
        readonly List<GameObject> openSheets = new List<GameObject>();
        public bool SheetOpen => openSheets.Count > 0;
        public string LastToast { get; private set; }
        float refreshAt;

        public static GameUI Create(CatIsland.Game.Game g, IWorldHooks world)
        {
            var go = new GameObject("GameUI"); var ui = go.AddComponent<GameUI>(); ui.G = g; ui.World = world; Instance = ui;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(390, 844); sc.matchWidthOrHeight = .5f;
            go.AddComponent<GraphicRaycaster>();
            if (!FindAnyObjectByType<EventSystem>()) { var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<InputSystemUIInputModule>(); }
            ui.Root = Kit.Rect(go.transform, "Safe"); Kit.Fill(ui.Root); ui.Root.gameObject.AddComponent<SafeArea>().Apply();
            ui.BuildHud();
            ui.SheetLayer = Kit.Rect(go.transform, "Sheets"); Kit.Fill(ui.SheetLayer);
            ui.Refresh();
            return ui;
        }

        /// <summary>손가락이 화면 버튼·창 위에 있는가 (그러면 섬 쓰다듬기로 보내지 않는다).</summary>
        public static bool PointerOverUI(int pointerId = -1)
        {
            var es = EventSystem.current; if (!es) return false;
            if (Instance != null && Instance.SheetOpen) return true;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        // ================================================================ 위아래 띠
        void BuildHud()
        {
            // 위: 코인 · 젤리 · 설정
            var top = Kit.Rect(Root, "Top"); Kit.Anchor(top, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -8), new Vector2(370, 48));
            coinText = Pill(top, UIIcon.Coin, new Vector2(0, 0), new Vector2(0, .5f), () => Open(BuildShop));
            jellyText = Pill(top, UIIcon.Jelly, new Vector2(132, 0), new Vector2(0, .5f), () => Open(BuildJellyShop), plus: true);
            var gear = Kit.IconBtn(top, UIIcon.Gear, null, () => Open(BuildSettings), 46); Kit.Anchor((RectTransform)gear.transform.parent, new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(46, 46));

            // 오른쪽 떠 있는 버튼: 모아 둔 선물, 손님, 구역
            var side = Kit.Rect(Root, "Side"); Kit.Anchor(side, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -70), new Vector2(72, 250));
            Kit.VList(side, 8).childAlignment = TextAnchor.UpperRight; side.GetComponent<VerticalLayoutGroup>().childControlWidth = false; side.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            var ib = Kit.IconBtn(side, UIIcon.Gift, "", () => Open(BuildIdle), 60, Theme.Butter); idleBtn = (RectTransform)ib.transform.parent; idleAmt = idleBtn.GetComponentInChildren<Text>();
            var gb = Kit.IconBtn(side, UIIcon.Fish, "손님", () => Open(BuildGuest), 60, Theme.Sky); guestBtn = (RectTransform)gb.transform.parent;
            var zb = Kit.IconBtn(side, UIIcon.Fence, Str.Yard, ToggleZone, 60); zoneBtn = (RectTransform)zb.transform.parent;

            // 처음 7일 안내 (한 줄 카드)
            var hc = Kit.Card(Root, "Hint"); hintCard = (RectTransform)hc.transform.parent; Kit.Anchor(hintCard, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 112), new Vector2(330, 46));
            hintText = Kit.Label(hc.transform, "", Theme.Body); Kit.Fill(hintText.rectTransform, 12, 4, 12, 4);

            // 아래: 상점 · 꾸미기 · 할 일 · 고양이 · 사진
            var bot = Kit.Rect(Root, "Bottom"); Kit.Anchor(bot, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(370, 88));
            var h = Kit.HList(bot, 10); h.childAlignment = TextAnchor.LowerCenter;
            Kit.IconBtn(bot, UIIcon.Shop, Str.Shop, () => Open(BuildShop));
            var db = Kit.IconBtn(bot, UIIcon.Bag, Str.Decorate, () => Open(BuildBag)); decoBadge = Kit.Badge(db.transform);
            var tb = Kit.IconBtn(bot, UIIcon.Tasks, Str.Tasks, () => Open(BuildTasks)); tasksBadge = Kit.Badge(tb.transform);
            var cb = Kit.IconBtn(bot, UIIcon.Cat, Str.Cats, () => Open(BuildCats)); catsBadge = Kit.Badge(cb.transform);
            Kit.IconBtn(bot, UIIcon.Camera, Str.Photo, OpenPhoto);
        }
        Text Pill(RectTransform parent, UIIcon icon, Vector2 pos, Vector2 anchor, Action onClick, bool plus = false)
        {
            var bg = Kit.Box(parent, "Pill_" + icon, Theme.Cream, 22); Kit.Anchor(bg.rectTransform, anchor, new Vector2(0, .5f), pos, new Vector2(124, 44));
            var line = Kit.Box(bg.transform, "Line", Theme.Cocoa, 22, 2); Kit.Fill(line.rectTransform); line.raycastTarget = false;
            var ic = Kit.IconImage(bg.transform, icon, 34); Kit.Anchor(ic.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(5, 0), new Vector2(34, 34));
            var t = Kit.Label(bg.transform, "0", Theme.Body, null, TextAnchor.MiddleRight); Kit.Fill(t.rectTransform, 40, 2, plus ? 30 : 12, 2);
            if (plus) { var p = Kit.IconImage(bg.transform, UIIcon.Plus, 20); Kit.Anchor(p.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-8, 0), new Vector2(20, 20)); }
            var b = bg.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.None; b.onClick.AddListener(() => onClick()); bg.gameObject.AddComponent<Press>();
            return t;
        }

        void Update()
        {
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + .5f; Refresh();
        }
        public void Refresh()
        {
            if (G?.S == null) return;
            coinText.text = G.S.coins.ToString("N0"); jellyText.text = G.S.jelly.ToString("N0");
            idleBtn.gameObject.SetActive(G.S.idleBank > 0); idleAmt.text = G.S.idleBank.ToString("N0");
            guestBtn.gameObject.SetActive(G.GuestHere && !G.S.guestTreated || G.CanAdoptGuest);
            zoneBtn.gameObject.SetActive(G.S.zonesUnlocked.Contains(1) || G.S.onboardingStep >= 7);
            tasksBadge.enabled = G.S.tasks.Any(t => !t.claimed && t.progress >= Catalog.DailyTasks.First(d => d.id == t.id).goal) || G.CanAttend;
            catsBadge.enabled = G.S.cats.Any(c => c.status == "home" && (c.hunger < .25f || c.thirst < .25f));
            decoBadge.enabled = G.S.cats.Count > 0 && G.S.floor.Count == 0 && G.S.tileBag.Any(c => c.n > 0);   // (받은 데크 타일을 아직 한 장도 안 깔았다: 꾸미기에서 깔아 보기)
            string hint = Str.Hint(G.OnboardingHint()); hintCard.gameObject.SetActive(!string.IsNullOrEmpty(hint) && !SheetOpen && !TileMode.Active); hintText.text = hint;   // (바닥 깔기 막대와 겹치지 않게)
        }

        // ================================================================ 창 (아래에서 올라오는 카드)
        /// <summary>시연 둘러보기 (CATISLAND_TOUR=1, 시뮬레이터 화면 점검용): 화면을 차례로 연다. 이용자는 볼 일이 없다.</summary>
        public System.Collections.IEnumerator Tour(float each)
        {
            Func<RectTransform, string>[] screens = { BuildIdle, BuildShop, BuildOdds, BuildJellyShop, BuildBag, BuildTasks, BuildCats, BuildSettings };
            foreach (var b in screens) { Open(b); yield return new WaitForSecondsRealtime(each); }
            shopTab = "floor"; Open(BuildShop); yield return new WaitForSecondsRealtime(each); shopTab = "all";   // (상점 바닥 탭: 데크 타일)
            CatMaker.Open(this); yield return new WaitForSecondsRealtime(each);
            StarLandUI.Open(this); yield return new WaitForSecondsRealtime(each);
            CloseAll();
        }

        /// <summary>지금 열린 창의 윗변 (화면 높이 비율, 창이 없으면 1). 카메라가 창 위로 고양이를 잡는 데 쓴다 (IslandCamera).</summary>
        public float SheetTop => SheetOpen ? sheetTop : 1f;
        float sheetTop = .78f;
        /// <param name="top">창 윗변 (화면 비율). 고양이 만들기는 낮게 열어 위로 3D 미리보기 고양이가 보이게 한다.</param>
        /// <summary>결제 상품 가격: 앱스토어가 알려 준 그 나라 가격(통화 포함). 아직 못 받았으면 원화 기준 가격.</summary>
        public string Price(string productId, int krw) => G?.Store is CatIsland.Game.IosServices ios && ios.LocalPrices.TryGetValue(productId, out var local) && !string.IsNullOrEmpty(local) ? local : Str.Krw(krw);
        public void Open(Func<RectTransform, string> build, float top = .78f)
        {
            CloseAll();
            sheetTop = top;
            var shade = Kit.Box(SheetLayer, "Shade", top < .7f ? new Color(Theme.Shade.r, Theme.Shade.g, Theme.Shade.b, Theme.Shade.a * .35f) : Theme.Shade, 0); Kit.Fill(shade.rectTransform);
            var sb = shade.gameObject.AddComponent<Button>(); sb.transition = Selectable.Transition.None; sb.onClick.AddListener(CloseAll);
            var card = Kit.Box(shade.transform, "Sheet", Theme.Cream, 24); var rt = card.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, top); rt.offsetMin = new Vector2(8, 8); rt.offsetMax = new Vector2(-8, 0);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;   // (카드 안을 눌러도 닫히지 않게)
            var body = Kit.Rect(rt, "Body"); Kit.Fill(body, 16, 62, 16, 16);
            string title = build(body);
            var t = Kit.Label(rt, title, Theme.Title, null, TextAnchor.MiddleLeft, "Title"); Kit.Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -10), new Vector2(260, 44));
            var close = Kit.IconBtn(rt, UIIcon.Close, null, CloseAll, 44); Kit.Anchor((RectTransform)close.transform.parent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -8), new Vector2(44, 44));
            openSheets.Add(shade.gameObject);
            StartCoroutine(SlideUp(rt));
            Refresh();
        }
        System.Collections.IEnumerator SlideUp(RectTransform rt)
        {
            var end = rt.anchoredPosition; float h = rt.rect.height + 40;
            for (float t = 0; t < .22f; t += Time.unscaledDeltaTime) { if (!rt) yield break; float u = 1 - Mathf.Pow(1 - t / .22f, 3); rt.anchoredPosition = end + new Vector2(0, -h * (1 - u)); yield return null; }
            if (rt) rt.anchoredPosition = end;   // (올라오는 사이 창이 닫혔으면 그만)
        }
        public bool InStarLand;
        public void CloseAll()
        {
            bool had = openSheets.Count > 0; foreach (var s in openSheets) if (s) Destroy(s); openSheets.Clear(); World?.Refresh(); Refresh();
            if (had && G != null) G.MaybeInterstitial(InStarLand);   // (전면 광고는 화면이 넘어갈 때만, 규칙은 Game.ShouldShowInterstitial)
            InStarLand = false;
        }
        public void Toast(string msg)
        {
            LastToast = msg;
            var c = Kit.Card(SheetLayer, "Toast"); var rt = (RectTransform)c.transform.parent; Kit.Anchor(rt, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(330, 64));
            var t = Kit.Label(c.transform, msg, Theme.Body); Kit.Fill(t.rectTransform, 14, 6, 14, 6);
            Destroy(rt.gameObject, 2.4f);
        }
        static RectTransform Row(RectTransform parent, float h = 64)
        {
            var c = Kit.Box(parent, "Row", Theme.MilkTea, 14); Kit.Size(c, -1, h);
            var r = Kit.Rect(c.transform, "In"); Kit.Fill(r, 12, 6, 10, 6); Kit.HList(r, 10).childAlignment = TextAnchor.MiddleLeft; return r;
        }
        static Text RowText(RectTransform row, string s, int size = Theme.Body, float flex = 1, Color? color = null) { var t = Kit.Label(row, s, size, color, TextAnchor.MiddleLeft); Kit.Size(t, -1, 40, flex); return t; }

        // ---------------------------------------------------------------- 모아 둔 선물 (방치 보상)
        string BuildIdle(RectTransform body)
        {
            Kit.VList(body, 14);
            var name = G.S.cats.FirstOrDefault(c => c.status == "home")?.name ?? "고양이";
            Kit.Size(Kit.Label(body, Str.Welcome + " " + Str.IdleReady(name, (int)G.S.idleBank), Theme.Body), -1, 60);
            Kit.Size(Kit.Btn(body, Str.IdleTake, () => { int c = G.CollectIdle(); CloseAll(); GameFeel.Coins(); Toast($"코인 {c}개를 받았어요"); }, Kit.Style.Primary, UIIcon.Coin), -1, 54);
            if (G.AdAvailable(Catalog.AdSpot.IdleDouble))
            {
                Kit.Size(Kit.Label(body, Str.AdHonest, Theme.Caption, Theme.Latte), -1, 30);
                Kit.Size(Kit.Btn(body, Str.IdleDouble, () => G.CollectIdleDoubled((ok, c) => { CloseAll(); GameFeel.Coins(); Toast(ok ? $"코인 {c}개를 받았어요 (두 배)" : $"코인 {c}개를 받았어요"); }), Kit.Style.Secondary, UIIcon.AdPlay), -1, 50);
            }
            return Str.IdleTitle;
        }

        // ---------------------------------------------------------------- 상점
        string shopTab = "all";
        string BuildShop(RectTransform body)
        {
            Kit.VList(body, 10);
            var tabs = Kit.Rect(body, "Tabs"); Kit.HList(tabs, 6); Kit.Size(tabs, -1, 50);
            foreach (var (id, ko) in new[] { ("all", "전체"), ("food", "먹이"), ("toy", "장난감"), ("furn", "가구"), ("floor", "바닥"), ("yard", "마당"), ("season", "계절") })
            { var tb = Kit.Btn(tabs, ko, () => { shopTab = id; Open(BuildShop); }, shopTab == id ? Kit.Style.Primary : Kit.Style.Secondary); tb.GetComponentInChildren<Text>().fontSize = Theme.Caption; Kit.Size(tb, 46, 46); }
            Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 420).flexibleHeight = 1;
            // 데크 타일 (바닥 탭): 9장 묶음
            if (shopTab == "floor")
                foreach (var t in Catalog.Tiles)
                {
                    var r = Row(list, 66); Kit.Size(TileIcon(r, t.id, 44), 44, 44);
                    RowText(r, $"{t.ko}  · {t.pack}장");
                    int own = G.Tiles(t.id) + G.S.floor.Count(f => f.id == t.id);
                    if (own > 0) RowText(r, $"{own}장", Theme.Caption, 0, Theme.Latte);
                    var tile = t;
                    Kit.Size(Kit.Btn(r, Str.Price(t.price, false), () => { if (G.BuyTiles(tile.id)) { Toast($"{Josa.EulReul(tile.ko)} {tile.pack}장 샀어요. 꾸미기에서 깔아 봐요"); Open(BuildShop); } else Toast("코인이 조금 모자라요"); }, Kit.Style.Primary, UIIcon.Coin), 108, 46);
                }
            foreach (var d in Catalog.Items.Where(d => shopTab != "floor" && G.InShop(d) && TabHas(d)).OrderBy(d => d.limited ? 0 : 1).ThenBy(d => d.price))
            {
                var r = Row(list, 66);
                Kit.Size(ItemIcon(r, d, 44), 44, 44);
                RowText(r, d.ko + (d.limited ? "  · 한정" : ""));
                int own = Bag.Get(G.S.inventory, d.id) + G.S.placed.Count(p => p.item == d.id);
                if (own > 0 && !d.consumable) RowText(r, $"{own}개", Theme.Caption, 0, Theme.Latte);
                var item = d;
                Kit.Size(Kit.Btn(r, Str.Price(d.price, d.currency == Currency.Jelly), () => BuyItem(item), Kit.Style.Primary, d.currency == Currency.Jelly ? UIIcon.Jelly : UIIcon.Coin), 108, 46);
            }
            // 무료 뽑기 (확률 공개)
            var box = Row(list, 66); Kit.Size(Kit.IconImage(box, UIIcon.Gift, 44), 44, 44); RowText(box, "무료 선물 상자");
            Kit.Size(Kit.Btn(box, "확률", () => Open(BuildOdds), Kit.Style.Secondary), 64, 46);
            if (G.AdAvailable(Catalog.AdSpot.FreeBox)) Kit.Size(Kit.Btn(box, "광고", () => G.FreeBoxAd(it => { CloseAll(); Toast(it != null ? $"{Josa.IGa(Catalog.Item(it).ko)} 나왔어요" : Str.AdLater); }), Kit.Style.Primary, UIIcon.AdPlay), 86, 46);
            return Str.Shop;
        }
        bool TabHas(ItemDef d) => shopTab switch
        {
            "food" => d.category == ItemCategory.Food, "toy" => d.category == ItemCategory.Toy, "furn" => d.category == ItemCategory.Furniture || d.category == ItemCategory.Tower || d.category == ItemCategory.Deco,
            "yard" => d.zone == Zone.Yard, "season" => d.limited, _ => true,
        };
        void BuyItem(ItemDef d)
        {
            if (!G.Buy(d.id)) { Toast(d.currency == Currency.Coin ? Str.NotEnoughCoins : Str.NotEnoughJelly); return; }
            if (d.consumable) { Toast($"{Josa.EulReul(d.ko)} 가방에 넣었어요"); Open(BuildShop); return; }
            // 산 용품은 바로 섬에 놓아 본다 (고양이가 바로 써 보는 것이 보상: 기획서 2-2)
            CloseAll(); PlaceFromBag(d.id);
        }
        void PlaceFromBag(string id)
        {
            if (World == null) { Toast($"{Josa.EulReul(Catalog.Item(id).ko)} 가방에 넣었어요"); return; }
            World.BeginPlace(id, ok => { Refresh(); if (ok) Toast("좋은 자리예요"); });
        }
        string BuildOdds(RectTransform body)
        {
            Kit.VList(body, 8);
            Kit.Size(Kit.Label(body, Str.OddsHonest, Theme.Body), -1, 40);
            foreach (var (item, p) in Catalog.FreeBoxOdds) { var r = Row(body, 48); RowText(r, Catalog.Item(item).ko); RowText(r, $"{p * 100:0.#}%", Theme.Body, 0); }
            return "선물 상자 확률";
        }

        // ---------------------------------------------------------------- 젤리 상점 (젤리 묶음 · 계절 세트 · 별나라 꾸미기뿐)
        string BuildJellyShop(RectTransform body)
        {
            Kit.VList(body, 10); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 460).flexibleHeight = 1;
            foreach (var p in Catalog.Products.Where(p => p.kind != "star"))
            {
                var r = Row(list, 66); Kit.Size(Kit.IconImage(r, p.kind == "jelly" ? UIIcon.Jelly : UIIcon.Gift, 44), 44, 44); RowText(r, p.ko);
                bool owned = p.kind != "jelly" && G.S.purchases.Contains(p.id);
                var prod = p;
                Kit.Size(Kit.Btn(r, owned ? "가졌어요" : Price(p.id, p.priceKrw), () => { if (!owned) G.Purchase(prod.id, res => { Toast(res == PurchaseResult.Success ? "고마워요! 받았어요" : res == PurchaseResult.Cancelled ? "괜찮아요, 다음에 봐요" : Str.Oops); Open(BuildJellyShop); }); }, owned ? Kit.Style.Secondary : Kit.Style.Primary), 120, 46);
            }
            var rr = Row(list, 56); RowText(rr, "이전에 산 것 다시 받기");
            Kit.Size(Kit.Btn(rr, Str.Restore, () => { G.RestorePurchases(); Toast("다시 확인했어요"); }, Kit.Style.Secondary), 110, 44);
            return Str.JellyShop;
        }

        // ---------------------------------------------------------------- 꾸미기 (가방)
        string BuildBag(RectTransform body)
        {
            Kit.VList(body, 10); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 460).flexibleHeight = 1;
            var items = G.S.inventory.Where(c => !Catalog.Item(c.id).consumable).ToList();
            if (items.Count == 0) Kit.Size(Kit.Label(list, "가방이 비었어요. 상점에서 용품을 골라 봐요.", Theme.Body, Theme.Latte), -1, 60);
            foreach (var c in items)
            {
                var d = Catalog.Item(c.id); var r = Row(list, 64); Kit.Size(ItemIcon(r, d, 42), 42, 42); RowText(r, $"{d.ko}  ×{c.n}");
                var id = c.id; Kit.Size(Kit.Btn(r, Str.Place, () => { CloseAll(); PlaceFromBag(id); }), 90, 46);
            }
            // 데크 타일: 가진 무늬마다 '깔기' (바닥 깔기 모드)
            var tiles = Catalog.Tiles.Where(t => G.Tiles(t.id) > 0 || G.S.floor.Any(f => f.id == t.id)).ToList();
            if (tiles.Count > 0)
            {
                Kit.Size(Kit.Label(list, "데크 타일", Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24);
                foreach (var t in tiles)
                {
                    var r = Row(list, 60); Kit.Size(TileIcon(r, t.id, 40), 40, 40);
                    RowText(r, $"{t.ko}  ×{G.Tiles(t.id)}" + (G.S.floor.Any(f => f.id == t.id) ? $" · 깐 {G.S.floor.Count(f => f.id == t.id)}" : ""));
                    var tid = t.id; Kit.Size(Kit.Btn(r, "깔기", () => { CloseAll(); if (GameBootstrap.Instance) TileMode.Begin(GameBootstrap.Instance, tid, Refresh); }), 90, 46);
                }
            }
            if (G.S.placed.Count > 0) Kit.Size(Kit.Label(list, "섬에 놓인 것", Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24);
            for (int i = 0; i < G.S.placed.Count; i++)
            {
                var p = G.S.placed[i]; var d = Catalog.Item(p.item); var r = Row(list, 60); int idx = i;
                Kit.Size(ItemIcon(r, d, 38), 38, 38); RowText(r, d.ko + (p.zone == Zone.Yard ? " · 마당" : ""));
                if (World is WorldSync ws) Kit.Size(Kit.Btn(r, "옮기기", () => { CloseAll(); ws.BeginMove(idx, ok => Refresh()); }, Kit.Style.Secondary), 84, 46);
                bool essential = (p.item == "food_bowl" || p.item == "water_bowl") && G.S.placed.Count(x => x.item == p.item) == 1;   // (하나뿐인 그릇·물그릇은 넣지 않는다)
                if (!essential) Kit.Size(Kit.Btn(r, Str.PutAway, () => { G.PutAway(idx); G.Save(); World?.Refresh(); Open(BuildBag); }, Kit.Style.Secondary), 64, 46);
            }
            var food = G.S.inventory.Where(c => Catalog.Item(c.id).consumable).ToList();
            if (food.Count > 0) { Kit.Size(Kit.Label(list, "먹을 것", Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24); foreach (var c in food) { var r = Row(list, 52); RowText(r, $"{Catalog.Item(c.id).ko}  ×{c.n}"); } }
            if (!G.S.zonesUnlocked.Contains(1))
            {
                var r = Row(list, 80); Kit.Size(Kit.IconImage(r, UIIcon.Fence, 42), 42, 42);
                RowText(r, Str.YardLocked(Catalog.YardExpansionCoins) + "\n" + string.Join(" · ", Catalog.YardExpansionMaterials.Select(m => $"{Catalog.MaterialKo[m.id]} {G.Material(m.id)}/{m.n}")), Theme.Caption);
                Kit.Size(Kit.Btn(r, "열기", () => { if (G.UnlockYard()) { CloseAll(); Toast("마당이 열렸어요!"); World?.ShowZone(Zone.Yard); } else Toast("재료를 조금 더 모아 봐요"); }), 70, 46);
            }
            return Str.Decorate;
        }

        // ---------------------------------------------------------------- 할 일 · 출석 · 주간 · 계절
        string BuildTasks(RectTransform body)
        {
            Kit.VList(body, 10); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 470).flexibleHeight = 1;
            // 출석
            var ar = Row(list, 70); Kit.Size(Kit.IconImage(ar, UIIcon.Calendar, 44), 44, 44);
            RowText(ar, $"{Str.Attendance}  {Enumerable.Range(0, 7).Select(i => i < G.S.attendIndex ? "●" : "○").Aggregate((a, b) => a + b)}");
            if (G.CanAttend) Kit.Size(Kit.Btn(ar, Str.AttendStamp, () => { var r = G.Attend(); Toast(r != null && r.StartsWith("box:") ? $"7일째 상자: {Catalog.Item(r.Substring(4)).ko}" : "도장을 찍었어요"); Open(BuildTasks); }), 104, 46);
            // 오늘 할 일
            Kit.Size(Kit.Label(list, Str.TasksToday, Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 24);
            for (int i = 0; i < G.S.tasks.Count; i++)
            {
                var t = G.S.tasks[i]; var d = Catalog.DailyTasks.First(x => x.id == t.id);
                var r = Row(list, 64); Kit.Size(Kit.IconImage(r, t.claimed ? UIIcon.Check : UIIcon.Paw, 36), 36, 36);
                RowText(r, $"{d.ko}  ({t.progress}/{d.goal})");
                int idx = i;
                if (!t.claimed && t.progress >= d.goal) Kit.Size(Kit.Btn(r, Str.Claim, () => { G.ClaimTask(idx); Toast($"젤리 {d.jelly}개를 받았어요"); Open(BuildTasks); }, Kit.Style.Primary, UIIcon.Jelly), 92, 46);
            }
            // 주간
            var wr = Row(list, 60); RowText(wr, $"{Str.Weekly}  {Mathf.Min(G.S.weeklyDone, Catalog.WeeklyGoal)}/{Catalog.WeeklyGoal}");
            if (!G.S.weeklyClaimed && G.S.weeklyDone >= Catalog.WeeklyGoal) Kit.Size(Kit.Btn(wr, Str.Claim, () => { G.ClaimWeekly(); Open(BuildTasks); }), 80, 46);
            // 계절
            if (!string.IsNullOrEmpty(G.S.seasonId))
            {
                var s = Catalog.Seasons.First(x => x.id == G.S.seasonId); var sr = Row(list, 64); Kit.Size(Kit.IconImage(sr, UIIcon.Star, 40), 40, 40);
                RowText(sr, $"{s.ko} {Str.Season}  {G.S.seasonStamps}/{Catalog.SeasonStampGoal}");
                if (!G.S.seasonClaimed && G.S.seasonStamps >= Catalog.SeasonStampGoal) Kit.Size(Kit.Btn(sr, Str.Claim, () => { G.ClaimSeason(); Toast($"{Josa.EulReul(Catalog.Item(s.rewards[0]).ko)} 받았어요"); Open(BuildTasks); }), 80, 46);
            }
            // 광고 선물
            if (G.AdAvailable(Catalog.AdSpot.Gift))
            {
                var gr = Row(list, 60); Kit.Size(Kit.IconImage(gr, UIIcon.Gift, 40), 40, 40); RowText(gr, "고양이가 물어 온 선물 상자");
                Kit.Size(Kit.Btn(gr, "열기", () => G.GiftAd((c, j) => { Toast(c > 0 ? $"코인 {c}개{(j > 0 ? $", 젤리 {j}개" : "")}를 받았어요" : Str.AdLater); Open(BuildTasks); }), Kit.Style.Primary, UIIcon.AdPlay), 86, 46);
            }
            return Str.Tasks;
        }

        // ---------------------------------------------------------------- 고양이 · 산책 · 도감 · 텃밭 · 만들기
        string catsTab = "cats";
        string BuildCats(RectTransform body)
        {
            Kit.VList(body, 10);
            var tabs = Kit.Rect(body, "Tabs"); Kit.HList(tabs, 6); Kit.Size(tabs, -1, 50);
            foreach (var (id, ko) in new[] { ("cats", Str.Cats), ("dex", Str.Dex), ("garden", Str.Garden), ("craft", Str.Craft) })
                Kit.Size(Kit.Btn(tabs, ko, () => { catsTab = id; Open(BuildCats); }, catsTab == id ? Kit.Style.Primary : Kit.Style.Secondary), 76, 46);
            Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 420).flexibleHeight = 1;
            if (catsTab == "cats")
            {
                foreach (var c in G.S.cats.Where(c => c.status != "star"))
                {
                    var r = Row(list, 112); Kit.Size(CatIcon(r, c.breed, 64), 64, 64);
                    var info = $"{c.name} · {Catalog.BreedKo(c.breed)}\n{Catalog.Ko(c.personality)} · 호감 {c.Level}단계\n" +
                               (c.status == "walk" ? $"산책 중 · {Math.Max(1, (c.walkEndsAt - G.Now) / 60)}분 뒤 와요" : $"밥 {Pct(c.hunger)} · 물 {Pct(c.thirst)} · 놀이 {Pct(c.play)}");
                    Kit.Size(RowText(r, info, Theme.Caption), -1, 96, 1);
                    var uid = c.uid;
                    if (c.status == "home" && GameBootstrap.Instance)   // (줄을 누르면 그 고양이를 본다: 카메라가 따라가고, 바닥을 누르면 이 고양이가 온다)
                    { var rowBtn = r.parent.gameObject.AddComponent<Button>(); rowBtn.transition = Selectable.Transition.None; rowBtn.onClick.AddListener(() => { CloseAll(); GameBootstrap.Instance.SelectCat(uid); }); }
                    if (c.status == "home")
                    {
                        Kit.Size(Kit.Btn(r, "밥", () => { if (G.Feed(uid)) Toast("냠냠"); else if (G.AdAvailable(Catalog.AdSpot.FreeFood)) G.FreeFoodAd(ok => Toast(ok ? "사료 한 봉지를 받았어요" : Str.AdLater)); else Toast("상점에서 사료를 사 와요"); Open(BuildCats); }, Kit.Style.Secondary), 52, 44);
                        if (Bag.Get(G.S.inventory, "churu") > 0 && GameBootstrap.Instance) Kit.Size(Kit.Btn(r, "츄르", () => { CloseAll(); if (!GameBootstrap.Instance.GiveChuru(uid)) Toast(Str.Oops); }, Kit.Style.Secondary), 60, 44);
                        Kit.Size(Kit.Btn(r, Str.Walk, () => Open(b => BuildWalk(b, uid)), Kit.Style.Primary), 64, 44);
                    }
                    else if (c.status == "walk" && G.AdAvailable(Catalog.AdSpot.WalkSkip))
                        Kit.Size(Kit.Btn(r, "부르기", () => G.CallWalkHome(uid, ok => { Toast(ok ? Str.WalkBack(c.name) : Str.AdLater); Open(BuildCats); }), Kit.Style.Primary, UIIcon.AdPlay), 96, 44);
                }
                if (G.CanAddCat) Kit.Size(Kit.Btn(list, Str.MakeCat, () => CatMaker.Open(this), Kit.Style.Primary, UIIcon.Plus), -1, 54);
                else if (G.S.catSlots < Catalog.MaxCatSlots) Kit.Size(Kit.Btn(list, Catalog.CatSlotJelly[G.S.catSlots] == 0 ? "고양이 칸 늘리기 (무료)" : $"고양이 칸 늘리기 (젤리 {Catalog.CatSlotJelly[G.S.catSlots]})", () => { if (G.OpenCatSlot()) Open(BuildCats); else Toast(Str.NotEnoughJelly); }, Kit.Style.Secondary, UIIcon.Plus), -1, 50);
                if (G.S.stars.Count > 0 || G.S.cats.Count > 0) Kit.Size(Kit.Btn(list, Str.StarLand, () => StarLandUI.Open(this), Kit.Style.Secondary, UIIcon.Moon), -1, 50);
            }
            else if (catsTab == "dex")
            {
                Kit.Size(Kit.Label(list, $"만난 품종 {G.S.dex.Count} / {Catalog.Breeds.Length}", Theme.Body, null, TextAnchor.MiddleLeft), -1, 32);
                foreach (var b in Catalog.Breeds) { bool met = G.S.dex.Contains(b.id); var r = Row(list, 60); Kit.Size(CatIcon(r, b.id, 52, !met), 52, 52); RowText(r, met ? $"{b.ko}  (만남 {G.Meetings(b.id)}번)" : "아직 못 만났어요", Theme.Body, 1, met ? (Color?)null : Theme.Latte); }
            }
            else if (catsTab == "garden")
            {
                for (int i = 0; i < G.S.plots.Count; i++)
                {
                    var p = G.S.plots[i]; var r = Row(list, 64); Kit.Size(Kit.IconImage(r, UIIcon.Sprout, 40), 40, 40); int idx = i;
                    if (string.IsNullOrEmpty(p.seed))
                    {
                        RowText(r, $"빈 밭 {i + 1}");
                        foreach (var sd in Catalog.Seeds) { var s = sd; Kit.Size(Kit.Btn(r, s.ko.Replace(" 씨앗", "").Replace("씨", ""), () => { if (G.Plant(idx, s.id)) Toast($"{Josa.EulReul(s.ko)} 심었어요 ({s.hours}시간)"); else Toast(Str.NotEnoughCoins); Open(BuildCats); }, Kit.Style.Secondary), 64, 44); }
                    }
                    else
                    {
                        var d = Catalog.Seeds.First(x => x.id == p.seed); long left = p.plantedAt + d.hours * 3600L - G.Now;
                        RowText(r, G.Ripe(i) ? $"{d.ko} 다 자랐어요" : $"{d.ko} · {Math.Max(1, left / 60)}분 남았어요");
                        if (G.Ripe(i)) Kit.Size(Kit.Btn(r, "거두기", () => { G.Harvest(idx); Toast($"{Catalog.MaterialKo[d.yields]} {d.amount}개"); Open(BuildCats); }), 84, 46);
                    }
                }
                Kit.Size(Kit.Label(list, "재료: " + (G.S.materials.Count == 0 ? "아직 없어요" : string.Join(" · ", G.S.materials.Select(m => $"{Catalog.MaterialKo[m.id]} {m.n}"))), Theme.Caption, Theme.Latte, TextAnchor.MiddleLeft), -1, 44);
            }
            else
            {
                foreach (var r in Catalog.Recipes.Where(x => G.S.recipes.Contains(x.id)))
                {
                    var row = Row(list, 70); var d = Catalog.Item(r.item); Kit.Size(ItemIcon(row, d, 40), 40, 40);
                    RowText(row, d.ko + "\n" + string.Join(" · ", r.needs.Select((m, k) => $"{Catalog.MaterialKo[m]} {G.Material(m)}/{r.counts[k]}")), Theme.Caption);
                    var rid = r.id; Kit.Size(Kit.Btn(row, Str.Craft, () => { if (G.Craft(rid)) { Toast($"{Josa.EulReul(d.ko)} 만들었어요"); } else Toast("재료가 조금 모자라요"); Open(BuildCats); }, Kit.Style.Primary, UIIcon.Hammer), 104, 46);
                }
                int unknown = Catalog.Recipes.Count(x => !G.S.recipes.Contains(x.id));
                if (unknown > 0) Kit.Size(Kit.Label(list, $"아직 모르는 만들기 {unknown}개 · 손님 고양이가 알려 줘요", Theme.Caption, Theme.Latte), -1, 40);
            }
            return Str.Cats;
        }
        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";
        string BuildWalk(RectTransform body, string uid)
        {
            Kit.VList(body, 12); var c = G.Cat(uid);
            Kit.Size(Kit.Label(body, $"{Josa.EulReul(c.name)} 산책 보낼까요? 오래 걸을수록 선물이 많아요.", Theme.Body), -1, 56);
            foreach (var h in Catalog.WalkHours) { int hh = h; Kit.Size(Kit.Btn(body, Str.WalkHours(h), () => { if (G.SendWalk(uid, hh)) { CloseAll(); Toast($"{Josa.IGa(c.name)} 산책을 나갔어요"); World?.Refresh(); } }, Kit.Style.Secondary, UIIcon.Walk), -1, 50); }
            return Str.Walk;
        }

        public void OpenGuest() => Open(BuildGuest);
        // ---------------------------------------------------------------- 손님 고양이
        string BuildGuest(RectTransform body)
        {
            Kit.VList(body, 12); var ko = Catalog.BreedKo(G.S.guestBreed);
            Kit.Size(Kit.Label(body, Str.GuestBody(ko, G.Meetings(G.S.guestBreed)), Theme.Body), -1, 60);
            if (!G.S.guestTreated) Kit.Size(Kit.Btn(body, Str.GuestTreat, () => { G.TreatGuest(); CloseAll(); Toast($"{ko} 손님이 기뻐해요"); }, Kit.Style.Primary, UIIcon.Fish), -1, 54);
            if (G.CanAdoptGuest) Kit.Size(Kit.Btn(body, Str.GuestAdopt, () => { var c = G.AdoptGuest(null); CloseAll(); if (c != null) { Toast($"{Josa.WaGwa(c.name)} 함께 살게 됐어요"); World?.Refresh(); } }, Kit.Style.Primary, UIIcon.Heart), -1, 54);
            else if (G.GuestHere && G.Meetings(G.S.guestBreed) >= Catalog.AdoptAfterMeetings && !G.CanAddCat) Kit.Size(Kit.Label(body, "같이 살려면 고양이 칸이 하나 더 있어야 해요", Theme.Caption, Theme.Latte), -1, 40);
            return Str.GuestTitle;
        }

        // ---------------------------------------------------------------- 설정
        string BuildSettings(RectTransform body)
        {
            Kit.VList(body, 8); Kit.Scroll(body, out var list); Kit.Size(list.parent.GetComponent<RectTransform>(), -1, 480).flexibleHeight = 1;
            Toggle(list, Str.Sound, () => G.S.soundOn, v => G.S.soundOn = v);
            Toggle(list, Str.Music, () => G.S.musicOn, v => G.S.musicOn = v);
            Toggle(list, Str.Haptics, () => G.S.hapticsOn, v => G.S.hapticsOn = v);
            Toggle(list, "알림: 선물이 가득 찼을 때", () => G.S.notifyIdleFull, v => { G.S.notifyIdleFull = v; G.ScheduleNotifications(); });
            Toggle(list, "알림: 산책에서 돌아올 때", () => G.S.notifyWalkHome, v => { G.S.notifyWalkHome = v; G.ScheduleNotifications(); });
            var r = Row(list, 56); RowText(r, Str.Restore); Kit.Size(Kit.Btn(r, "확인", () => { G.RestorePurchases(); Toast("다시 확인했어요"); }, Kit.Style.Secondary), 80, 44);
            Kit.Size(Kit.Label(list, Str.PhotoPrivacy, Theme.Caption, Theme.Latte), -1, 50);
            Kit.Size(Kit.Label(list, "광고는 집사님이 고를 때만 봐요. 배너 광고는 없어요.", Theme.Caption, Theme.Latte), -1, 40);
            Kit.Size(Kit.Label(list, "글꼴: 주아 (SIL Open Font License)", Theme.Tiny, Theme.Latte), -1, 24);
            Kit.Size(Kit.Label(list, Str.SoundCredits, Theme.Tiny, Theme.Latte), -1, 48);
            return Str.Settings;
        }
        void Toggle(RectTransform list, string label, Func<bool> get, Action<bool> set)
        {
            var r = Row(list, 56); RowText(r, label);
            Kit.Size(Kit.Btn(r, get() ? "켜짐" : "꺼짐", () => { set(!get()); G.Save(); Open(BuildSettings); }, get() ? Kit.Style.Primary : Kit.Style.Secondary), 84, 44);
        }

        // ---------------------------------------------------------------- 사진 · 구역
        void OpenPhoto()
        {
            if (GameBootstrap.Instance && GameBootstrap.Instance.UI == this) { PhotoModeUI.Open(this, GameBootstrap.Instance); return; }
            if (World == null) { Toast(Str.PhotoPrivacy); return; }
            World.Capture(file => { if (file != null) { G.TakePhoto(file, G.S.cats.FirstOrDefault()?.uid ?? ""); Toast("사진을 남겼어요 (휴대폰 안에만 저장)"); } });
        }
        Zone zone = Zone.Indoor;
        void ToggleZone()
        {
            if (!G.S.zonesUnlocked.Contains(1)) { Open(BuildBag); return; }
            zone = zone == Zone.Indoor ? Zone.Yard : Zone.Indoor; World?.ShowZone(zone);
            zoneBtn.GetComponentInChildren<Text>().text = zone == Zone.Indoor ? Str.Yard : Str.Indoor;
        }

        /// <summary>용품 그림: 실제 모델을 찍은 작은 그림(Resources/ItemIcons, ItemIconBake)이 있으면 그것, 없으면(먹이 등) 아이콘.</summary>
        public static Graphic ItemIcon(Transform parent, ItemDef d, float size)
        {
            var tex = d != null ? Resources.Load<Texture2D>("ItemIcons/" + d.model) : null;
            if (!tex) return Kit.IconImage(parent, IconOf(d), size);
            var rt = Kit.Rect(parent, "ItemIcon"); var raw = rt.gameObject.AddComponent<RawImage>(); raw.texture = tex; raw.raycastTarget = false;
            rt.sizeDelta = new Vector2(size, size); return raw;
        }

        /// <summary>고양이 얼굴 그림 (Resources/CatIcons, CatIconBake). 아직 못 만난 품종은 어두운 실루엣.</summary>
        /// <summary>데크 타일 그림 (무늬 한 장을 둥근 네모로).</summary>
        public static Graphic TileIcon(Transform parent, string id, float size)
        {
            // (Mask 없이: 무늬 한 장 + 얇은 코코아 테두리. 스텐실 마스크는 창을 닫은 뒤 다른 글자를 가리는 일이 있었다)
            var frame = Kit.Box(parent, "TileIcon_" + id, Theme.Cocoa, Mathf.Max(2, (int)(size * .12f))); frame.raycastTarget = false;
            var ri = new GameObject("Tex", typeof(RectTransform)); ri.transform.SetParent(frame.transform, false); var raw = ri.AddComponent<RawImage>(); raw.texture = PaintedTextures.Tile(id); raw.raycastTarget = false;
            Kit.Fill(raw.rectTransform, 2, 2, 2, 2);
            return frame;
        }
        public static Graphic CatIcon(Transform parent, string breed, float size, bool silhouette = false)
        {
            var tex = Resources.Load<Texture2D>("CatIcons/" + breed);
            if (!tex) return Kit.IconImage(parent, silhouette ? UIIcon.Lock : UIIcon.Cat, size);
            var rt = Kit.Rect(parent, "CatIcon"); var raw = rt.gameObject.AddComponent<RawImage>(); raw.texture = tex; raw.raycastTarget = false;
            if (silhouette) raw.color = new Color(.22f, .17f, .14f, .4f);   // (누군지 모르게, 모양만)
            rt.sizeDelta = new Vector2(size, size); return raw;
        }

        public static UIIcon IconOf(ItemDef d) => d.category switch
        {
            ItemCategory.Food => d.id.Contains("water") || d.id.Contains("fountain") || d.id.Contains("milk") ? UIIcon.Cloud : UIIcon.Fish,
            ItemCategory.Toy => d.id.Contains("ball") || d.id.Contains("yarn") ? UIIcon.Yarn : UIIcon.Paw,
            ItemCategory.Tower => UIIcon.Star, ItemCategory.Outdoor => UIIcon.Fence, ItemCategory.Deco => UIIcon.Sprout, _ => UIIcon.Home,
        };
    }
}
