# Aspect-Ratio Resolution Filter Design

## Goal

Add an aspect-ratio dropdown that filters resolution presets and makes the unfiltered list easier to scan. Improve the visible `Copy primary` wording without changing its persisted value or behavior.

## User interface

The `Display` group gains an `Aspect ratio` dropdown above `Resolution preset`.

- Its first item is `All aspect ratios`.
- Remaining items are ratios present in the available resolution presets, sorted from narrowest to widest.
- Common ratios include a plain-language label: `1:1 (Square)`, `4:3 (Standard)`, `5:4 (Standard)`, `3:2 (Classic)`, `16:10 (Wide)`, `16:9 (Wide)`, `21:9 (Ultrawide)`, and `32:9 (Super ultrawide)`.
- Unknown ratios use their reduced numeric form without an invented label.

When `All aspect ratios` is selected, each normal resolution includes its ratio and label, for example `1920 x 1080 (16:9 Wide)`. When a specific ratio is selected, normal resolutions show only dimensions, for example `1920 x 1080`.

The visible `Copy primary` item becomes `Match primary display`. The internal `CopyPrimary` settings key remains unchanged, preserving existing saved settings. `Match primary display` stays first and `Custom` stays last.

## Ratio classification and ordering

Resolution dimensions are reduced with their greatest common divisor. Common display variants that manufacturers market under a nearby standard ratio are assigned to the nearest known ratio only through an explicit 3% relative tolerance; this groups modes such as `3440 x 1440` and `2560 x 1080` under `21:9 (Ultrawide)`. If no known ratio is within that tolerance, the exact reduced ratio is used.

The complete resolution list is ordered by classified aspect-ratio value, then width, then height. Filtering retains that order.

The filter contains only ratios represented by available normal presets. It does not add resolutions. `Match primary display` and `Custom` remain available under every filter because neither is a normal preset category.

Changing the aspect-ratio filter keeps the current normal preset selected when it belongs to the new filter. Otherwise the first matching normal preset is selected. If no normal preset matches, `Match primary display` is selected. Width, height, refresh, validation, and persistence continue through the existing selection flow.

## Components

`ResolutionOptions` owns pure ratio classification, labeling, and aspect-first sorting. `MainForm` owns the new dropdown and rebuilds the existing preset choices from one cached, sorted resolution list. No new abstraction or dependency is added.

The aspect-ratio filter is a view preference and is not persisted. All five resolution controls are disabled while the virtual display is active or transitioning.

## Testing

The executable self-test covers:

- Exact reduction and common ultrawide classification.
- Unknown-ratio fallback.
- Aspect-first then dimension ordering.
- Filter item ordering and labels.
- Unfiltered resolution labels containing ratios.
- Filtered resolution labels containing dimensions only.
- Selection preservation and fallback.
- `Match primary display` wording with unchanged `CopyPrimary` persistence semantics.
- Aspect-ratio control locking and unlocking with other resolution controls.

Verification remains a Release build plus `--self-test`. No live display smoke test is required.

## Non-goals

- Adding resolution presets.
- Persisting the selected aspect-ratio filter.
- Filtering refresh rates.
- Changing custom-dimension validation or driver behavior.
