# How PrismFX Highlight works

Notes on the main decisions, mostly so future me remembers why things are the way they are.

## The basic idea

I didn't want to touch anyone's materials. Swapping materials or adding extra material slots gets messy fast: SRP batching, shared materials, skinned meshes, and restoring everything when the effect ends.

So instead:

- `PrismHighlight` keeps its own material instance and registers itself in a static list, but only while it actually has something visible.
- `PrismHighlightFeature` goes through that list each frame and calls `DrawRenderer(renderer, material, submesh, pass)` for every pass it needs. This works for skinned meshes too, since Unity handles the skinning.

The one exception is hologram *Replace* mode. It does swap `sharedMaterials`, because that's the whole point of it, and it restores them on disable.

Render Graph uses `RasterCommandBuffer` and Compatibility Mode uses `CommandBuffer`. I didn't want to write the drawing code twice, so there's a tiny `IPrismDrawSink` interface with one struct per buffer type. `Render<T>` is generic over a struct, which avoids boxing. A test makes sure the whole path allocates nothing.

## Pass order

For each highlighted object:

1. **See-through.** Uses `ZTest Greater`, plus the stencil so overlapping triangles only draw once.
2. **Surface.** Overlay, inner glow and hit flash are all one pass. They used to be separate, but there was no reason to draw the mesh three times.
3. **Hologram** (overlay mode).
4. **Mask.** Writes the silhouette into one stencil bit.
5. **Glow layers.** Each layer is a bit wider than the last and drawn additively, so the falloff comes for free.
6. **Outline.**
7. **Clear.** Resets the stencil bit. Without this, two overlapping highlighted objects cut into each other's outlines.
8. **Target** quad.

### The stencil bit mistake

My first version used bit 64 for the stencil. It worked fine in Forward. Then I read through URP's `StencilUsage` and found that bits 5–6 hold the material type in Deferred, so outlines would have disappeared on SimpleLit surfaces there. URP reserves bits 0–3 for users. The package now uses 8 by default, and you can change it on the renderer feature.

## Constant pixel width

The outline is an inverted hull. The usual approach pushes vertices out along the normal in object space, which makes the outline thinner the further away the object is. Instead I push in clip space:

```hlsl
float3 dirCS = mul((float3x3)GetWorldToHClipMatrix(), normalWS);
float2 dir   = normalize(dirCS.xy) * saturate(length(dirCS.xy) * 4);
posCS.xy    += dir * widthPx * 2 * posCS.w / _ScaledScreenParams.xy;
```

- Multiplying by `w` cancels the perspective divide.
- The `saturate(length * 4)` term stops normals that face the camera from shooting out in random directions.
- `_ScaledScreenParams` is the per-eye size in XR, so the width is correct in VR too.

Flat-shaded meshes have split normals, which leave gaps at the corners. *FromCenter* mode pushes along the vertex position from the object center instead. It's not perfect for weird shapes, but it fixes boxes and machined parts, which is most of what I highlight.

## Dashes

Every dash mode works the same way: compute a 1D coordinate, take its `frac`, and antialias the edges with `fwidth`.

- **Radial:** the coordinate is `atan2` around the object's projected center. I round the dash count to a whole number, otherwise there's a visible seam where the angle wraps.
- **Linear:** the coordinate is a dot product with a direction, either in screen space or in world space.

## Hologram

The hologram is its own shader, so you can also put it on a regular material.

- Scanlines, the sweeping band and the fresnel are all cheap math.
- The glitch offsets horizontal bands along the camera's right vector. I keep the probability low on purpose: big jumps that differ between the eyes feel bad in a headset.
- The optional depth prepass lives in an `SRPDefaultUnlit` pass and gets toggled with `SetShaderPassEnabled`.

## CPU side

- **Idle components** return on the first line of `Update`.
- **Material properties:** the static ones are only sent when settings change. Fade and see-through intensity are sent only when their values actually change.
- **Bounds** are only computed when something needs them: radial dashes, the target, or a linked dashed line. `Renderer.bounds` on a skinned mesh costs more than you'd expect.
- **Hit flash:** I store the start time in `Time.timeSinceLevelLoad`, which is the same clock as `_Time.y` in the shader. The fade-out then happens entirely on the GPU.

## Testing

Unity tests:
- **EditMode** covers shaders and passes, project setup, presets and renderer discovery. It also renders a cube through URP and checks the outline pixels.
- **PlayMode** covers fade, hit, hologram swap and restore, events, audio, exact draw counts and a zero-allocation check with 50 highlights.

Outside Unity, I compiled every pass with DXC against the URP 17.0 and 17.7 shader libraries:
- D3D11 and Vulkan
- Normal, instanced and single-pass-instanced stereo variants

I also checked the Render Graph calls against the URP source for 6.0, 6.3 and 6.7.
