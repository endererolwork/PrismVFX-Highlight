# Changelog

## 1.1.0 (2026-11-10)

- **Proximity.** The highlight fades in as a `PrismProximitySource` (fingertip, controller) gets close. Details:
  - start/full distance, response curve and smoothing
  - per-effect "Affects" toggles
  - bounds or collider distance
  - optional main-camera source for non-XR builds
- **Touch glow.** A spot under the fingertip, drawn in the existing surface pass, so no extra draw call.
- `onProximityEnter` / `onProximityExit` events and an optional enter sound.
- Edit-mode proximity preview slider, a live meter in Play mode, and range gizmos.
- GameObject > PrismFX > Add Proximity Source.
- Fade is now per effect internally, so proximity can drive the outline without touching the overlay.

## 1.0.0 (2026-11-09)

First public version.

- `PrismHighlight` with:
  - outline (solid or dashed)
  - outer and inner glow
  - overlay
  - hologram
  - target reticle
  - see-through
  - hit/select/error flash
  - sounds
- `PrismHighlightFeature`, with Render Graph and Compatibility Mode support
- `PrismDashedLine`
- Presets, profile assets and a custom inspector
- Setup / validate / demo scene menu items under Tools > PrismFX
- EditMode and PlayMode tests
