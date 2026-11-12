# Test route

One fixed raid, run the same way every time, so a change can be judged against a reference instead of from memory.

**Rule:** one change per test raid. After each raid, compare it with the reference:

```
python tools/compare_raids.py <reference raid folder> <new raid folder>
```

Anything that sounds worse gets fixed or rolled back before the next change.

## Setup

- **Map:** Ground Zero, daytime (the author's choice: streets, office buildings, an underground car park, a short raid). Pick rain in the raid settings if you can; if not, skip item 6.
- **Gear:** one suppressed rifle (the same one every time) with its suppressor, one grenade or more, and a headset in your backpack.
- **Settings:** your normal ones. Turn on **Bug report - logs**.
- **Marks:** press **F10** at each numbered item, in order. Mark 1 = item 1 and so on, so the logs line up with the checklist. If you skip an item, press F10 anyway.

## The route (about 10 minutes)

Start without the headset on.

| # | Where | What to do | Listen for |
|---|---|---|---|
| 1 | A street, away from the buildings | 3 shots with the suppressor, take it off, 3 more | Outdoor shot with the street's echoes, no room ring |
| 2 | An office building's front door | 1 shot just outside, step in, 1 shot just inside | The shot changing smoothly from outdoor to indoor at the door, no jump |
| 3 | A small office room | 3 shots, then turn around and fire 1 more | A short, tight ring; echoes off near walls |
| 4 | The underground car park | 3 shots | A long concrete ring, later echoes |
| 5 | A stairwell | 2 shots | A hard, ringing stairwell |
| 6 | Rain: deep inside a building, by a window, then under a shop awning or metal roof | Stand 10 s at each | Rain coming *from* the window; a metal roof louder; not doubled |
| 7 | When a fight starts in another room or building | Stay put and listen | Shots from the doorway they come through, echoes, not cut off |
| 8 | A bot or fight behind a wall or closed door | Listen | Muffled by the wall's material; a thump you feel |
| 9 | Grenade ~5 m away round a corner (yours or a bot's), **no headset** | — | Crack, then muffled but still heard, ringing, then heartbeat and breathing settling within ~15 s |
| 10 | Put the headset on. Grenade ~5 m round a corner again | — | Shorter crack, only a small dip in hearing, no ringing |
| 11 | Grenade ~20 m away, headset on | — | A distant boom from the right direction, maybe echoes, no concussion |
| 12 | Headset on, same spot as item 3 | 3 suppressed shots | Your gun clear, not muffled or much quieter than without the headset |

## After the raid

Write a line per item: **fine**, or what was wrong. Send it with the raid folder name.

## Reference raids

| Date | Build | Raid folder | Notes |
|---|---|---|---|
| | | | The first full run of this route becomes the reference |
