# SCORAM — Performance Round 4 (P1 React, student site) Report

Scope: `ScoramWeb` student site only (admin panel untouched). No backend, API-contract or package change (no new dependency).

## 1. Honest status — what was and was not run
Unlike Rounds 2–3, the JS toolchain works here, so this round was actually executed:
- `npm ci`, `npm test`, `npm run lint`, and `vite build` were run.
- **Tests: 175 pass** (131 existing + 44 new). One existing test file, `Login.captcha.test.jsx`, fails **before and after** my change: the supplied zip has no `src/assets/*.png` (logos), so it cannot import `Login.jsx`.
- **Build:** the same missing images make `vite build` fail on the supplied copy. With temporary 8-byte placeholder PNGs (removed again, not in the zip) the build succeeds. In your real repo, where the images exist, this should not apply.
- **Lint:** 109 warnings at baseline → 107 now, none new, 0 errors (all remaining warnings are pre-existing, e.g. `set-state-in-effect`).
- **Not done:** no real browser against the real API. The manual checklist is in section 7.
- Mutation-checked: removing the stale-append guard, the abort check, or the "only when needed" total gate each makes a test fail.

Local git (this sandbox only; the zip carries files, not history): baseline `8e4c1d5` + round commit `7680904`.

## 2. Decisions applied (yours)
- Cache is **memory only**, **3 min fresh** (a stale value is shown at once and refreshed in the background). Nothing is written to the browser, so a full reload starts empty.
- Shared by the **Question Bank page, Practice Tests, the home and landing "Popular Exams" strips and the signed-out exam list**.
- The two **P2 bugs in the page's `loadPage`** are fixed in this round (see 3d).

## 3. Changes

### a) New cache — `src/api/catalogCache.js`, `src/hooks/useCatalog.js`
- **Keyed cache + in-flight de-dup + stale-while-revalidate.** Concurrent callers for a key share one request. React StrictMode's double effects no longer double-fetch.
- **The fetcher never receives a caller's AbortSignal**, so one screen unmounting cannot cancel a request another screen waits on.
- Failures are never cached, and a failed background refresh keeps the stale value.
- **Student isolation.** The catalog endpoints are My-Exams scoped, so a user-scoped key embeds the student: the JWT `sub` (read from the token, because the token is written synchronously on login while React `user` state is not yet there when a child's first effect runs). If a token has no readable `sub`, the key falls back to that token's own tail — it can never match another student. Public data (the exam list) uses a shared `pub` key and survives logout.
- **Epoch guard:** a request that started before a clear/invalidate can never write its result afterwards (logout → another login cannot see the previous student's data).
- **Cleared / invalidated on:** logout and account deletion (`AuthContext`), the `scoram:session-expired` event (also covers single-device revoke), and My Exams changes (`MyExamsContext`: `save`, `addExam`, `setPrimary` always; `refresh`/`removeExam` only when the exam set really differs; the first load only records a baseline). Mounted screens refetch by themselves when their entry is dropped.
- Clock moved backwards → entry treated as stale.

### b) Question Bank page (`QuestionBankSearch.jsx`)
- Exams, years and subjects come from the cache: a revisit within 3 min paints instantly with **zero catalog requests**; older → instant paint plus one background refresh.
- **The blocking `search pageSize=1` stat call is gone from the normal path.** The "Questions" total is seeded from the unfiltered first page's `totalCount` (same number, no request). Only when the student arrives with a search/filter/restored position and no total is cached is ONE `pageSize=1` request made — after the first page has settled, so it never competes with the page they wait for.
- Subjects keep the previous behaviour: when the exam changes, the old list stays until the new one arrives (an empty list in between would have flipped the exam-wise mode and fired a wrongly filtered page load).
- Topics go through the cache and request de-dup; they now also have a cancel guard.

### c) Other screens
`PracticeTests` (subjects / exams / topics share the Question Bank's cache entries), `PopularExams` (home), `PopularExamsSection` (landing) and `useSelectableExams` (signed-out) share one `/api/exams` request. `listExams({search|organizationId})` (My Exams picker) is deliberately not cached. Cached arrays are never mutated by any consumer (checked: all sorts copy first).

### d) P2 fixes bundled (yours to accept/reject — same code path)
- **Abort treated as error:** `apiFetch` rewrites a fetch `AbortError` into an `ApiError(timedOut)`, so the old `err.name === "AbortError"` never matched and **every filter change flashed the error panel**. New `isCallerAbort(err, signal)` in `api/client.js` (the caller's own signal is the test). Also applied to `useSelectableExams` (rewritten on the cache) and `OrganizationExamPicker`. A real failure or timeout still shows the error.
- **Stale "load more":** appends now share a generation counter and an in-flight set; a filter change, a new first page or leaving the page aborts all of them, and a result from an older generation is ignored (it could previously append old-filter items and overwrite `totalCount/hasMore/pageRef`).

## 4. Test → change map
| Test file | Covers |
|---|---|
| `api/__tests__/catalogCache.test.js` (18) | identity per student (incl. fallback), public key shared, fresh = no refetch, one shared request, no signal passed, SWR + notify, failed refresh keeps stale, failure not cached, clock-back, `setCatalog`, clear keeps public, in-flight result after clear can't repopulate, post-clear request is fresh, invalidate notifies, session-expired clears |
| `hooks/__tests__/useCatalog.test.jsx` (9) | first-render paint, miss → load once, shared request across components, `enabled`, failure + `reload`, late result of an old key never shown, auto-refetch on invalidate, recovery when cleared mid-flight, unmount |
| `pages/__tests__/QuestionBankSearch.catalog.test.jsx` (6) | plain open: no `pageSize=1`, total from page, each list once under StrictMode; revisit = no catalog requests; filtered arrival = one `pageSize=1` after the page; superseded request aborted and no error flash; late load-more not appended; real failure still shows the error |
| `context/__tests__/catalogInvalidation.test.jsx` (6), `catalogLogout.test.jsx` (2) | baseline load keeps cache; refresh invalidates only on a real change; save / add / setPrimary / remove; logout and deleteAccount clear |
| `api/__tests__/isCallerAbort.test.js` (3) | the helper |

Page tests fake only `apiFetch` and the heavy child components; the page, `useCatalog` and the cache are real.

## 5. Files
Modified: `api/{client,exams,questionBank}.js`, `components/exams/OrganizationExamPicker.jsx`, `components/home/PopularExams.jsx`, `components/landing/PopularExamsSection.jsx`, `context/{AuthContext,MyExamsContext}.jsx`, `hooks/useSelectableExams.js`, `pages/{PracticeTests,QuestionBankSearch}.jsx`.
New: `api/catalogCache.js`, `hooks/useCatalog.js`, and the five test files above.

## 6. Remaining risks / not done
- Up to 3 min of stale catalog on a revisit (then refreshed): a just-added exam or question count can lag briefly. My Exams changes made in this tab invalidate at once; changes made on another device/tab are picked up within 3 min or on a reload.
- The topic list uses the cache for de-dup but is not subscribed to invalidation while mounted (My Exams is edited on a different screen, so the page is normally unmounted).
- "Surprise Me" reads the cached total; a total up to 3 min old could pick a position past the end if questions were removed in that window (same class of issue as before, with a smaller window).
- The standalone `/search` (`SearchQuestions.jsx`), `QuestionBankQuestionDetail` and admin screens were not changed. The progress-restore path (pages 2..N in parallel, unbounded) is untouched — the Flutter concurrency cap was not ported here (P2 candidate).
- The `loading="lazy"` images and lazy list rendering are still in the backlog.

## 7. NOT YET RUN BY ME — checklist for you
1. `npm ci`, `npm test`, `npm run lint`, `npm run build` in your real repo (the logo images exist there).
2. Browser, network tab, signed in: plain open of the Question Bank → no `search?...pageSize=1`; `exams`, `years`, `subjects` each once (also in `npm run dev`, which uses StrictMode); the "Questions" card shows the total.
3. Navigate away and back within 3 min → no catalog requests; after 3 min → instant paint, one refresh.
4. Open Practice Tests after the Question Bank → no repeated exams/subjects calls. Home and landing share one `/api/exams`.
5. Change My Exams, go back → lists match the new scope; log out and log in as another student → no previous student's data, even for a moment.
6. Type quickly in the search box and change filters → no "Something went wrong" flash, the list matches the last filter; scroll to load more while changing a filter → no old items appear.
7. Open with a shared filtered link → the Questions card appears shortly after the list (one single-row request).

## 8. Next (unstarted)
P2: `ExamTaxonomyService` nested correlated `OR EXISTS`, `GetById`/`Today` sequential counts (API-08), admin list/stats (API-11/RA-01), lazy list rendering, `loading="lazy"` images, React restore concurrency cap. P3: deep-offset / non-unique sort and Surprise Me (API-10/RW-05), admin panel polish, payload trimming. Tell me which.
