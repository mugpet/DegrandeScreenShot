# Scroll capture stitching

Verified working in v0.2.9 after several broken iterations. Keep these rules.

## What works

- Match consecutive viewports by **minimum-SAD vertical shift** on per-row luminance profiles, not by row-equality or "fraction of similar rows".
- Row profile: 64 luminance buckets across the central analysis band (`HorizontalAnalysisBandPercent=30`, min 96px wide).
- Acceptance: best shift must be both `bestSad < baselineSad * 0.6` (no-shift baseline) AND `bestSad < 1500`. Otherwise treat as "no scroll detected" and fall back to `lastAppendStartRow` / wheel.
- `ScrollPositionChanged` and `BitmapsEqual` use the same SAD-similarity (`RowProfileSad <= 512` per row) — never the old exact-hash path.

## What fails (do not reintroduce)

- Exact-equality row hashes (FNV or quantized 4-bit signature). Antialiased text shifts luminance across quantization boundaries and drops the match ratio to ~1% even on the true overlap.
- "Fraction of similar rows" overlap matchers. Many whitespace rows look alike, producing a false full-viewport overlap → 0 rows appended each iteration → only the first viewport survives.
- Scrollbar-strip change detection. The cursor is parked on the scrollbar edge and obscures the indicator; rely on content-band SAD instead.

## Key files

- `src/DegrandeScreenShot.App/Services/ScreenCaptureService.cs`: `StitchScrollingFrames`, `FindBestVerticalShift`, `AverageProfileSad`, `ComputeRowProfiles`, `ScrollPositionChanged`, `BitmapsEqual`.
- `src/DegrandeScreenShot.Core/ScrollCaptureStitcher.cs`: legacy equality matcher kept only for `FindDynamicTop` / `FindDynamicBottomExclusive` viewport detection and unit tests.

## Diagnostics

On stitch failure (and on each successful run), `%TEMP%\dss_log.txt` records per-frame `shift / bestSad / baselineSad` plus `dss_frame{0,1}.png` and `dss_vp{0,1}.png`. Always read it before changing the matcher again.
