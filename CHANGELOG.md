# Changelog

## 1.2.0 (2026-11-17)

- **Focus.** While an object is highlighted, the rest of the scene gets dimmed, desaturated and blurred.
  - One copy of the camera color and one fullscreen triangle per camera, and only while a focus is active. On other frames, URP doesn't even create the intermediate texture for it.
  - Settings: color, dim, desaturate, blur + radius, and "reveal through walls".
  - Several focused objects at once all stay bright; the strongest one sets the look.
  - Works with both Render Graph and Compatibility Mode on Unity 6.
- **Distance culling.** Objects past Max Distance leave the render list (saves draw calls and CPU), with a fade range so they don't pop.
- **LOD Groups.** Only the visible LOD is drawn. This was mostly true already through `Renderer.isVisible`; it's now documented and the code path is shared.
- `IPrismDrawSink` gained `DrawFullscreen`. If you implemented it yourself, add that method.

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
