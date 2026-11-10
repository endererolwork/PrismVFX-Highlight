# PrismFX Highlight

Highlight and selection effects for Unity URP, built with XR training sims in mind.

<!-- MEDIA: uncomment when the GIF is in Media~
![demo](Media~/hero.gif)
-->

I build VR maintenance and training simulations. In those, half the UX is telling the trainee what to look at: grab this bolt, wrong part, this goes there. The highlight assets I tried either broke in single-pass VR, cost too much on Quest, or still relied on the old render path. So I wrote my own. It started as a simple outline and grew from there.

It's the first piece of a small set of VFX I'm putting together under the name **PrismFX**.

## What's in it

One component, `PrismHighlight`, with these effects:

- **Outline** with constant pixel width. It can also be dashed: radial dashes that run around the silhouette, or straight stripes in screen or world space.
- **Outer glow** built from 1–4 layers, HDR so it blooms, with an optional pulse.
- **Inner glow**: a fresnel rim.
- **Overlay**: a color or texture on top of the surface, with UV, triplanar or screen mapping.
- **Hologram** with scanlines, a sweeping band, flicker and a bit of glitch. It can sit on top of the object or replace its materials. Replace is handy for "put the part here" ghost previews.
- **Target**: a rotating reticle, either facing the camera or lying on the ground.
- **See-through**: shows the object when it's behind a wall.
- **Hit flash**: `HitFX()`, `Select()` and `Error()`.
- **Proximity**: the highlight fades in as a hand gets close, and a soft glow follows the fingertip on the surface. You can see what's grabbable before you reach for it.
- **Sounds**: on/off, hit, select, error and a hologram hum. They're 3D, with a small cooldown so fast hovering doesn't spam them.

On top of that:

- `PrismDashedLine`, a flowing dashed line between two objects (part → slot, object → label). It fades with the highlight.
- Presets and profile assets, so many objects can share one look.

<!-- MEDIA: uncomment when the files are in Media~
| | |
|---|---|
| ![](Media~/outline-dash.gif) | ![](Media~/hologram.gif) |
| ![](Media~/see-through.gif) | ![](Media~/inspector.png) |
-->

## Install

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/endererolwork/PrismVFX-Highlight.git
```

There's also a `.unitypackage` on the [Releases](../../releases) page if you prefer that.

After installing, run **Tools → PrismFX → Setup Project** once. It adds the renderer feature to your URP renderers and puts the shaders in *Always Included*. If you want to see everything working first, **Tools → PrismFX → Create Demo Scene** builds a test scene for you.

Requires Unity 6 and URP 17. Both Render Graph and Compatibility Mode work.

## Usage

Add **PrismFX → Prism Highlight** to an object and tick **Highlighted**. The effect shows in the Scene view right away. Pick a preset or open the sections and tweak. In Play mode the inspector gets buttons to test hit/select/error.

From code:

```csharp
var h = GetComponent<PrismHighlight>();

h.highlighted = true;   // fades in, plays the sound, fires onHighlightStart
h.Select();             // short rim flash + select sound
h.Error();              // red flash + error sound

h.settings.outlineDashMode = PrismDashMode.Radial;
h.Refresh();            // needed after changing settings from code
```

### Proximity (hand tracking)

1. Turn on **Proximity** in the inspector.
2. Select the fingertip bones of your hand rig (or your controller tips) and pick **GameObject → PrismFX → Add Proximity Source**.

That's all you need. Start/Full Distance control when it kicks in. Under *Affects* you choose which effects react; by default that's outline and glow. *Touch Glow* is the spot under the fingertip: it starts wide and faint and tightens as the finger gets closer.

`onProximityEnter` / `onProximityExit` fire once per approach, which makes them a good place for a haptic pulse. On tablet or desktop builds, *Include Main Camera* makes the camera count as a source.

It's cheap. Each object does one box-distance check per source per frame, and far-away objects only refresh their bounds every few frames. The touch glow is drawn in the existing surface pass, so it adds no extra draw call.

With XR Interaction Toolkit you don't need any code: point *Hover Entered / Exited* at `PrismHighlight.highlighted` and *Select Entered* at `Select()`.

## How it works (short version)

A renderer feature draws the extra passes with `DrawRenderer`, so your materials stay untouched. Each object goes through these passes: stencil mask, glow layers, outline, clear. The outline is extruded in clip space, which keeps its width in pixels no matter how far away the object is.

Objects that aren't highlighted don't do any work at all. Neither the renderer nor `Update` touches them.

I've written the longer version, including a couple of things I got wrong along the way, in [Documentation~/ARCHITECTURE.md](Documentation~/ARCHITECTURE.md).

## Tests

Both EditMode and PlayMode tests are included. To run them, add the package to `testables` in your manifest:

```json
"testables": ["com.endererol.prismfx.highlight"]
```

My favorite one renders a cube through URP and counts outline-colored pixels. Another checks that 50 active highlights don't allocate anything per frame.

## Known limitations

- **URP only.** Built-in and HDRP won't work.
- **Flat-shaded meshes:** the outline can split at hard edges. Set *Normal Mode* to *FromCenter*.
- **Hologram Replace** only swaps materials in Play mode. In the editor it previews as Overlay.
- **See-through** reacts to everything in front of the object. There's no occluder layer filter yet.
- **On Quest**, I'd keep glow at 2 passes and not have more than about 10 highlights visible at once.

## Next up

Dissolve/assemble and an x-ray/cutaway effect are next on my list. After that: guidance arrows and a few VFX Graph things (sparks, smoke).

## About me

I'm Ender, a Unity developer from Ankara. I've been working with Unity for 5+ years, mostly on AR/VR: training simulators, multiplayer VR procedures and equipment sims. My background is a mix of software and electrical/mechanical engineering, plus a master's degree on the computer vision side.

[LinkedIn](https://www.linkedin.com/in/endererol-gd) · [GitHub](https://github.com/endererolwork)

Issues and PRs are welcome.

## License

MIT
