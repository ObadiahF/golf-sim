# Bug hunt log

QA findings, newest round first. Each entry has a severity (blocker / major / minor / polish), the component, repro steps, expected vs actual, and evidence.
Evidence files live in the tester's tmp folder (round 1: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt/`, round 2: `…/tmp/bughunt2/`, round 3: `…/tmp/bughunt3/`, round 4: `…/tmp/report/e2e/`, round 5: `…/tmp/qa5/`, round 6: `…/tmp/qa6/`) unless noted.

---

## Round 6 (course content + trainer UX, 2026-10-02)

Judged the holes as a golfer, both the current generator's output and the trainer's pool that friends will rate and play today. Then went through the trainer as a first-time friend on a phone. Report only: no code was changed.

- **Generated:** 60 holes with generator v5 (`gen_hole.py generate`, spacing 0.75 as the trainer uses): 6 presets × 10 seeds. Par 3: 24, par 4: 18, par 5: 18. Folder: `…/tmp/qa6/gen/`.
- **Trainer pool:** all 417 packages copied from `e2etrainer` (`…/tmp/qa6/pool/`). The Game API's `/api/game/top-holes?limit=100` returned 20 holes (`top.json`, linked in `top/`).
- **Checker:** `…/tmp/qa6/hole_qa.py` (par vs length, landing-zone distance, fairway width at the landing zone, slope at the landing zone, water carries, bunkers far from play, the pin on the green, tee-box facing, and tree crowns on the shot lines). Crowns are tested in 2D and against a rough ball-flight height (driver apex 32 m, irons 28 m), using a fan of 15 lines across the target.
  - `centre.py` checks the centre line of every planned shot.
  - `notches.py` finds terrain creases (a second difference over 0.15 m between neighbouring heightmap samples within 25 m of the hole line, away from pads, bunkers and water).
  - `dbg.py` lists the trees near a tee line.
  - Contact sheets of the previews are in `sheets/`.
- **Trainer UX:** Playwright as `e2e1`/`pw1`, mobile contexts at 390×844 and 844×390 (touch on, taps and CDP touch drags), plus one desktop pass. Screenshots are in `…/tmp/qa6/ux/`.
  - **Votes cast:** 1 (👎 + "Too long" + comment on `links_732819599_c62e07`), and 1 real Skip. The waiting-state checks used a mocked `/api/next`.
  - **Refill rule:** e2e1 had rated 0 of the 12 holes in the newest batch (34), and a refill needs 6, so no batch was triggered.

### Automated checks

| Check | Generated (v5, 60) | Trainer pool (417, all generator v3) | Top holes (20) |
|---|---|---|---|
| `prep_hole.py validate` | 60/60 ok | 417/417 ok | n/a |
| `scan_playability.py` (generator caps: pin 4 %, surface 6 %) | 0 fail | n/a | **11 fail** (pin 4.0–5.5 %, one tee-shot rise of 0.5 m). All pass the trainer's 6 % cutoff. |
| Par vs length (3: 100–230, 4: 250–430, 5: 430–600 m) | 60/60 in range (par 3 136–174, par 4 350–420, par 5 481–519) | in range | in range |
| A tree crown on every line of the tee shot (3D) | 2 | 11 (+6 with under 40 % of lines clear) | 1 under 40 % clear (#8) |
| Landing zone on a slope over 10 % | 5 | 38 | 0 |
| Grading seams (> 20 crease cells) | **13** | 42 (v3 has no grading pass, so most of these are natural relief) | 1 (#5, natural relief) |
| Fairway width at landing zone 1 | 27–61 m, none under 20 m | none under 20 m | 32–55 m |
| Pin to green edge | 6.3–11.6 m | ok | ok |
| Tee box facing the line | all within 1° | ok | ok |
| Crowns overhanging the green / stray bunkers / water carries over 200 m | none | none | none |

**Overall:**
- **Generated v5:** 42 of 60 (**70 %**) are fine, not counting the par-5 shape (G6-2). Every par 5 has that shape, so only 29 of 60 (**48 %**) are fine with it counted.
- **Pool:** 334 of 417 (80 %) are fine without G6-2. With it, 46 % are fine: 194 of the 417 holes (47 %) are par 5s.
- **Top 20:** 19 of 20 are fine without G6-2 (not #8), and 16 of 20 with it.

### Course content

#### G6-1 Grading cuts stair-step seams across the hole line on hilly v5 holes
- **Severity:** major (visible in the 3D view and the preview, and the ball rolls over it in the game). It hits 6 of the 10 mountain holes; mountain holes are 8 % of the pool.
- **Component:** `Tools/course_gen/grading.py` `grade_corridor`.
- **Repro:** `gen_hole.py generate --preset mountain --par 5 --seed 6419 --spacing 0.75` (`mountain_6419_e7481a`). Look at its preview, or sample the heightmap along the tee line.
- **Expected:** a smooth graded corridor.
- **Actual:**
  - The preview shows parallel stripes across the corridor: horizontal terraces from the tee to the fairway on `mountain_6419`, and vertical seams on `mountain_6426`, `6398`, `6384`, `6370` and `6405`, `links_6265` and `desert_6342`.
  - On `mountain_6419`, 12 m left of the line, the ground goes 0.62 → −0.41 m within 1 m and climbs back over about 4 m, every ~11.5 m. On the line itself it zigzags ±6 cm each meter.
  - The worst crease is 1.49 m (second difference). In all, 13 of 60 holes have more than 20 crease cells.
- **Cause (confirmed):**
  - `grade_corridor` gives each cell the correction (`delta`) of its nearest 1 m path sample, found by `distance_transform_edt` on rounded sample cells. Where `delta` changes fast (a steep slope graded to 4–10 %), neighbouring cells take corrections from different samples, so the correction field is a staircase.
  - With `grade_corridor` stubbed out, the same seeds drop from 2417 to 13 crease cells (`mountain_6398`) and from 1272 to 43 (`links_6265`), in `…/tmp/qa6/nograde/`.
  - One possible fix: project each cell onto the path (`line_locate_point`) and interpolate `delta` linearly, or smooth the field before applying it.
- **Evidence:** `sheets/gen_5.png` and `gen_4.png`, `crop_mountain_6419_e7481a.png`, `crop_6342_tee.png`, `gen_notches.txt`, and `nograde/`.
- **Status:** Fixed (2026-10-02, generator v6): `grade_corridor` projects every cell onto the path (`line_locate_point`), interpolates the correction at that arc length, and blurs the whole correction (5 m) after the fade to the off-corridor ground, so corners and the fade leave no creases. On QA's 60 jobs, 8 holes still have > 20 crease cells, but they are natural relief: the same layouts with grading switched off have the same count (e.g. `links_6237` 224 vs 224, `mountain_6419` 46 vs 64). The old grading gave 9945 crease cells on 24 hilly test holes, the new one 232 (ungraded: 325). Test: `tests/test_course_content.py::test_grading_adds_no_seams` (24 mountain/links holes at 0.75 m: at most 8 more crease cells than ungraded).

#### G6-2 Every par 5 has the same shape: a 275 m corner, a ~100 m second shot, and 120 m into the green
- **Severity:** major (design). 18 of 18 generated par 5s, 191 of 194 pool par 5s and 4 of the top 20.
- **Component:** `layout.py` `LayoutBuilder.route`.
- **Actual:**
  - `d1 = clip(length × 0.56–0.66, 200, 275)` is always 275 m for 480–520 m holes.
  - `d2 = clip(d1 + 190–230, 0, length − 120)` is always `length − 120`.
  - So legs are 275 / 86–124 / 120 m on every par 5 (`…/tmp/qa6` path-segment table).
  - The first corner sits at the bag's longest drive (Driver carries 251 m in `Clubs.cs`). Most drives finish 20–50 m short of the bend. The "second shot" is then a wedge to a second bend, and the third is another 120 m wedge.
  - The holes play as a long par 4 plus a wedge, not as a par 5 with a lay-up decision.
- **Related:** par-4 corners at 256–271 m (4 of 18 v5 par 4s, and 5 of the 7 par 4s in the top 20) are past most drives too. The short-drive check (40 m short of the corner) found no closed corners, so this is a feel issue, not a blocked hole.
- **Suggested:** cap `d1` at about 240 m and put the second landing about 200 m past it, so a par 5 leaves 50–110 m in.
- **Status:** Fixed (2026-10-02, generator v6): `layout.route` puts the drive 215–250 m out (par 5: ~235 ± 10 m; par 4: 56–66 % of the length, capped at 250 m). A par 5 lays up 130–230 m to leave 55–130 m in, and bends at the first corner, the second, both (same way or an S) or neither. Over 204 par 5s: drive 215–250 (median 237), second shot 151–230 (median 199), into the green 58–129 (median 94); 97 bend at the first corner only, 52 at the second only, 17 at both, 38 straight. Length priors and par ranges unchanged. Test: `test_par5_routes_vary` (200 routes).

#### G6-3 Single trees stand right on the tee line, 30–50 m out (no protection outside the fairway)
- **Severity:** major for the affected holes. That is 2 of 60 v5 holes and 11 pool holes where every line hits a crown, plus 6 where under 40 % of lines clear. One is top hole #8.
- **Component:** `layout.py` `_specimen_trees` (only `fairway.buffer(6)`, `green.buffer(18)` and `tees.buffer(12)` are kept clear), the `Rough trees` scatter rule (`vegetation_themes.py`), and `validate.problems` (its "woods block the line of play" check looks at woods polygons only, never at single trees).
- **Actual:** a 20–23 m conifer 2–5 m off the line, 35–50 m from the tee, which no club clears at that range. Par 3s with no fairway (65 % of par 3s) and the rough between the tee and the fairway start (the first 85–150 m) are open to these trees.
  - `forest_6146_fe2134` (par 3): a 20.9 m tree 41.6 m out, 2.7 m off the line.
  - `lakes_6181_a6c3d9` (par 4): a 22.6 m tree 36.6 m out, 2.2 m off the line. A 14.8 m tree 89 m out, 1.7 m off the line.
  - Pool: `forest_813740144_3613bb` (par 3, 21.6 m tree 36.8 m out, 2.0 m off the line), `forest_312110832_352c42` (21.3 m tree at 37.7 m, 0.9 m off the line), `forest_141638515_47e910` and `forest_123372095_7ce23a`, among others. All are marked playable in `hole_checks`.
  - **Top #8** `forest_870462187_c3bfa9`: a 23.9 m tree at 65 m, 6.4 m off the line, and a 12.6 m tree at 76 m, 0.1 m off the line. Only 13 % of the tee-shot fan clears.
- **Suggested:** keep specimen and rough trees out of `path.buffer(~10)` for the first ~120 m (the whole line on a par 3). Add a tree-crown line check to `validate.problems` and `hole_checks`.
- **Evidence:** `gen_qa.json`, `pool_qa.txt`, `dbg.py` output, `sheets/gen_2.png` (`forest_6104`: a tree 0.2 m off the line at 106 m), and `sheets/gen_3.png`.
- **Status:** Fixed (2026-10-02, generator v6): `layout.shot_zone` (a 12° cone either side of the tee shot to the first landing zone, the pin on a par 3, and 8° from each landing zone to the next stop, plus 6 m) is kept free of specimen trees and woods, and `vegetation.plant(tree_clear=...)` keeps every tree rule out of it (rough, native and woods trees). New `validate.tree_line_problems`: of 17 lines 8° either side of the tee shot, 60 % must miss every crown (radius from the kind and height, `vegetation_themes.CROWNS`) under a rough ball flight; from each landing zone, one line to the next stop must be open. The generator drops any tree still in the way. `scan_playability.py` reports it, and the trainer's `hole_checks` (CHECK_VERSION 3) flags pool holes with fewer than `TRAINER_TEE_LINE_MIN_CLEAR` (40 %) clear. On `e2etrainer`, 26 pool holes are now unplayable for trees, including `forest_870462187_c3bfa9` (top #8, 65 % blocked), `forest_813740144_3613bb`, `forest_312110832_352c42`, `forest_141638515_47e910` and `forest_123372095_7ce23a`; none are in `/api/game/top-holes` any more. Test: `test_shot_lines_are_open_and_landing_zones_level`, `test_validators_catch_a_tree_on_the_line_and_a_tilted_landing_zone`, `test_scan_finds_a_tree_on_the_tee_line`.

#### G6-4 Landing zones on 10–23 % side slopes
- **Severity:** minor (v5) / major (pool mountain holes).
- **Component:** `grading.py` limits only the grade along the line, never across it; `validate` has no landing-zone slope check.
- **Actual:** 5 of 60 v5 holes have a landing zone over 10 %: `mountain_6419` LZ1 20 % / LZ2 17 %, `links_6251` 13 %, `links_6237` 12 %, `mountain_6377` 12 % and `parkland_6027` 10 %. In the pool, 38 holes, up to 23 % (`desert_280041325_c2bf24`, `mountain_963745374_eecfb6`). A perfect drive rolls off into the rough or the trees.
- **Status:** Fixed (2026-10-02, generator v6): grading now also holds the line to 6 % within 30 m of a landing zone and tilts the corridor about the hole line where it leans more than 8 % across it (4 % at the landing zones). New `validate.landing_problems` (8 % across, 10 % along, at each landing zone) makes the generator retry, e.g. when a pond bank crosses a landing zone. `scan_playability.py` reports it, and `hole_checks` flags pool holes over `TRAINER_LANDING_MAX_SIDE_SLOPE` (15 % across). On `e2etrainer` that flags 3 holes (`links_122780274_de3a7a` 20 %, `mountain_265573462_71dd54` 17 %, `mountain_293663143_f64263` 16 %). QA's `desert_280041325_c2bf24` and `mountain_963745374_eecfb6` measure 11 % and 14 % across: QA's 23 % included the grade along the line. On QA's 60 jobs, regenerated with v6, no landing zone is over 10 % by QA's measure.

#### G6-5 Top holes include holes with only 👎, so 9+ hole rounds play holes friends disliked
- **Severity:** minor (today: an 18-hole round has only 14 holes with any 👍).
- **Component:** `ranking.py` / `game.py` `top-holes`.
- **Actual:**
  - Ranks 15–20 of `/api/game/top-holes` have 0 👍 and 1–2 👎 (score 0).
  - The Top holes panel lists them too.
  - QA's one 👎 (`links_732819599_c62e07`, now 0/1) put a fresh disliked hole into Top holes at #18.
  - An 18-hole game takes 4+ disliked holes.
- **Suggested:** leave score-0 or net-negative holes out of the game list (or back-fill with unrated playable holes), and show "not enough liked holes" when there are too few.
- **Status:** Fixed (2026-10-02): `ranking.top_holes` (so `/api/top`, `/api/game/top-holes` and `top`) lists only liked holes: more 👍 than 👎 and at least `TRAINER_TOP_MIN_LIKES` (1) 👍. With fewer qualifying holes than asked, the list is just shorter (the game cycles it). The Top holes panel says so and its empty state reads "No liked holes yet". Documented in the README (Leaderboard, Game API, settings). Checked: `test_ranking.py` `test_top_holes_leave_out_disliked_holes` / `test_liked_needs_more_likes_than_dislikes`; on `e2etrainer` the panel lists 10 holes, all net 👍 (it was 20, with 0 👍 rows).

#### G6-6 Today's pool is all generator v3, so the v4/v5 fixes don't reach players
- **Severity:** note.
- **Actual:**
  - All 417 packages on `e2etrainer` have `generatorVersion` 3. The image runs v5, but batches 32–34 were made earlier.
  - Top holes (the game's round) and pool holes still carry v3 traits: greens at 4–5.5 % at the pin (11 of 20 top holes fail the generator's own 4 % cap; rank 3 `lakes_249237828_a380a1` is at 5.5 %, rank 18 `mountain_549136632_222692` at 6.8 % on the surface), landing zones on steep slopes, and the trees on the line (G6-3).
  - Only the trainer's looser 6 % / tee-shot rules are applied.
  - New v5 holes appear only after the next refill.
- **Also noted:**
  - Rank 3 is an island-like par 3 with water left, right and in front (fun, but any miss is wet).
  - Rank 8 is the tight forest par 5 above.
  - Pool par mix: 47 % par 5, 22 % par 4, 32 % par 3. The model is drifting toward par 5s, and every one has the G6-2 shape.

### Trainer UX (phone)

Login → first hole: files in **1.1 s** and canvas in **1.2 s**. After Submit the next hole's files take **0.35 s**, after Skip **0.16 s**, and a Top-holes peek **0.1 s** (≈2.6 s to a settled view on desktop). Touch: the joystick moves (198 → 134 yd to the pin after a 2 s push), a look drag works, and chips, Like/Dislike, the comment box, Submit, Skip, Tee/Green/Overhead, Top holes, a Top-hole tap and Log out all respond to taps in both orientations. Chip exclusivity works (Too long drops Too short from the selection). After Log out, `/api/me` returns 401. Console: only `THREE.Clock … deprecated` and one 401 for `/api/me` before login, on every first visit. No page errors.

#### T6-1 On a phone the HUD hides most of the hole: Overhead and the green are covered
- **Severity:** minor.
- **Component:** `web/src/hud` layout on small screens.
- **Actual:**
  - **Portrait:** the hole card and view buttons cover the top third, and the rate panel the bottom third. In Overhead (`07-view-Overhead.png`) the green and its bunkers are under the hole card and the view buttons, and the tee is under the rate panel.
  - **Landscape (844×390):** the hole card covers the top 35 % and the rate panel the right 35%. On rank 3 `lakes_249237828` the green is under the card (`13-landscape-top-hole-open.png`).
  - The joystick also covers the floating "198 yd" pin label (`02-first-hole-portrait.png`).
  - A friend can't see the whole hole they are rating unless they collapse something, and nothing collapses.
- **Suggested:** frame Overhead inside the free area, and let the rate panel or hole card collapse.
- **Status:** Fixed (2026-10-02): on phones the hole card minimises (▴) to a one-line strip (name, par, yards, Skip; the top bar hides until it opens), and the rating sheet collapses to the thumbs row (handle: tap, or swipe down / up; Submit shows once a thumb is picked). The touch controls and waiting card follow the measured panel edges (`hud/insets.ts`, `--hud-top` / `--hud-bottom`), and the waiting card sits under the view buttons, clear of the stick. Overhead frames the hole inside the area the HUD leaves free (`measureInsets` → `views.ts` `overhead`: fits the hole's extent to the free area and aims off-centre), on desktop too. Checked in Playwright at 390×844 and 844×390, both expanded and collapsed, plus 1440×900: `…/tmp/trainerfix6/05`, `06`, `12`–`15`, `18-*.png`.

#### T6-2 Two different yardages on screen
- **Severity:** polish.
- **Actual:** the card says YARDS 554, and the flag label says "579 yd" from the start view (it is the distance from the camera, which starts behind the tee). Par 3 165 vs "198 yd", rank 3 169 vs "202 yd" (`02-…`, `10-…`, `13-…`).
- **Suggested:** label it "to pin" as the desktop minimap does, or measure from the tee at the start.
- **Status:** Fixed (2026-10-02): the flag label shows the hole's yardage from the tee, the card's number ("⛳ 171 yd"); once you move more than 40 m from the tee it adds the distance from you, labelled ("171 yd · 108 yd to pin"). `…/tmp/trainerfix6/04`, `05-*.png`.

#### T6-3 Keyboard and jargon text shown to phone users
- **Severity:** polish.
- **Actual:**
  - The comment box says "Anything else? (F to type, optional)".
  - Top holes ends with "Score: the like share each hole has at least (Wilson 95% lower bound)…", and the scores read as bare "51" / "21".
  - The card shows "MODEL 77%" with no explanation.
  - On a phone F means nothing, and "Wilson" and "model" mean nothing to a friend (`04-chip-comment.png`, `08-top-portrait.png`).
- **Status:** Fixed (2026-10-02): the comment box says "Anything else? (optional)" on touch screens ("F to type" only with a keyboard). Top holes ends with "Ranked by likes, with confidence: more votes count more. Only holes with more 👍 than 👎 make the list." (the Wilson details are in its tooltip and the score's). MODEL is now **Taste match**, with the tooltip "how much the model expects you'll like this hole…". `…/tmp/trainerfix6/09`, `13-*.png`.

#### T6-4 While waiting for new holes, the skipped hole stays rateable behind the card
- **Severity:** polish.
- **Actual:** after Skip with nothing unseen ("Generating new holes… 3 of 12 ready" or "No new holes until more are rated"), the header still shows the hole you just skipped. Like, Dislike, Skip and Submit stay live under the waiting card, so a tap rates the skipped hole without the friend realising it. The waiting card also covers the joystick (`14-waiting-portrait.png`, `15-waiting-capped.png`; `/api/next` was mocked).
- **Suggested:** dim the old hole's rate panel, or label it "Rate the hole you skipped".
- **Status:** Fixed (2026-10-02): while waiting, the rating panel is replaced by "Nothing to rate yet: the hole behind is one you already skipped or rated…; to rate a hole you skipped, pick it from Revisit a recent hole", and the waiting card always offers that list. The 1 / 2 / Enter hotkeys and `submit` do nothing while waiting. Client-side only (rating a skipped hole on purpose stays allowed on the server). Checked with a mocked `/api/next`: no thumbs, no `/api/rate` call, `…/tmp/trainerfix6/16`, `17-waiting-*.png`.

### Not tested / notes (round 6)
- The game's 3D build of the holes wasn't checked (the Unity Editor was busy). The trainer's 3D view matched the previews on the three top holes viewed: `links_967464816`, `forest_870462187` and `lakes_249237828`.
- No objects in water: `validate` passes all 417 pool holes, and one v5 crown edge reaches over a pond (`forest_6097`).
- The ball-flight model in `hole_qa.py` is rough (no wind, a fixed apex). Tree "blocks" are trees within reach of a normal flight. Lower shots (punches, mishits) hit more.
- The real refill and generation wait wasn't triggered (refill rule). `e2e1`'s state afterwards: 1 new 👎, 1 skip and 3 peeks.

---

## Round 5

Moved to [bug-hunt-round5.md](bug-hunt-round5.md) to keep this file short.

## Round 4

Moved to [bug-hunt-round4.md](bug-hunt-round4.md) to keep this file short.

## Round 3

Moved to [bug-hunt-round3.md](bug-hunt-round3.md) to keep this file short.

## Round 2

Moved to [bug-hunt-round2.md](bug-hunt-round2.md) to keep this file short.

## Round 1

Moved to [bug-hunt-round1.md](bug-hunt-round1.md) to keep this file short.
