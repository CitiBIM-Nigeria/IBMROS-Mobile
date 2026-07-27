# QR / Deep-Link Feature — Implementation Notes & Go-Live Guide

> Companion to `docs/IBMROS_QRCode_Feature_Implementation_Roadmap.md`.
> Status as of this autonomous session: **code complete across both repos, tested
> where testable. Only production config + credentials + in-Editor wiring remain.**
> Nothing here is blocked on those — every unknown is a clearly-marked placeholder.

Spans two repos:
- **`ros-pipeline`** (Python) — QR generation, storage, backfill, link hosting.
- **`IBMROS Mobile`** (Unity, this repo) — scanning, deep-link handling, product
  detail, Save for Later, Add to Current Room, build hooks.

---

## 1. What "done" means right now

| Area | State | Verified how |
|---|---|---|
| Pipeline: `qr_url` written inline per product | ✅ done | module imports; offline test |
| Pipeline: QR SVG render + S3 upload + row patch (enrichment pass) | ✅ done | real encode→decode round-trip test |
| Pipeline: standalone backfill `tools/backfill_qr.py` | ✅ done | **live read-only dry-run vs the real 700-product table** |
| Pipeline: printable labels `tools/export_qr_labels.py` | ✅ done | offline self-test |
| Hosting file generator `infra/setup_link_hosting.py` | ✅ done | generated + JSON validated |
| Unity: resolve-by-canonical, deep-link manager, router, Save for Later, scanner, build hooks | ✅ code complete | static symbol verification (no Unity compiler here) |
| Production domain / Apple Team ID / Android SHA-256 / store URLs | ⛔ placeholders | intentionally deferred |
| Unity in-Editor wiring (scene managers, UXML Scan button, ZXing import) | ⛔ TODO | needs the Unity Editor |
| Live QR backfill WRITE (mutates prod DynamoDB/S3) | ⛔ deferred | not run autonomously — go-live step |

---

## 2. Every placeholder, and the ONE place each lives

All production-unknowns are centralized. Search the repos for `PLACEHOLDER` or
`ibmros.local` to find them all. There are exactly three config surfaces (they
can't share one file because Python, C#, Xcode, and Gradle all read at different
times):

| Value | `ros-pipeline/link_config.json` | `IBMROS Mobile` |
|---|---|---|
| Link domain | `link_domain` | `LinkConfig.LinkDomain` **+** `Assets/Plugins/Android/AndroidManifest.xml` (host) **+** iOS entitlement is derived from `LinkConfig` automatically |
| Apple Team ID | `ios.team_id` | Player Settings (bundle id) |
| iOS bundle id | `ios.bundle_id` | Player Settings |
| Android package | `android.package_name` | Player Settings (`com.CitiBIM.IBMROS`) |
| Android signing SHA-256 | `android.sha256_cert_fingerprints` | (used only in hosted `assetlinks.json`) |
| App Store URL | `ios.app_store_url` | (used only in the hosted redirect page) |
| Play Store URL | `android.play_store_url` | (used only in the hosted redirect page) |

> **Current placeholder domain:** `placeholder.ibmros.local`. Deep links, the QR
> images, the AASA file, the Android manifest, and the Unity parser all use it, so
> the whole chain is internally consistent and testable today — it just doesn't
> point at a real server yet.

---

## 3. Go-live checklist (config-only; no code changes)

When the real values arrive:

1. **`ros-pipeline/link_config.json`** — set `link_domain`, `ios.team_id`,
   `ios.bundle_id`, `android.package_name`, `android.sha256_cert_fingerprints`,
   store URLs.
2. **`IBMROS Mobile` → `LinkConfig.cs`** — set `LinkDomain` to the same domain.
3. **`AndroidManifest.xml`** — set the `android:host` to the same domain (and
   confirm the activity name — see §5).
4. **Player Settings** — set iOS bundle id + Team ID; confirm Android package.
5. Re-run **`infra/setup_link_hosting.py --upload`** → stages AASA/assetlinks/
   redirect to S3; then wire the CloudFront distribution + DNS + ACM cert per the
   printed TODO.
6. Re-run **`tools/backfill_qr.py`** → regenerates every QR image against the real
   domain, and writes `qr_url`/`qr_image_url` to all rows. (Safe to run anytime;
   idempotent. Running it now with the placeholder domain is also fine — step 6
   just overwrites with the real link.)
7. Build the app; verify with the steps in §7.

That's it. No source edits.

---

## 4. Documented engineering assumptions (the "reasonable assumption + TODO" calls)

1. **Deep link carries the canonical id** (worldwide IKEA article), not the slug —
   region-independent, matches the shelf tag. Resolved via the existing
   `canonical-index` GSI. *(Verified working against the live table.)*
2. **Two distinct scan flows (by design):**
   - **In-app scanner** (`QrScannerController`, used while in a room): decode →
     extract canonical id (pure local parse, no domain dependency) → resolve via
     the canonical-index GSI → **add the model DIRECTLY into the current room**
     (`RoomUIManager.AddProductToRoom`). This is the production behaviour and works
     today with the placeholder domain (it never contacts the domain).
   - **External universal/app link** (`DeepLinkManager` from the OS): opens the
     **Product Detail** sheet (Preview / Save / Add). In the Room scene the sheet
     opens immediately; from home the product is auto-saved and the app enters the
     Room designer, which opens it on load (`ScannedProductRouter`). This flow goes
     fully live once we own the domain + configure Universal/App Links.
   **TODO (future):** a dedicated standalone home detail screen would avoid
   entering Room for the external flow; deferred (new UI Toolkit screen couldn't be
   verified without the Editor).
3. **Save for Later is local-first** (device JSON, `SavedItemsService`). Works for
   guests, offline, no backend. **TODO (future):** backend sync table keyed on the
   Cognito user; the stored fields migrate cleanly.
3b. **Scan History is a separate, AUTOMATIC feature** (`ScanHistoryService`):
   every successful scan (in-app or external link) records canonical id, product
   id, name, thumbnail, timestamp, and whether it was added to a room — local
   JSON, newest-first, capped at 200 entries. History = what the user DID;
   Saved Items = what the user CHOSE. Both flows record it: the scanner after the
   add-to-room attempt (real outcome flag), the deep link with
   `addedToRoom=false` (it opens detail). Sync-ready fields for future accounts.
4. **The existing fav (☆) button IS "Save for Later"** and the existing Add button
   IS "Add to Current Room" — no UXML surgery needed. The fav button now persists
   via `SavedItemsService` instead of just toggling a star.
5. **Android activity = `UnityPlayerGameActivity`** because the project builds with
   GameActivity (`androidApplicationEntry: 2`). If you switch to classic Activity,
   change the manifest to `UnityPlayerActivity`.
6. **No signed/tokenized links.** The link carries a public catalog id to
   read-only, guest-accessible data — signing adds key management for zero security
   gain. Parser validates host + path + id charset and rejects anything else.

---

## 5. Remaining IN-EDITOR wiring (can only be done in the Unity Editor)

These are the only Unity steps left; each is quick:

1. ~~Add manager components to Main.unity~~ ✅ **no longer needed** —
   `ServicesBootstrapper` (RuntimeInitializeOnLoadMethod) auto-creates
   `DeepLinkManager`, `SavedItemsService`, `ScanHistoryService`, and
   `QrScannerController` on a persistent GameObject before the first scene loads.
   Press Play and they exist.
2. **In-app scanner:** ✅ **fully wired, zero Editor steps.**
   - ZXing.Net installed (`Assets/Plugins/ZXing/zxing.unity.dll`, Unity flavor
     verified, proper importer meta) + `IBMROS_ZXING` define set (Android/
     iPhone/Standalone).
   - `ScannerOverlayController` builds the full-screen viewfinder UI
     programmatically (live WebCamTexture feed, scan frame, status line, close);
     `RoomUIManager.InitializeQrUI` injects a **Scan** button and a **My Items**
     button into the existing bottom bar (same USS classes as Store/Add).
   - **Editor webcam testing**: enter the Room scene, tap Scan — macOS prompts
     for camera permission once. Point the webcam at one of the pre-generated
     test QRs in `ros-pipeline/docs/qr_test_*.png` (real live products WITH 3D
     models; open the PNG on your phone or a second window). The product
     resolves from DynamoDB and spawns into the room.
   - No-camera fallback:
     `QrScannerController.Instance.HandleDecodedText("https://placeholder.ibmros.local/p/40466704")`.
3. **My Items screen:** ✅ built (`MyItemsPanelController`) — Saved / History
   tabs over the two local stores; rows show thumbnail, name, price/relative
   time, "✓ Added" badge; tap re-resolves (canonical-first) and opens the detail
   sheet; Saved rows removable; History has Clear. All programmatic UI Toolkit —
   no UXML/USS assets.
3. **iOS**: set bundle id + Team ID (Player Settings). The associated-domains
   entitlement is added automatically at build by
   `Assets/Editor/DeepLinkBuildPostProcessor.cs`.
4. **Android**: confirm the activity name in `AndroidManifest.xml` (§4.5) and that
   the release keystore's SHA-256 matches the hosted `assetlinks.json`.
5. **A "Saved" list screen** (optional): `SavedItemsService.All()` returns the
   items; wire a simple UI Toolkit list; each row → `FurnitureDataService`.

> These are listed in priority order. #1 alone makes external-camera scanning +
> deep-link → product detail work end-to-end.

---

## 6. New / changed files

**`ros-pipeline`:**
- `link_config.json` *(new — central placeholder config)*
- `pipeline/ikea_pipeline.py` — `_load_link_config`, `qr_deep_link`,
  `render_qr_svg`, `qr_url` in record, QR fields persisted in `DynamoWriter.write`,
  `DynamoWriter.patch_qr`, `_scan_qr_jobs`/`_qr_process_one`/`run_qr_backfill`,
  `--no-qr` flag, `main()` wiring
- `tools/backfill_qr.py`, `tools/export_qr_labels.py` *(new)*
- `tools/debug/test_qr.py` *(new — offline round-trip test)*
- `infra/setup_link_hosting.py` + `infra/link_hosting/generated/*` *(new)*
- `requirements.txt` — `segno`

**`IBMROS Mobile`:**
- `Assets/MyStuffs/Scripts/Managers/LinkConfig.cs` *(new)*
- `Assets/MyStuffs/Scripts/Managers/DeepLinkManager.cs` *(new)*
- `Assets/MyStuffs/Scripts/Managers/ScannedProductRouter.cs` *(new)*
- `Assets/MyStuffs/Scripts/Managers/SavedItemsService.cs` *(new)*
- `Assets/MyStuffs/Scripts/Managers/ScanHistoryService.cs` *(new — automatic scan log)*
- `Assets/MyStuffs/Scripts/Managers/ServicesBootstrapper.cs` *(new — auto-creates
  all QR services at startup; removes manual scene setup)*
- `Assets/MyStuffs/Scripts/Scanning/QrScannerController.cs` *(new, ZXing-gated;
  camera-permission coroutine, 1280×720 feed)*
- `Assets/MyStuffs/Scripts/UI/ScannerOverlayController.cs` *(new — programmatic
  viewfinder overlay: live feed, scan frame, status, auto-resume on error)*
- `Assets/MyStuffs/Scripts/UI/MyItemsPanelController.cs` *(new — Saved/History
  tabbed panel, programmatic UI Toolkit)*
- `Assets/Plugins/ZXing/zxing.unity.dll` *(+ importer meta; `IBMROS_ZXING` define
  set in ProjectSettings for Android/iPhone/Standalone)*
- `ros-pipeline/docs/qr_test_*.png` *(3 test QRs from live model-backed products
  — for webcam testing in the Editor)*
- `Assets/Editor/DeepLinkBuildPostProcessor.cs` *(new, iOS)*
- `Assets/Plugins/Android/AndroidManifest.xml` *(new, App Links)*
- `Assets/MyStuffs/Scripts/Models/ProductModel.cs` — `CanonicalProductId`, `QrUrl`
- `Assets/MyStuffs/Scripts/Furniture/FurnitureRepository.cs` — projection + attr
  names + `ParseProduct` + `GetProductByCanonical`
- `Assets/MyStuffs/Scripts/Furniture/FurnitureDataService.cs` — `ResolveByCanonical`
- `Assets/MyStuffs/Scripts/UI/ItemDetailSheetController.cs` — fav → Save for Later
- `Assets/MyStuffs/Scripts/UI/RoomUIManager.cs` — `OpenProductDetail` public +
  consume pending scan on load

---

## 7. End-to-end verification (once config + wiring land)

1. `venv/bin/python tools/backfill_qr.py --dry-run` → lists rows needing a QR
   *(already passes read-only against live data)*.
2. Run the real backfill → `qr.svg` per product in S3; decode one to confirm it
   carries the real link.
3. `curl https://<domain>/.well-known/apple-app-site-association` → `application/json`,
   200, no redirect. Validate `assetlinks.json` in Google's Statement List tester.
4. Print a label (`tools/export_qr_labels.py`), scan with the **native camera**,
   app **not** installed → store redirect.
5. Install app; scan again → opens Product Detail (cold start path is handled by
   `DeepLinkManager` via `Application.absoluteURL`).
6. In-app **Scan** (ZXing enabled) → same product opens.
7. Tap ☆ → persists across restart (as guest). Open a room → Add → places in AR.
8. Scan garbage / unknown id / offline → each a defined, non-crashing outcome
   ("Not an IBMROS product code" / "Product not available" / queued until ready).

---

## 8. What was deliberately NOT done autonomously

- **The production QR backfill write** (bulk mutation of live DynamoDB + S3). The
  read-only dry-run was run to prove the path; the write is a one-command go-live
  step (`tools/backfill_qr.py`) left for a human to trigger.
- **Provisioning the CloudFront distribution / DNS / ACM cert** — impossible
  without owning the domain; the hosting files and exact steps are generated and
  printed.
