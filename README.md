<p align="center"><img src="assets/logo.svg" width="128" alt="Voice Forever logo: a glowing blue infinity sign on a dark medallion"></p>

# Voice Forever

Voice-to-chat for WoW: Forever in gamepad mode. Open chat, click the right stick, and say your message. It's typed into the chat box, and you press **A** to send it. Keyboard players can set a shortcut instead, which works like Windows+H.

Speech recognition is [Whisper](https://github.com/openai/whisper), running locally on your GPU through [whisper.cpp](https://github.com/ggml-org/whisper.cpp). Nothing is sent anywhere. There's no addon: the app works with WoW's own gamepad chat panel.

## Using it

1. **LB+RB+Down** opens the chat panel, as normal. Opening it from the radial menu works too (**Menu** button, then Chat on the Main Menu page).
2. **Click the right stick** (RS). You hear a beep: speak for as long as you like.
3. Stop talking and it finishes after a 1.5 s pause. Or **click RS again** to finish straight away. A rising two-tone beep means it heard you, and the text appears in the chat box a second or two later.
4. **A** sends it. **B** backs out. **X** changes channel, as normal.

Press RS again to add more to the same message. Pressing A or B mid-dictation discards the dictation.

**Both buttons can be changed** in the app's **Buttons & shortcuts** tab. Click **Change**, hold any modifier buttons, press the last button, then let go. The app can't change WoW's own bindings, so if you rebind "open chat" in the game, set the same combo here. Combos that clash with the chat panel's own buttons (A, B, X, Y) are refused.

The app works out whether chat is open by watching the same buttons WoW responds to. RS only dictates while the chat panel's text box is open, and it never types into anything but the game, so stray text can't turn into key presses in game. Inside the chat panel, RS normally just toggles tooltips.

The radial menu is tracked the way WoW draws it: it always opens on the Main Menu page, LB/RB cycle pages (wrapping round), and a selection happens when the right stick springs back to centre. If you've moved the radial menu to another button in WoW, set `RadialMenuChord` in the config to match.

### Keyboard shortcut

Off by default. In **Buttons & shortcuts**, click **Change** next to *Dictate (keyboard)* and press the combination, for example Ctrl+Shift+Space (**Turn off** removes it). It must include Ctrl, Alt, Shift or Windows, or be an F key, so it doesn't steal a key you type with. If another program already owns it, the app says so in red; pick another.

Like Windows+H, the shortcut dictates into **whatever text box has focus**, in any program: open chat with Enter, press the shortcut, speak. Press it again to finish early.

Sound cues: a beep means it's listening. A rising two-tone means it heard you and is transcribing. A short blip means the dictation was discarded. A low buzz means it wasn't possible (chat not open, nothing heard, or an error).

## Install

1. Run **`VoiceForever-Setup-1.0.0.exe`**. It installs for your Windows account only, so it needs no admin rights. The one exception is Microsoft's Visual C++ runtime (14.44 or later): if your PC doesn't already have it, Windows asks once to install it. Windows 10 (2004) or later, 64-bit.
   - The installer isn't code-signed yet, so SmartScreen shows "Windows protected your PC". Click **More info**, then **Run anyway**.
   - Options: a desktop shortcut, and starting with Windows. Both are off by default.
   - Afterwards, Voice Forever is in the Start menu: search for it, or right-click it to pin it to Start or the taskbar. Win+R `VoiceForever` also starts it.
2. **Pick a speech model** in the app's **Speech model** tab and click **Download**. Each model shows its download size and how much memory it holds while running. Downloads are checked against a SHA-256 hash before use, and can be cancelled.

   | Model | Download | Memory while running | Notes |
   |---|---|---|---|
   | **Turbo** (recommended) | 574 MB | about 1.0 GB | As accurate as full precision in the benchmark |
   | Turbo 8-bit | 874 MB | about 1.2 GB | Same transcripts as full precision |
   | Turbo full precision | 1.6 GB | about 2.0 GB | No more accurate in testing |
   | Small | 488 MB | about 1.0 GB | Less accurate, and slower |
   | Base | 148 MB | about 0.6 GB | For low-memory PCs; makes several times more mistakes |

   Models are saved in `%LOCALAPPDATA%\VoiceForever\models\`. Any other `ggml-*.bin` from [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) dropped in there also appears in the list.

   WoW fills most of a 16 GB card's video memory, so the model usually runs from system RAM instead. The app uses flash attention to stay fast when that happens; without it, the same message took 4.6 s.
3. **Leave the app open** while you play.

**Uninstalling** (Settings › Apps) asks whether to delete your downloaded models and settings as well. The default is to keep them.

WoW must be in **Windowed (Fullscreen)** or windowed mode, and in the foreground.

## The app

Styled after WoW Classic's frames, in the arcane blue of the logo. The logo in the title bar is still while you play, which costs nothing. It ripples while it's listening and quickens while transcribing, so it doubles as a status light. The headings use Cinzel, a free font under the SIL Open Font License (bundled with its licence); WoW's own font is licensed and can't be shipped. All artwork is original.


The status line and the **Active / Paused** switch sit above three tabs: **Dictation** (Last heard and Activity), **Speech model**, and **Buttons & shortcuts**.

- **The status line** shows whether the controller is connected and whether chat is open.
- **Active / Paused** turns controller watching and the keyboard shortcut on and off.
- **Speech model** lists the models with their download size and memory use: **Download**, **Use** (switches live; the choice is remembered), or the bin icon to delete one.
- **Stop listening after a pause of** sets how long a pause ends a dictation (0.5–4 s). Raise it if you pause mid-thought; RS always finishes straight away.
- **Test microphone** runs one dictation without the game, handy for comparing models on your own voice.
- **Buttons & shortcuts** shows the open-chat combo, the dictate button and the keyboard shortcut, with **Change** to rebind them. Changes apply immediately.
- **Last heard** shows the latest transcript, how long the speech was, how long transcription took, and which model did it.
- **Activity** is the live log. It's also written to `%LOCALAPPDATA%\VoiceForever\voiceforever.log`.

The first time a model runs on the GPU, the graphics driver compiles its shaders, which can take 20+ seconds. After that it loads in a few seconds.

## Checks to run in game first

| # | Question | How |
|---|---|---|
| 1 | Does typed text reach the chat box **in gamepad mode**? | `publish\VoiceForeverCli.exe --test-type`, then open chat (LB+RB+Down) within 5 s. |
| 2 | Does the app follow the chat panel? | Watch the app's status line while you open chat, open the X menu, pick a channel, and send. |
| 3 | End to end | LB+RB+Down, click RS, say something, wait for the text, press A. |
| 4 | **Safety** | Click RS, start talking, press **B** mid-sentence. Nothing should be typed, and your character mustn't move. |

## Config (`%LOCALAPPDATA%\VoiceForever\voiceforever.json`)

Plain JSON, checked at launch: a misspelt setting, a value out of range or a comment is reported (in the app's status line, or by the CLI) rather than silently ignored. Comments aren't allowed because the app rewrites the file whenever a setting changes, which would lose them.

| Key | Default | Meaning |
|---|---|---|
| `ProcessNames` | `["WowB"]` | Game process names without `.exe`. The beta is `WowB`; the live release may differ. |
| `OpenChatChord` | `LB+RB+DOWN` | WoW's open-chat combo. Set it from the app's Buttons & shortcuts tab. |
| `DictateChord` | `RS` | Starts a dictation while chat is open. Also set from the app. |
| `RadialMenuChord` | `START` | WoW's radial menu button (the Menu button), so chat opened from the radial is seen too |
| `KeyboardShortcut` | `null` (off) | e.g. `Ctrl+Shift+Space`. Dictates into whichever window has focus. Set from the app. |
| `SendChord`, `BackChord` | `A`, `B` | The chat panel's Send and Back |
| `MenuChords` | `X`, `Y` | The panel's Chat Channels and Tab Settings menus |
| `Prompt` | WoW place names | Words Whisper should expect. Without it, it hears "Iron Fudge" and "Dead Minds". Add guild and friends' names. |
| `ModelPath` | Turbo (q5_0) | The model list's **Use** sets this. |
| `Language` | `en` | Whisper language code, or `auto`. |
| `UseGpu` | `true` | Vulkan GPU; `false` for CPU only. Takes effect after a restart. |
| `BeamSize` | `1` | Decoding width. 1 (greedy) matched 5-beam accuracy on turbo in the benchmark, faster and lighter. |
| `MicDevice` | `-1` | `-1` is the Windows default mic. `--test-mic` lists the others. |
| `SilenceMs` | `1500` | Pause that ends a dictation. Set from the app's slider. |
| `NoSpeechTimeoutSeconds` | `6` | Gives up if you don't start talking. |
| `MaxSeconds` | `120` | Only a guard against a mic that never goes quiet; far longer than a chat message. |
| `SpeechThresholdDb` | `10` | How far above background noise counts as speech. Raise it if game audio from speakers triggers it; a headset helps. |
| `DelayMs` | `150` | Pause after RS before recording, so the beep isn't recorded. |
| `Sounds` | `true` | The audio cues. |

## CLI (`VoiceForeverCli.exe`)

| Command | Does |
|---|---|
| *(none)* | Headless mode, the same as the app without a window |
| `--probe` | Logs controller presses and chat-panel tracking. Records and types nothing. |
| `--test-mic` | One dictation from the microphone, printed with timing |
| `--test-type [text]` | Types text into WoW's chat box after a 5 s countdown |
| `--transcribe file.wav` | Transcribes a file |
| `--benchmark` | Memory, speed and word error rate for every installed model, at beam 5 and greedy (see below) |
| `--model path …` | Uses a different model for any of the above |

Only one copy, app or CLI, can watch the controller at a time; otherwise both would type every message.

## Benchmark

`VoiceForeverCli.exe --benchmark` measures every model in the models folder at both decoding widths, each in its own process: RAM, GPU memory (in video memory and spilled to system RAM), peak during transcription, speed, and word error rate. The test set is `%LOCALAPPDATA%\VoiceForever\benchmark\`: `.wav` files each with a `.txt` of what was said. Every clip is also run with noise mixed in. `powershell.exe -File tools\make-benchmark-clips.ps1` generates 24 clips with the Windows voices; add your own recordings the same way for a truer test.

Results with WoW running (8-second clips; memory in MB, held between dictations):

| Model | Decoding | Idle memory | Peak | Errors, clean | Errors, noisy | Per clip |
|---|---|---|---|---|---|---|
| turbo | beam 5 | 2,055 | 2,700 | 0.8% | 1.9% | 1,168 ms |
| turbo q8_0 | greedy | 1,215 | 1,174 | 0.8% | 1.5% | 785 ms |
| **turbo q5_0** | **greedy** | **932** | **884** | **0.8%** | **1.1%** | **673 ms** |
| small | beam 5 | 979 | 1,835 | 0.4% | 3.4% | 921 ms |
| base | greedy | 599 | – | 1.9% | 8.0% | 190 ms |

The test clips are synthetic voices. Adding a few recordings of your own voice to the benchmark folder is the best check that q5_0 holds up for you.

## Limitations

- **Chat opened with the keyboard** (Enter) isn't seen by the app, so RS won't dictate into it. Open chat with the controller, or use the keyboard shortcut.
- **The keyboard shortcut has no chat check**: like Windows+H, it types into whatever has focus. In WoW, open chat first, or the words become key presses.
- **WoW's chat box holds 255 characters**, roughly 40–50 words. Longer dictations are cut at the last whole word, and the app logs what was dropped.
- **Recognition ends on a pause**, detected by volume. Loud game audio through speakers can keep it listening; a headset avoids that.
- **Terms of service:** the app types only your own dictated words into one game client, as dictation software does. Blizzard's enforced policy targets input broadcasting across multiple clients. This is not something Blizzard has explicitly approved, though.

## Building

```
.\build.ps1              # self-contained app and CLI in publish\
.\build.ps1 -Installer   # also dist\VoiceForever-Setup-<version>.exe
```

The installer needs [Inno Setup](https://jrsoftware.org/isinfo.php) 6.7 or later. The version number comes from `Directory.Build.props`. `VoiceForever.slnx` opens everything in Visual Studio or Rider.

**Tests.** `dotnet test --project tests\VoiceForever.Core.Tests` runs the engine's tests (xUnit v3): end-of-speech detection, chat-panel and radial-menu tracking, bindings and shortcuts, the settings file and the model list. They use a temporary data folder (`VOICEFOREVER_DATA`), never your real settings. `build.ps1` runs them first and stops if any fail.

**Linting.** Every build runs the .NET analyzers (`AnalysisLevel` latest-recommended) and the code style rules in `.editorconfig`, and warnings fail the build. Package versions live in `Directory.Packages.props`, `nuget.config` pins the feed to nuget.org, and `global.json` pins the .NET SDK. The Visual C++ redistributable isn't kept in git: `build.ps1 -Installer` downloads it from Microsoft the first time and checks its signature.

**Layout.** Namespaces follow folders.

| Folder | What's there |
|---|---|
| `app\Core` | The engine library. `Engine.cs` ties it together. |
| `app\Core\Configuration` | `Config` (the settings file) and `AppPaths` |
| `app\Core\Input` | XInput, chords, the chat panel and radial menu trackers, the keyboard shortcut |
| `app\Core\Presentation` | The model list's row view model: no UI types, so it's tested without WinUI |
| `app\Core\Speech` | Recording, end-of-speech detection, Whisper, the model catalog and downloads |
| `app\Core\Dictation` | One dictation from button to typed text, and the sound cues |
| `app\Core\Interop`, `app\Core\Logging` | Typing via SendInput; the log |
| `app\Gui` | The WinUI 3 app: `Views\MainWindow` (one partial file per tab), `Controls\LogoView`, `Models` |
| `app\Cli` | Headless mode; `Commands` has the benchmark and setup checks |
| `tests\VoiceForever.Core.Tests` | The engine's tests |
| `installer` | Inno Setup script, branded wizard art, the Visual C++ redistributable |
| `tools` | Benchmark clips; `IconGen` draws the app icon and installer art from the logo geometry (`IconGen icon <out.ico>`, `IconGen wizard installerrt app\Gui\Assets\Fonts\Cinzel.ttf`) |
| `assets` | Logo SVGs |

`THIRD-PARTY-NOTICES.txt` ships with the app.

Data from before the rename to Voice Forever (`%LOCALAPPDATA%\ForeverVoice`) moves across automatically on first launch.
