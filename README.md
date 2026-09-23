<p align="center"><img src="assets/logo.svg" width="128" alt="Speak Forever logo: a glowing blue infinity sign on a dark medallion"></p>

# Speak Forever

[![Build](https://github.com/samsbase/SpeakForever/actions/workflows/build.yml/badge.svg)](https://github.com/samsbase/SpeakForever/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/samsbase/SpeakForever)](https://github.com/samsbase/SpeakForever/releases/latest)

**[Website and download](https://samsbase.github.io/SpeakForever/)**

Chat in **World of Warcraft: Forever** with your voice. Open chat with your controller, click the right stick, and say your message: it's typed into the chat box, and you press **A** to send it. Keyboard players can use a shortcut instead, which works like Windows+H.

Speech recognition is [Whisper](https://github.com/openai/whisper), running on your own PC through [whisper.cpp](https://github.com/ggml-org/whisper.cpp). What you say never leaves your computer. There's no addon: Speak Forever works alongside WoW's own gamepad chat panel.

WoW: Forever is in beta; Speak Forever works with the beta client.

## Download and install

1. Go to the **[latest release](https://github.com/samsbase/SpeakForever/releases/latest)** and, under **Assets**, download **`SpeakForever-Setup-<version>.exe`**.
2. Run it. It installs for your Windows account only, so it doesn't need admin rights.
   - The installer isn't code-signed yet, so Windows SmartScreen may say "Windows protected your PC". Click **More info**, then **Run anyway**.
   - If your PC doesn't have Microsoft's Visual C++ runtime (14.44 or later), Windows asks once to install it.
   - Options: a desktop shortcut, and starting Speak Forever when you sign in. Both are off by default.
3. Speak Forever is now in the Start menu: search for it, or right-click it to pin it to Start or the taskbar.

**You need:** Windows 10 (version 2004) or Windows 11, 64-bit; WoW: Forever; a microphone (a headset works best). A controller for gamepad mode, or just a keyboard. A graphics card with Vulkan makes recognition fast, but it also works on the processor.

**Updates:** Speak Forever checks for a new version when it starts and every few hours, and says so on its Dictation tab. **Settings › Check for updates** checks now. Download the new installer and run it: it updates in place, keeping your settings and speech models.

**Uninstalling:** Windows Settings › Apps › Speak Forever. It asks whether to delete your downloaded speech models and settings too; the default is to keep them.

## First run

1. **Speak Forever finds WoW: Forever** where Battle.net installed it. If it can't, it asks: choose the folder with the game in it (in the beta, `World of Warcraft\_classic_beta_`; choosing the `World of Warcraft` folder works too). You can change it any time on the **Settings** tab. Speak Forever only ever types into the game it finds there.
2. **Download a speech model** on the **Speech model** tab. Each model shows its download size and how much memory it uses while running. Downloads come from the whisper.cpp project on Hugging Face and are checked against a SHA-256 hash.

   | Model | Download | Memory while running | Notes |
   |---|---|---|---|
   | **Turbo** (recommended) | 574 MB | about 1.0 GB | As accurate as full precision in testing |
   | Turbo 8-bit | 874 MB | about 1.2 GB | Same results as Turbo |
   | Turbo full precision | 1.6 GB | about 2.0 GB | No more accurate in testing |
   | Small | 488 MB | about 1.0 GB | For PCs without a capable graphics card; more mistakes |
   | Base | 148 MB | about 0.6 GB | For low-memory PCs; several times more mistakes |

   The first time a model runs on your graphics card, the driver prepares it, which can take 20 seconds or more. After that it loads in a few seconds.
3. **Leave Speak Forever running** while you play. Play WoW in **Windowed (Fullscreen)** or windowed mode.

## Using it

### With a controller

1. **LB+RB+Down** opens the chat panel, as normal. Opening Chat from the radial menu (the **Menu** button) works too.
2. **Click the right stick** (RS). You hear a beep: speak for as long as you like.
3. Stop talking and it finishes after a 1.5 second pause, or **click RS again** to finish straight away. A rising two-tone beep means it heard you, and your words appear in the chat box a second or two later.
4. **A** sends it. **B** backs out. **X** changes channel, as normal.

Click RS again to add more to the same message. Pressing A or B while you're speaking discards it.

Speak Forever follows the chat panel by watching the same buttons WoW does. RS only dictates while the chat box is open, and it never types into anything but the game, so stray words can't turn into key presses in game.

**Changing the buttons:** on the **Buttons & shortcuts** tab, click **Change**, hold any modifier buttons, press the last button, then let go. Speak Forever can't change WoW's own bindings, so if you rebind "open chat" in the game, set the same combo here.

### With a keyboard

On the **Buttons & shortcuts** tab, click **Change** next to *Dictate (keyboard)* and press a combination such as Ctrl+Shift+Space. It must include Ctrl, Alt, Shift or Windows, or be an F key, so it doesn't take a key you type with.

Like Windows+H, the shortcut types into **whatever has focus**: in WoW, open chat with Enter first, then press the shortcut and speak. Press it again to finish early.

### Sound cues

A beep means it's listening. A rising two-tone means it heard you and is transcribing. A short blip means the dictation was discarded. A low buzz means it couldn't (chat not open, nothing heard, or an error).

### The window

The status line shows whether your controller is connected and whether chat is open; **Active / Paused** turns Speak Forever on and off. The logo in the title bar ripples while it's listening and quickens while transcribing.

| Tab | What's there |
|---|---|
| **Dictation** | Anything that needs your attention, what it last heard (with timings), and the activity log |
| **Speech model** | Download, switch (**Use**) or delete models; **Test microphone**; how long a pause ends a dictation |
| **Buttons & shortcuts** | The controller buttons and the keyboard shortcut |
| **Settings** | Where WoW: Forever is installed (**Find it** or **Choose folder**); version, **Check for updates**, and whether to check automatically |

## Privacy

Your voice is recognised on your PC and never sent anywhere. Speak Forever connects to the internet only to download the speech models you choose (from Hugging Face) and to check GitHub for new versions. Turn the update check off on the **Settings** tab.

## Limitations

- **Chat opened with the keyboard** (Enter) isn't seen by the controller tracking, so RS won't dictate into it. Open chat with the controller, or use the keyboard shortcut.
- **The keyboard shortcut types into whatever has focus.** In WoW, open chat first, or the words become key presses.
- **WoW's chat box holds 255 characters**, about 40–50 words. Longer dictations are cut at the last whole word, and the activity log shows what was dropped.
- **Dictation ends on a pause**, detected by volume. Loud game audio through speakers can keep it listening; a headset avoids that.
- **Terms of service:** Speak Forever types only your own dictated words into one game client, as dictation software does. Blizzard's enforced policy targets input broadcasting across several clients, but Blizzard hasn't explicitly approved this.

## Troubleshooting

- **Nothing is typed:** check the Dictation tab's notices. Speak Forever needs a speech model, and needs to know where WoW: Forever is (Settings tab). The activity log says why each attempt was refused.
- **"Chat isn't open":** open chat with LB+RB+Down (or the radial menu), not Enter, or use the keyboard shortcut.
- **It keeps listening:** raise the pause on the Speech model tab, or `SpeechThresholdDb` in the settings file, if game sound from speakers is being heard.
- The log is also written to `%LOCALAPPDATA%\SpeakForever\speakforever.log`.

## Advanced

### Settings file (`%LOCALAPPDATA%\SpeakForever\speakforever.json`)

Most settings are in the app. The file is plain JSON, checked at launch: a misspelt setting, a value out of range or a comment is reported rather than silently ignored. (Comments aren't allowed because the app rewrites the file when a setting changes, which would lose them.)

| Key | Default | Meaning |
|---|---|---|
| `GameFolder` | found on first run | The WoW: Forever folder. Speak Forever only types into a program running from here. Set on the Settings tab. |
| `ProcessNames` | `["WowB"]` | Used only while `GameFolder` isn't set: game process names without `.exe`. |
| `OpenChatChord` | `LB+RB+DOWN` | WoW's open-chat combo. Set on the Buttons & shortcuts tab. |
| `DictateChord` | `RS` | Starts a dictation while chat is open. Also set in the app. |
| `RadialMenuChord` | `START` | WoW's radial menu button, so chat opened from the radial is seen too |
| `KeyboardShortcut` | `null` (off) | e.g. `Ctrl+Shift+Space`. Set in the app. |
| `SendChord`, `BackChord` | `A`, `B` | The chat panel's Send and Back |
| `MenuChords` | `X`, `Y` | The chat panel's Chat Channels and Tab Settings menus |
| `CheckForUpdates` | `true` | Check GitHub for new versions at launch and every 6 hours. Set on the Settings tab. |
| `Prompt` | WoW place names | Words Whisper should expect. Without it, it hears "Iron Fudge" and "Dead Minds". Add your guild's and friends' names. |
| `ModelPath` | set by **Use** | The speech model in use |
| `Language` | `en` | Whisper language code, or `auto` |
| `UseGpu` | `true` | Vulkan graphics card; `false` for the processor only. Takes effect after a restart. |
| `BeamSize` | `1` | Decoding width. 1 (greedy) matched 5-beam accuracy on Turbo in testing, and is faster and lighter. |
| `MicDevice` | `-1` | `-1` is the Windows default microphone. `SpeakForeverCli --test-mic` lists the others. |
| `SilenceMs` | `1500` | The pause that ends a dictation. Set by the slider. |
| `NoSpeechTimeoutSeconds` | `6` | Gives up if you don't start talking |
| `MaxSeconds` | `120` | Only a guard against a microphone that never goes quiet |
| `SpeechThresholdDb` | `10` | How far above background noise counts as speech |
| `DelayMs` | `150` | Pause after the dictate button before recording, so the beep isn't recorded |
| `Sounds` | `true` | The sound cues |

### Command line (`SpeakForeverCli.exe`, installed beside the app)

| Command | Does |
|---|---|
| *(none)* | Headless mode: the app without a window |
| `--probe` | Logs controller presses and chat tracking; records and types nothing |
| `--test-mic` | One dictation from the microphone, printed with timings |
| `--test-type [text]` | Types text into WoW's chat box after a 5 second countdown |
| `--transcribe file.wav` | Transcribes a file |
| `--benchmark [filter]` | Memory, speed and accuracy for every installed model (below) |
| `--model path …` | Uses a different model for any of the above |

Only one copy, app or command line, watches the controller at a time; otherwise both would type every message.

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

- **Tests:** `dotnet test --project tests\SpeakForever.Core.Tests` (xUnit v3): end-of-speech detection, chat-panel and radial-menu tracking, bindings, shortcuts, the settings file, finding the game, update checks and the model list. They use a temporary data folder (`SPEAKFOREVER_DATA`), never your real settings.
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
| `app\Core\Game` | Finding WoW: Forever in a Battle.net install |
| `app\Core\Input` | XInput, chords, the chat panel and radial menu trackers, the keyboard shortcut |
| `app\Core\Speech` | Recording, end-of-speech detection, Whisper, the model catalog and downloads |
| `app\Core\Dictation` | One dictation from button to typed text, and the sound cues |
| `app\Core\Updates` | Checking GitHub for a newer release |
| `app\Core\Presentation` | The model list's row view model (no UI types, so it's tested without WinUI) |
| `app\Core\Interop`, `app\Core\Logging` | Typing via SendInput; the log |
| `app\Gui` | The WinUI 3 app: `Views\MainWindow` (one partial file per tab), `Controls`, `Models` |
| `app\Cli` | The command line; `Commands` has the benchmark and setup checks |
| `tests\SpeakForever.Core.Tests` | The engine's tests |
| `installer` | Inno Setup script and wizard art |
| `tools` | Benchmark clips; `IconGen` draws the app icon and installer art (`IconGen icon <out.ico>`, `IconGen wizard installer\art app\Gui\Assets\Fonts\Cinzel.ttf`) |
| `assets` | Logo SVGs |
| `docs` | The website on GitHub Pages: one static page. Its screenshots are of the real app. |

Speak Forever was called Voice Forever (and before that ForeverVoice); data in those folders under `%LOCALAPPDATA%` moves across on first launch, and the installer upgrades a Voice Forever install in place.

The app's look is original artwork styled after WoW Classic's frames; headings use Cinzel (SIL Open Font License, bundled with its licence). Third-party licences are in `THIRD-PARTY-NOTICES.txt`, which ships with the app. World of Warcraft is a trademark of Blizzard Entertainment, Inc.; Speak Forever is not affiliated with or endorsed by Blizzard.
