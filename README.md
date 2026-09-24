<p align="center"><img src="assets/logo.svg" width="128" alt="Speak Forever logo: a glowing blue infinity sign on a dark medallion"></p>

# Speak Forever

[![Build](https://github.com/samsbase/SpeakForever/actions/workflows/build.yml/badge.svg)](https://github.com/samsbase/SpeakForever/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/samsbase/SpeakForever)](https://github.com/samsbase/SpeakForever/releases/latest)

**[Website and download](https://speakforever.app/)**

Chat in **World of Warcraft: Forever** with your voice. Open chat with your controller, click the right stick, and say your message: Speak Forever copies it, you paste it into the chat box with **Ctrl+V**, and press **A** to send it. Keyboard players can use a shortcut instead, which works like Windows+H.

Speak Forever never presses a key in the game. It only puts your words on the clipboard; pasting and sending is up to you.

Speech recognition is [Whisper](https://github.com/openai/whisper), running on your own PC through [whisper.cpp](https://github.com/ggml-org/whisper.cpp). What you say never leaves your computer. There's no addon: Speak Forever works alongside WoW's own gamepad chat panel.

WoW: Forever is in beta; Speak Forever works with the beta client.

## Download and install

1. Go to the **[latest release](https://github.com/samsbase/SpeakForever/releases/latest)** and, under **Assets**, download **`SpeakForever-Setup-<version>.exe`**.
2. Run it. It installs for your Windows account only, so it doesn't need admin rights.
   - The installer isn't code-signed yet, so Windows SmartScreen may say "Windows protected your PC". Click **More info**, then **Run anyway**.
   - If your PC doesn't have Microsoft's Visual C++ runtime (14.44 or later), Windows asks once to install it.
   - Options: a desktop shortcut, and starting Speak Forever when you sign in. Both are off by default.
3. Speak Forever is now in the Start menu: search for it, or right-click it to pin it to Start or the taskbar.

**You need:** Windows 10 (version 2004) or Windows 11, 64-bit; WoW: Forever; a microphone (a headset works best). A controller for gamepad mode (Xbox, PlayStation, Nintendo Switch and most others, with no extra software), or just a keyboard. A graphics card with Vulkan makes recognition fast, but it also works on the processor.

**Updates:** Speak Forever checks for a new version when it starts and every few hours, and says so on its Home tab. **Settings › Check for updates** checks now. **Update now** downloads the new version, closes Speak Forever, installs it and opens it again, keeping your settings and voice models.

**Uninstalling:** Windows Settings › Apps › Speak Forever. It asks whether to delete your downloaded voice models and settings too; the default is to keep them.

## First run

The first time it opens, Speak Forever walks you through two steps:

1. **It downloads a voice model**: Turbo, unless you choose a different one on the **Voice model** tab, where each model shows its download size and how much memory it uses while running. Downloads come from the whisper.cpp project on Hugging Face and are checked against a SHA-256 hash.

   | Model | Download | Memory while running | Notes |
   |---|---|---|---|
   | **Turbo** (recommended) | 574 MB | about 1.0 GB | As accurate as full precision in testing |
   | Turbo 8-bit | 874 MB | about 1.2 GB | Same results as Turbo |
   | Turbo full precision | 1.6 GB | about 2.0 GB | No more accurate in testing |
   | Small | 488 MB | about 1.0 GB | For PCs without a capable graphics card; more mistakes |
   | Base | 148 MB | about 0.6 GB | For low-memory PCs; several times more mistakes |

   The first time a model runs on your graphics card, the driver prepares it, which can take 20 seconds or more. After that it loads in a few seconds.
2. **Say hello:** a quick microphone test. You can also have it start when you sign in to Windows.

Then **leave Speak Forever running** while you play. Play WoW in **Windowed (Fullscreen)** or windowed mode.

## Using it

### With a controller

1. **LB+RB+Down** opens the chat panel, as normal. Opening Chat from the radial menu (the **Menu** button) works too.
2. **Click the right stick** (RS). You hear a beep: speak for as long as you like.
3. Stop talking and it finishes after a 1.5 second pause, or **click RS again** to finish straight away. A rising two-tone beep means it heard you, and a second or two later the overlay says **Ready to paste**.
4. **Ctrl+V** pastes it into the chat box. Check it, then **A** sends it. **B** backs out. **X** changes channel, as normal.

Changed your mind? **Click RS again** while it says *Ready to paste*: that cancels it and takes it off the clipboard. Click RS once more to dictate again. Sending or closing chat (A, B, Enter or Esc) finishes with it; the text stays on the clipboard until the next dictation replaces it. Pressing A or B while you're speaking discards it.

**Pasting from the controller:** Speak Forever deliberately doesn't press Ctrl+V for you. To paste without reaching for the keyboard, map a spare button to Ctrl+V in your controller's own software or in Steam Input, or use a controller that can send keyboard keys itself.

Speak Forever follows the chat panel by watching the same buttons WoW does. RS only dictates while the chat box is open. Speak Forever doesn't look at the game or which window is in front: it just copies what you said when you press the button.

**Changing the buttons:** on the **Controls** tab (with a controller connected), click **Change**, hold any modifier buttons, press the last button, then let go. Speak Forever can't change WoW's own bindings, so if you rebind "open chat" in the game, set the same combo here.

### With a keyboard

On the **Controls** tab, click **Change** next to *Dictate (keyboard)* and press any key or combination, such as Ctrl+Shift+Space or F8. While Speak Forever is active, Windows sends that key to Speak Forever instead of the program you're in, so pick one you don't need elsewhere, or pause Speak Forever when you do.

Like Windows+H, the shortcut works in **any program**: press it and speak, then paste with Ctrl+V wherever you like. In WoW, open chat with Enter, paste, and press Enter to send. Press the shortcut again to finish early, or, while it says *Ready to paste*, to cancel.

### The overlay

While you speak, a small **Listening** pill shows at the top of the screen, over the game, with the button that finishes early. It says **Transcribing…**, then **Ready to paste** with the button that cancels, and stays until you send the message or close chat. If a message is too long for the chat box, it says **Too long for chat** instead: only the start was copied.

It never takes focus from the game. It shows over WoW in **Windowed (Fullscreen)** or windowed mode (nothing can draw over exclusive fullscreen). Turn it off on the **Settings** tab.

### Sound cues

A beep means it's listening. A rising two-tone means it heard you and is transcribing. Nothing else makes a sound: the dictate button is often bound to something else in the game too, so a press that doesn't start a dictation stays quiet, and the activity log (**Show activity** on the Home tab) says why.

### The window

The Home tab's status says what Speak Forever is doing and what to press next: **Ready**, **Listening…**, **Transcribing**, **Ready to paste**, or what it needs from you. **Active / Paused** beside it turns Speak Forever on and off. The logo in the title bar ripples while it's listening and quickens while transcribing.

| Tab | What's there |
|---|---|
| **Home** | The status, anything that needs your attention, the last message (with timings and **Copy**), the model, microphone (**Test**) and shortcut at a glance, and **Show activity** for the log |
| **Voice model** | The model in use and **Test microphone**; download, switch (**Use**) or delete others (**Show advanced models** for the two larger Turbos); how long a pause ends a dictation |
| **Controls** | The controller buttons (open chat, dictate) and the keyboard shortcut |
| **Settings** | The microphone; the in-game overlay; version, **Check for updates**, and whether to check automatically; starting when you sign in to Windows |

The window opens tall enough that no tab needs scrolling, as far as the screen allows.

## Privacy

Your voice is recognised on your PC and never sent anywhere. Speak Forever connects to the internet only to download the voice models you choose (from Hugging Face) and to check GitHub for new versions. Turn the update check off on the **Settings** tab.

## Limitations

- **Chat opened with the keyboard** (Enter) isn't seen by the controller tracking, so RS won't dictate into it. Open chat with the controller, or use the keyboard shortcut.
- **Pasting needs Ctrl+V**: a keyboard, or a button mapped to it (see *Pasting from the controller*).
- **Your clipboard is used.** Each dictation replaces what was on it. It's kept out of Windows clipboard history (Win+V) and cloud clipboard sync.
- **WoW's chat box holds 255 characters**, about 40–50 words. Only what fits is copied, cut at the last whole word; the overlay and the Home tab say what was left out.
- **Dictation ends on a pause**, detected by volume. Loud game audio through speakers can keep it listening; a headset avoids that.
- **Terms of service:** Speak Forever doesn't read the game or send it any input: it puts your own dictated words on the clipboard, and you paste and send them. Blizzard hasn't approved it, though, and its terms forbid third-party software it hasn't authorised, so use it at your own risk.

## Troubleshooting

- **Nothing to paste:** check the Home tab's status and notices. Speak Forever needs a voice model, and RS only dictates with chat open. The activity log says why each attempt was refused.
- **"Chat isn't open":** open chat with LB+RB+Down (or the radial menu), not Enter, or use the keyboard shortcut.
- **It keeps listening:** raise the pause on the Voice model tab, or `SpeechThresholdDb` in the settings file, if game sound from speakers is being heard.
- **No overlay:** it's on the Settings tab, and it can't show over exclusive fullscreen: set WoW's display mode to Windowed (Fullscreen).
- The log is also written to `%LOCALAPPDATA%\SpeakForever\speakforever.log`.

## Advanced

### Settings file (`%LOCALAPPDATA%\SpeakForever\speakforever.json`)

Most settings are in the app. The file is plain JSON, checked at launch: a misspelt setting, a value out of range or a comment is reported rather than silently ignored. (Comments aren't allowed because the app rewrites the file when a setting changes, which would lose them.)

| Key | Default | Meaning |
|---|---|---|
| `ControllerSlot` | `-1` | With several controllers connected, which to use: `0` for the first, up to `3`. `-1` uses whichever is found first. |
| `OpenChatChord` | `LB+RB+DOWN` | WoW's open-chat combo. Set on the Controls tab. Buttons are named by position, Xbox-style: `A` is the bottom face button on every controller (Cross on PlayStation, B on Switch), and the app shows your controller's own icons. |
| `DictateChord` | `RS` | Starts a dictation while chat is open, and cancels one that's ready to paste. Also set in the app. |
| `RadialMenuChord` | `START` | WoW's radial menu button, so chat opened from the radial is seen too |
| `KeyboardShortcut` | `null` (off) | e.g. `Ctrl+Shift+Space`. Set in the app. |
| `SendChord`, `BackChord` | `A`, `B` | The chat panel's Send and Back |
| `MenuChords` | `X`, `Y` | The chat panel's Chat Channels and Tab Settings menus |
| `CheckForUpdates` | `true` | Check GitHub for new versions at launch and every 6 hours. Set on the Settings tab. |
| `Prompt` | WoW: Forever names | Words Whisper should expect: dungeons and raids (the new ones too), zones, and abilities and slang it would otherwise mishear. Without it, it hears "Iron Fudge" and "Dead Minds". Whisper reads only about the last 224 tokens (roughly 150 words), so to add your guild's or friends' names, remove something first, and put what matters most at the end. |
| `CorrectNames` | `true` | Puts WoW: Forever names back where Whisper wrote something that sounds like one but isn't a real word ("Stratham" becomes Stratholme, "Chandra Lass" becomes Shen'dralas). Real words and chat slang are never changed, and neither are names that sound like a real word, such as Innervate. The names are in `app/Core/Speech/Names/wow-names.txt`. |
| `ModelPath` | set by **Use** | The voice model in use |
| `Language` | `en` | Whisper language code, or `auto` |
| `UseGpu` | `true` | Vulkan graphics card; `false` for the processor only. Takes effect after a restart. |
| `BeamSize` | `1` | Decoding width. 1 (greedy) matched 5-beam accuracy on Turbo in testing, and is faster and lighter. |
| `MicDevice` | `-1` | `-1` is the Windows default microphone. Pick another on the Settings tab; `SpeakForeverCli --test-mic` lists them. |
| `SilenceMs` | `1500` | The pause that ends a dictation. Set by the slider. |
| `NoSpeechTimeoutSeconds` | `6` | Gives up if you don't start talking |
| `MaxSeconds` | `120` | Only a guard against a microphone that never goes quiet |
| `SpeechThresholdDb` | `10` | How far above background noise counts as speech |
| `DelayMs` | `150` | Pause after the dictate button before recording, so the beep isn't recorded |
| `Sounds` | `true` | The sound cues |
| `ShowOverlay` | `true` | The in-game overlay. Set on the Settings tab. |

Settings earlier versions had (`RedoChord`, `GameFolder`, `ProcessNames`) are dropped from an older file when it loads.

### Command line (`SpeakForeverCli.exe`, installed beside the app)

| Command | Does |
|---|---|
| *(none)* | Headless mode: the app without a window |
| `--probe` | Logs controller presses and chat tracking; records and copies nothing |
| `--test-mic` | One dictation from the microphone, printed with timings |
| `--transcribe file.wav` | Transcribes a file |
| `--benchmark [filter]` | Memory, speed and accuracy for every installed model (below) |
| `--model path …` | Uses a different model for any of the above |

Only one copy, app or command line, watches the controller at a time; otherwise both would dictate every message.

### Benchmark

`SpeakForeverCli --benchmark` measures each installed model, each in its own process: memory (RAM, video memory, and GPU memory spilled to RAM), speed, and word error rate on clean and noisy audio. The test clips are `.wav` files with a `.txt` of what was said, in `%LOCALAPPDATA%\SpeakForever\benchmark\`; `powershell -File tools\make-benchmark-clips.ps1` generates 24 with the Windows voices. Results with WoW running (memory in MB, held between dictations):

| Model | Decoding | Idle memory | Peak | Errors, clean | Errors, noisy | Per clip |
|---|---|---|---|---|---|---|
| turbo | beam 5 | 2,055 | 2,700 | 0.8% | 1.9% | 1,168 ms |
| turbo q8_0 | greedy | 1,215 | 1,174 | 0.8% | 1.5% | 785 ms |
| **turbo q5_0** | **greedy** | **932** | **884** | **0.8%** | **1.1%** | **673 ms** |
| small | beam 5 | 979 | 1,835 | 0.4% | 3.4% | 921 ms |
| base | greedy | 599 | – | 1.9% | 8.0% | 190 ms |

## For developers

### Building

```
.\build.ps1              # tests, then the self-contained app and CLI in publish\
.\build.ps1 -Installer   # also dist\SpeakForever-Setup-<version>.exe
```

Needs the .NET 10 SDK (pinned in `global.json`) and, for the installer, [Inno Setup](https://jrsoftware.org/isinfo.php) 6.7 or later. The Visual C++ redistributable isn't kept in git: `build.ps1 -Installer` downloads it from Microsoft the first time and checks its signature. `SpeakForever.slnx` opens everything in Visual Studio or Rider.

- **Tests:** `dotnet test --project tests\SpeakForever.Core.Tests` (xUnit v3): end-of-speech detection, chat-panel and radial-menu tracking, bindings, shortcuts, the settings file, update checks and the model list. They use a temporary data folder (`SPEAKFOREVER_DATA`), never your real settings.
- **Linting:** every build runs the .NET analyzers (latest-recommended) and the style rules in `.editorconfig`; warnings fail the build. Package versions are in `Directory.Packages.props`; `nuget.config` pins the feed to nuget.org.

### Releasing

[`.github/workflows/build.yml`](.github/workflows/build.yml) builds and tests every pull request. Every merge to `main` also builds the installer, keeps it with the workflow run, and, **if the version is new**, publishes it as a GitHub release tagged `v<version>`. To release:

1. Bump `<Version>` in `Directory.Build.props`.
2. Merge to `main`. The release appears with the installer attached, and installed copies offer the update within a few hours.

The app checks for updates in the repository it was built from: the workflow passes `github.repository`, and local builds use `<GitHubRepository>` in `Directory.Build.props`. If the repository moves, change that and the links at the top of this README.

### Layout

Namespaces follow folders.

| Folder | What's there |
|---|---|
| `app\Core` | The engine library; `Engine.cs` ties it together |
| `app\Core\Configuration` | The settings file (`Config`) and data folder (`AppPaths`) |
| `app\Core\Input` | Reading controllers (SDL3, with SDL_GameControllerDB), chords, the chat panel and radial menu trackers, the keyboard shortcut |
| `app\Core\Speech` | Recording, end-of-speech detection, Whisper, the model catalog and downloads |
| `app\Core\Dictation` | One dictation from button to copied text, and the sound cues |
| `app\Core\Updates` | Checking GitHub for a newer release |
| `app\Core\Presentation` | The model list's row view model (no UI types, so it's tested without WinUI) |
| `app\Core\Interop`, `app\Core\Logging` | The clipboard; the log |
| `app\Gui` | The WinUI 3 app: `Views\MainWindow` (one partial file per tab), `Controls`, `Models` |
| `app\Cli` | The command line; `Commands` has the benchmark and setup checks |
| `tests\SpeakForever.Core.Tests` | The engine's tests |
| `installer` | Inno Setup script and wizard art |
| `tools` | Benchmark clips; `IconGen` draws the app icon and installer art (`IconGen icon <out.ico>`, `IconGen wizard installer\art app\Gui\Assets\Fonts\Cinzel.ttf`) |
| `assets` | Logo SVGs |
| `docs` | The website on GitHub Pages: one static page. Its screenshots are of the real app. |

Speak Forever was called Voice Forever (and before that ForeverVoice); data in those folders under `%LOCALAPPDATA%` moves across on first launch, and the installer upgrades a Voice Forever install in place.

## Licence

Speak Forever is free and open source under the [MIT License](LICENSE).

The app's look is original artwork styled after WoW Classic's frames; headings use Cinzel (SIL Open Font License, bundled with its licence). Third-party licences are in `THIRD-PARTY-NOTICES.txt`, which ships with the app. World of Warcraft is a trademark of Blizzard Entertainment, Inc.; Speak Forever is not affiliated with or endorsed by Blizzard.
