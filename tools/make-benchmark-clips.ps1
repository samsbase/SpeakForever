# Generates the benchmark test set: WoW-style chat lines spoken by each installed Windows voice,
# as 16 kHz mono .wav files, each with a .txt holding what was said.
# Your own recordings can go in the same folder the same way (clip.wav + clip.txt).
# Needs Windows PowerShell 5.1 for System.Speech: powershell.exe -File tools\make-benchmark-clips.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech

$out = Join-Path $env:LOCALAPPDATA 'VoiceForever\benchmark'
New-Item -ItemType Directory -Force $out | Out-Null

$lines = @(
    'Anyone want to run Deadmines? Meet me at the Ironforge bank in five minutes.'
    'Can somebody help me kill the elite ogre near the lake in Elwynn Forest?'
    "I'm heading to Orgrimmar to train, back in ten minutes."
    'Does anyone know where the flight master is in Stormwind?'
    'Thanks for the group, that was a really fun dungeon.'
    'Can you trade me some bandages and a few healing potions?'
    'We need one more healer for Molten Core tonight at eight.'
    "Watch out, there's a patrol coming from the left."
    "I'll pull the next pack, make sure you're all at full mana."
    'Is the auction house in Undercity cheaper than the one in Orgrimmar?'
    "Sorry, I have to go make dinner, I'll be back soon."
    "Let's meet at the entrance to Blackrock Mountain."
)

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($voice in $synth.GetInstalledVoices() | Where-Object Enabled) {
    $name = $voice.VoiceInfo.Name -replace '^Microsoft ', '' -replace ' Desktop$', ''
    $synth.SelectVoice($voice.VoiceInfo.Name)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $clip = Join-Path $out ('{0}-{1:D2}' -f $name.ToLower(), ($i + 1))
        $synth.SetOutputToWaveFile("$clip.wav", $format)
        $synth.Speak($lines[$i])
        $synth.SetOutputToNull()
        Set-Content "$clip.txt" $lines[$i] -Encoding UTF8
    }
}
$synth.Dispose()
Write-Host "Wrote $((Get-ChildItem $out -Filter *.wav).Count) clips to $out"
