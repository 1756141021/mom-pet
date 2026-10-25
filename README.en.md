# Mom Pet

[繁體中文](README.md) | [简体中文](README.zh-Hans.md) | **English** | [日本語](README.ja.md)

A little desktop pet for Windows that follows your mouse around~! Her face changes with the shape of your cursor. Click her and she hops; keep clicking and she gets mad, and if you overdo it she gets dizzy! Leave the mouse alone and she drifts off into her own little world. She's see-through to clicks, so she never gets in your way~

<p align="center"><img src="docs/media/follow.gif" width="720" alt="Following the cursor"></p>

> Three characters come built in: DeepSeek-chan, the big blue fish who lives on plain rice; Claude-chan, who holds an orange spark while she thinks; and my own OC, Hein! Switch between them in the settings~

<p align="center"><img src="docs/media/characters.png" alt="The three characters"></p>

## Download

Grab the zip from [Releases](../../releases), unzip it and run `MomPet.exe`, that's it! Works on Windows 10 / 11 with nothing else to install. If Windows says "Windows protected your PC" the first time, click "More info" and then "Run anyway"~

## What she does

**Follows your cursor**: she leans the way she's going, squishes flatter the faster she runs, and wobbles a bit when she stops. Fling the mouse fast enough and she floats~

<p align="center"><img src="docs/media/float.gif" width="720" alt="Floating"></p>

**Getting clicked**: one click and she hops up with stars and hearts popping out. Keep clicking the same spot (less than 0.8 s between clicks) and her mood goes downhill:

| Clicks | Face |
| --- | --- |
| 1 | happy `happy` |
| 3 | surprised `surprised` |
| 5 | flustered `flustered` |
| 8 | annoyed `annoyed` |
| 11 | angry `angry`, and shaking |
| 16 | dizzy `dizzy`, spinning around |
| stop after 8 or more | sulks `sulk` for a moment |

When you click links and buttons she only gets as far as surprised, so everyday browsing won't make her cross~

<p align="center"><img src="docs/media/clicks.gif" width="480" alt="Click streak"></p>

**Idling**: leave the mouse still for 8 seconds and she does her own thing: DeepSeek-chan tucks into a big bowl of rice, Claude-chan holds her orange spark and thinks, and Hein dozes off with little z's floating up. Move the mouse and she jumps!

<p align="center"><img src="docs/media/idle.gif" width="420" alt="Idling and jumping awake"></p>

**Changing faces**: each of the 16 Windows cursors has its own pose. With the spinning cursor she's busy, with the hand she points at things, and with the "unavailable" sign she crosses her arms into an X~

<p align="center"><img src="docs/media/poses.gif" width="380" alt="Poses for each cursor"></p>

All 26 poses (the names are the file names used in image packs):

<p align="center"><img src="docs/media/poses.png" alt="All poses"></p>

## Settings

Click the yellow cat in the system tray to open the settings! You can switch characters, pause her, start with Windows, hide her from screenshots and recordings, make her disappear when the mouse sits still for a while, and resize her (50%–200%). The interface comes in Traditional Chinese, Simplified Chinese, English and Japanese. She also hides by herself while you play a fullscreen game or give a presentation~

## Use your own character

Get some PNGs with transparent backgrounds and swap them in from the settings! Animated GIFs work too~ A single `default.png` is enough; any pose without its own image uses it. A whole set can be packed into a zip and shared, and animations can go in frame by frame as `idle_1.png`, `idle_2.png`... Want to draw a full set with NovelAI or ChatGPT? See the [character art guide](docs/角色圖片創作指南.md) (in Chinese).

## Make your own click effects

A click effect is a text file of formulas, so you can make your own without touching the code~ Two come built in:

<p align="center"><img src="docs/media/effects.gif" width="520" alt="Built-in click effects"></p>

How to write one: [click effect guide](docs/點擊特效說明.md) (in Chinese).

## Building

All you need is the .NET Framework 4 that comes with Windows, no Visual Studio. Run `build.cmd` in the folder to get `MomPet.exe`; keep it next to the `assets`, `characters`, `lang` and `effects` folders.

To add another built-in character, make a folder in `characters` with her images and a `character.txt` for her name, credit, size and animation frame times. The format is described at the top of `src\Characters.cs`~

`--settings` opens the settings page right away, and `--stats` writes the frame rate to `stats.log` next to the exe every 5 seconds. Settings live in `HKCU\Software\MomPet`; your own images and effects in `%LOCALAPPDATA%\MomPet\`.

## License

The code is [MIT](LICENSE), use it however you like~

The images (in `characters`, `assets` and `docs/media`) are [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/): give credit, no commercial use, and share anything you make from them under the same license!

DeepSeek-chan is a community fan character, not an official DeepSeek design. She is based on "溟月" by 上善无形, and the maid whale girl was drawn by ZipZipPipe; see [characters/deepseek/LICENSE.txt](characters/deepseek/LICENSE.txt). Claude-chan is a fan character too, not an official Anthropic one, designed by ZipZipPipe; see [characters/claude/LICENSE.txt](characters/claude/LICENSE.txt).
