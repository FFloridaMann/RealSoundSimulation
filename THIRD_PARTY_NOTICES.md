# Third-party notices

## Sound Engine Backport, by red_the_fox

https://sp-mod.com/mod/3314/sound-engine-backport

Released under the MIT licence. Used in Real Sound Simulation:

- `src/RealSoundSimulation/Fixes/FoleyPresets.cs` (setting `10a`): the distance curves, stereo spread and 3D processing values for the Character and Weaponary sound groups, and the logic that fades 3D processing by distance, angle and height. The author describes these as EFT 1.0's values.
- `src/RealSoundSimulation/Acoustics/ExtraRoutes.cs` (setting `10b`): the way its route files are loaded and checked against the map. The route files themselves are installed locally and are not in this repository.
- `tools/build_seb_sounds.py` (setting `0`): uses its gun sound bundles, installed locally and not in this repository.

The game audio and data inside its bundles and route files belong to their original owners and are not covered by this notice.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Bring Back Concussion, by harmony (harmonyzt)

https://forge.sp-tarkov.com/mod/2550/bring-back-concussion

https://github.com/harmonyzt/BringBackConcussion

Released under the MIT licence, as listed on its Forge page, with the name and year left blank there. The name and year below come from its GitHub repository, created in 2026, and haven't been confirmed by the author. Used in Real Sound Simulation:

- `src/RealSoundSimulation/Fixes/ConcussionProof.cs` (setting `6.9h`): adapts its sound cut: how deep, how fast and how long the cut is, and how it gives way to the game's tinnitus. That mod is still needed for the concussion itself.

MIT License

Copyright (c) 2026 harmony (harmonyzt)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
