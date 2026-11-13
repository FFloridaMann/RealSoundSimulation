# Code review, 13 Nov 2026

A read-only review of `src/RealSoundSimulation` at `abd80e4`. Nothing was changed, built or installed. Paths are relative to `src/RealSoundSimulation/`.

## Verdict: is it spaghetti?

**Mostly no, with one real tangle.** Each feature sits in its own file, with its own switch, its own reset and its own error catch. A crash in one feature does not take the others down. The mixer owner (`2bcd97c`) was a genuine rewrite, not another layer: it deleted every competing "where to go back to" copy from `ConcussionProof`, `ShockWave` and `GunfireHearing` (about 200 lines) and put one owner in their place. The honest answer to your question is **both**. Over the last three days the code grew by +2,684 / −156 lines (21 commits), so almost everything was added and almost nothing taken out. Within a day, fixes were stacked on fixes. For example, `7a18758` added a fourth copy array (`Normal`) to `ConcussionProof` to undo a stuck −6 dB, and 70 minutes later the mixer owner threw all four copies away. **The tangle is wherever several features share something the game owns.** The mixer was one such place, and it is now fixed. The others are still open: the echo voice pool, the gun voice pool, the game's ring timer, `FireBullet` (patched 18 times), and raid start/reset, which lives inside a diagnostics class. That is where the next bugs will come from.

## Problems, ranked

**1. The echo voice pool has no owner. Rewrite (before 1.0, it's your planned "voice-pool budgets" step).**
`EchoVoices` is a shared pool used by 9 files, but it lives inside your gun's echo file (`Acoustics/OwnEchoes.cs:578`). It has already caused one bug: turning your echoes off silenced the thump, blasts and pass-bys (`54e4fdf`). Five call sites skip its budget completely with `ignoreCap: true` (`Fixes/ThroughWall.cs:184,187`, `Fixes/ShockWave.cs:323,331,361`). The output group and volume are passed through a global switch: `ConcussionProof.FeltOn/FeltOff` sets `EchoVoices.OutputOverride` and `VolumeTrim` (`Fixes/ConcussionProof.cs:347-358`), every `Play` in between picks them up, and `ShockWave.cs:327` reads the trim back to undo it.
*Fix:* move it to its own file, `Audio/EchoVoices.cs`. Pass the output group and trim as arguments to `Play`. Replace `ignoreCap` with a small reserved budget per kind of sound (own, bot, blast, felt).

**2. Raid start and reset are owned by diagnostics. Rewrite (before 1.0, small).**
`Diagnostics/DiagRunner.cs:170` is one line that resets 33 systems, including `MixerOwner`, `GameRing` and `ConcussionProof`. The whole raid start runs inside one `Plugin.Safe` (`:82`), so if one reset throws, every reset after it on that line is skipped for the whole raid. `DiagRunner` also drives the two sound features `RoomTail.Tick` and `ShotOverlap.Tick` (`:98-101`). "Are we in a raid" is decided differently in at least four places: `DiagRunner.cs:81` uses `GamePlayerOwner.MyPlayer`, `MixerOwner.cs:141` excludes `HideoutGameWorld`, and `GunfireHearing.cs:135,214` does its own check.
*Fix:* add a small `Raid` class with one `IsOn` test, a list of resets each run in its own try, and the per-frame ticks. `DiagRunner` then only logs.

**3. The gun voice pool's borrowers each set voices up their own way. Rewrite (small, before 1.0).**
`ShotOverlap` borrows from the game's gun pool in two places (`Fixes/ShotOverlap.cs:158` and `:288`), and each sets the voice up by hand. The latest fix (`abd80e4`, `:211-216`) resets occlusion on **every shot** because a borrowed voice can come back still occluded from the gun it last served. That treats the symptom where it shows up, not where it starts.
*Fix:* add one `BorrowGunVoice(own)` helper that clears occlusion, stereo and mixer group as it borrows. Keep the per-shot check for one raid, then remove it if `OwnUnoccluded` stays at 0.

**4. Two ways to hold the ear ring while firing. Delete (after one or two raids confirm 6.9u).**
`GunfireHearing.HoldGameRing` (`Fixes/GunfireHearing.cs:299-323`) is the old way: it pushes the game's ring end later. `GameRing.Hold` (`Fixes/GameRing.cs:153-166`) is the new way. The old one still runs whenever "mod plays the game ring" is off or has failed. There are also two separate ring sources (`GameRing.cs:214`, `GunfireHearing.cs:273`), and the game's private ring-end field `float_2` is looked up in three files.
*Fix:* once 6.9u is trusted, remove the old hold and the `RingTakeoverOn` switch. If 6.9u fails, keep only the game's own ring as the fallback.

**5. The headset check lives in an off-by-default feature, and there are two meanings of "headset". Rewrite (small).**
`GunfireHearing.WearsHeadset` (`:95`) is used by `MixerOwner.cs:364` and `ConcussionProof.cs:217`, but gunfire ear ringing is off by default (`Plugin.cs:217`). It counts a helmet with a built-in headset. The mixer owner's "normal Guns level" comes from the game's `CurrentHeadphonesTemplate.DryVolume` instead (`MixerOwner.cs:210`). If the two disagree, for example with a helmet's built-in headset, the crack and cut use headset values while the mixer uses no-headset values. Worth one test raid with such a helmet.
*Fix:* move it to a `Gear` helper, checked twice a second, used by everyone.

**6. Silent failures that change the sound. Rewrite (tiny).**
There are 27 empty `catch {}` blocks. Most are in logging or clean-up and are fine. Two are not:
- `MixerOwner.Dry` (`:208-212`) returns 0 dB if the headset template can't be read. With a headset that would push Guns from about −55 dB to 0 dB.
- `WearsHeadset` (`GunfireHearing.cs:104`) returns "no headset" on any error.

*Fix:* report each once, and in the mixer owner leave Guns alone when its normal value is unknown.

**7. `BodySounds` is still in the build. Delete (move to a branch).**
It is 801 lines (`Fixes/BodySounds.cs`) with an 89-line header, 16 settings (`Plugin.cs:150-159`), Harmony patches installed at start (`Plugin.cs:490`), a per-frame component (`:511`), and a caller of the shared echo pool (`:591`, `:750`). It is off and out of 1.0, but every change to the echo pool still has to keep it working.
*Fix:* move it to a `body-sounds` branch and take it out of main.

**8. Settled A/B switches keep two code paths alive. Delete (during the settings clean-up).**
I found no setting that nothing reads: all 213 are used somewhere. But many only choose between an old path and a new one that has already won, for example `ShotOverlapOn`, `TailHandoffOn`, `OwnRingOutOn`, `GunMixerOn`, `ObstructionModeCfg`, `EchoReplaceOn`, `RingTakeoverOn`, `SoftCutOn`, `CrackOn` and `HeardThroughOn`, plus `BodySteepOn` ("only for the F8 test now"). Two timing diagnostics are on by default for every player (`Plugin.cs:444-445`).
*Fix:* for each switch that is settled, delete the "off" path along with the setting. Put the timing switches in the developer build only.

**9. Copied helpers. Rewrite (mechanical, any time).**
The game's private `playersBridge` field is declared 16 times across 15 files, `_queue` 8 times and `_isFiring` 4 times. The tone generator `ShockWave.MakeClip` is used by `Adrenaline`, `BodySounds` and `FragmentPassBy`, so it doesn't belong in `ShockWave`. The biquad filter only exists in `Adrenaline` (fine).
*Fix:* one `GameRefs.cs` for the private fields, and a `Synth.cs` for `MakeClip`. If a game update renames a field, you then fix it in one place.

**10. One game method, many hands. Leave for 1.0, rewrite after.**
`FireBullet` is patched 18 times (diagnostics included), `StopFiringLoop` 9, `PickClipsByDistance` 7 and `Enqueue` 6. Their order depends on Harmony priorities (`GunSoundSource.cs:59`, `ShotVariation.cs:26`, `OutdoorTail.cs:56`, `RoomTail.cs:69`) and on hidden hand-offs between patches, such as `RoomTail`'s `[ThreadStatic] _own` flag set in one patch and read in another (`RoomTail.cs:39,78-88`). It works, but adding a sixth feature to a shot is where things will start breaking each other. After 1.0, build one shot pipeline that runs the features in a fixed order.

**11. The run order of per-frame components is spread over comments. Leave.**
Order 32000 (`ShockWave`, `BlastFromOpening`, `PortalPositioning`, `FragmentPassBy`), then 32001 (`BotEchoes`, `BodySounds`), then 32002 (`ConcussionProof`), then 32010 (`MixerOwner`). This is correct today. The mixer owner's lease of a quarter of a second also hides a feature that asks one frame late. Just keep the rule written once in `MixerOwner.cs:387`: "anything that asks runs before 32010".

**12. Comment bloat, including comments that are now wrong. Rewrite (comments only, no test raid needed).**
Of 16,814 lines, 1,667 are comments, and 639 of those carry dates, versions, raid IDs or quotes. `ConcussionProof`'s header runs 69 lines. Lines 36-48 still describe the crack "holding Main and Guns ... then putting the other writer's value back", which `2bcd97c` deleted. A future session that trusts that header could rebuild the old design.
*Fix:* move the history to `docs/dev/FIXES.md`, which already exists. Leave a header of 10 lines at most: what it does now, why, and what it must not break.

**13. Small things. Leave or tidy when nearby.**
- `OwnEchoes.cs:655` has two declarations on one line, left over from a merge.
- `ShockWave.cs:176` increments `ConcussionProof.Felt`, another class's counter.
- `ConcussionProof.Deliver` can call `Adrenaline.Concussion()` twice per blast. This is harmless because the call does nothing the second time.
- `MixerOwner.Frame` calls `ConcussionProof.AfterMix` just for logging.
- Hot-path allocations are already listed in the sibling notes `code-optimisation-proposals.md` (A1-A11). Nothing new turned up in the files changed this week.

## Refactor plan

Under the "one change per test raid" rule, each step below is either one raid or zero (comment and file moves need no raid if they change no behaviour).

### Before 1.0

1. **Comments:** cut the headers back and move the histories to `FIXES.md`, starting with `ConcussionProof`, `ShotOverlap`, `GunfireHearing`, `RoomTail` and `Adrenaline`. *No raid.*
2. **Delete BodySounds from main** (branch it). *1 raid: check that footsteps and the echo pool are unchanged.*
3. **`Raid` class:** one in-raid test, resets each in their own try, the ticks moved out of `DiagRunner`. *1 raid: the summary is the same and nothing stays stuck across two raids.*
4. **Voice-pool budgets (already planned):** `EchoVoices` in its own file, output and trim as arguments, per-kind reserves in place of `ignoreCap`. *1-2 raids: a grenade fight plus close gunfire.*
5. **`BorrowGunVoice` helper** for `ShotOverlap`. *1 raid: semi-auto bursts, and `OwnUnoccluded` stays at 0.*
6. **`Gear` helper and the safe `Dry()`.** *1 raid with a helmet that has a built-in headset.*
7. **Settings clean-up to about 25:** delete each settled switch's "off" path, timing diagnostics in the developer build only. *Group by area; 1 raid per area.*
8. **Remove the old ring hold** once 6.9u has run clean for a couple of raids. *1 raid.*

### After 1.0

- `GameRefs.cs` and `Synth.cs` (mechanical, any time).
- One shot pipeline: a single `FireBullet`/`Enqueue` hook that runs the features in a fixed order. Prove it with a shadow run against the old path (see `rss-rebuild-refactor-options.md`).
- An owner for the game's ring timer (`float_2`) and the snapshot blend, on the same pattern as `MixerOwner`, if either one grows a second writer.
