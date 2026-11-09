# Media

Images and GIFs used by the main README. The `~` makes Unity skip this folder.

Files the README expects:
- `hero.gif`: the demo scene, all presets cycling
- `outline-dash.gif`: radial and linear dashes up close
- `hologram.gif`: ghost preview in Replace mode
- `see-through.gif`: object behind a wall + ground reticle
- `inspector.png`: the inspector with a few sections open

I record with Unity Recorder and convert with:

    ffmpeg -i in.mp4 -vf "fps=24,scale=820:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse" out.gif
