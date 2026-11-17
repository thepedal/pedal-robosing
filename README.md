# Pedal RoboSing

A vocal-tract instrument for ReBuzz. Text is the score for timbre: typed phrases
are compiled into mouth movement, and notes play that movement a syllable or a
phoneme at a time, in step with the tempo, or scrubbed like a wavetable. The
mouth's resonances can be tuned to the harmonics of the note or to a scale, and
the consonant noise can be pitched, so the words become part of the harmony.
It isn't trying to be a realistic singer.

Version 2.0.0 · managed generator · 8 tracks · net10.0-windows · GPL-3.0-or-later

Everything is original: English letter-to-sound rules and a formant model,
converted to filter coefficients as it plays. No speech ROM data and no code
from any other synthesiser is used.

## Quick start

1. Build (see below), restart ReBuzz, add **Pedal RoboSing** from the generators.
2. Right-click it, **Edit Phrases...**, and type one phrase per line.
3. Put notes in the pattern. Each note sings the next syllable of Phrase 1 and
   holds it until a note-off or the next note.
4. Turn **Tune** up and listen to the vowels lock onto the note's overtones.

## The three ideas

**The mouth is a playhead** (Play Mode):

| Mode | What a note does |
|---|---|
| Phrase | Speaks the whole phrase (or the Word/Words window) at speaking pace. |
| Syllable | Sings the next syllable. With Hold on, the vowel sustains until note-off, then the closing consonants are sung. A new note without a note-off moves straight to the next syllable. |
| Phoneme | The same, one phoneme per note. |
| Sync | Speaks the phrase with one syllable every *Sync Step* ticks. Short syllables hold their vowel to fill the slot; long ones are squeezed. |
| Scrub | The note just turns the voice on. The **Position** column says where in the window the mouth is; **Glide** sets how fast it travels there; **Scan** makes it travel on its own, forwards or backwards, looping. |

In Syllable and Phoneme modes the step counter wraps round the Word/Words
window, restarts when Phrase, Word or Words is written, and can be set for a
note with the **Step** column.

**Resonances tuned to the music** (Tune, Tune To, Root, Scale): each vowel
resonance is pulled toward the nearest harmonic of the played note, or the
nearest tone of the scale, and narrowed so it rings. Low settings add focus;
high settings turn each vowel into a small chord of overtones, and the words
play a melody inside a held note. Each resonance takes a different pitch.
Intelligibility falls as Tune rises; that is the trade.

**Pitched consonants** (Cons Tune, Cons Pitch): the noise in s, t, k, f and
friends is passed through a comb tuned to the note (or a fifth, an octave, and
so on above it), which rings on briefly after the consonant. Phoneme mode with
Hold off turns a line of text into tuned percussion.

## Track columns

| Column | Range | Default | What it does |
|---|---|---|---|
| Note | note / off | | Starts or moves the voice, and sets its pitch. |
| Velocity | 0–127 | 100 | Level. 100 is unity. |
| Phrase | 1–100 | 1 | Which line of the phrase list. The read-out shows the text. |
| Word | 1–100 | 1 | The window starts at this word. |
| Words | 0–100 | 0 (All) | How many words are in the window. |
| Step | 1–200 | | Syllable/Phoneme: this note plays step N of the window. |
| Position | 0–1000 | 0 | Scrub: where the mouth is in the window. |

Each track is its own voice. With **Mouth** set to Shared, tracks 2–8 copy
track 1's mouth and only supply pitch, so a chord speaks as one voice.

## Global parameters

| Name | Range | Default | What it does |
|---|---|---|---|
| Volume | 0–127 | 100 | Output level. |
| Speed | 0–127 | 64 | Speaking rate, 0.40x to 2.48x. In step modes it sets how fast a syllable's consonants go by. |
| Voice | 0–127 | 64 | Vocal tract size (formants x0.66 to x1.51). |
| Inflection | 0–127 | 64 | Pitch movement from the phrase's intonation. In Syllable/Phoneme modes it is measured from the held vowel, so the held note is always in tune. Ignored in Scrub unless Inflect Snap is on. |
| Mode | Speak / Spell / Say+Spell | Speak | Spell turns each word into its letter names. |
| Excitation | Chirp / Glottal / Saw / Square / Whisper | Chirp | The voice source. |
| Chip Rate | 4–22 kHz | 8 kHz | Internal sample rate. |
| Frame | 5–60 ms | 25 | How often the filter is updated. |
| Coef Bits | 2–11 | 6 | Filter coefficient resolution. 11 is off. |
| DAC Bits | 3–16 | 8 | Output resolution. 16 is off. |
| Speaker | 0–127 | 64 | Small-speaker voicing and image filtering. 0 is raw. |
| Note Off | Ignore / Cut | Ignore | Cut makes note-off a fast fade in every mode. With Ignore, note-off still releases a held syllable (it finishes its consonants) and stops Scrub and shared-mouth voices. |
| Breath | 0–127 | 0 | Adds noise to voiced sounds. |
| Play Mode | see above | Syllable | |
| Hold | Off / On | On | Syllable/Phoneme: sustain while the note is held. Off plays each step once at its natural length. |
| Sync Step | 1–32 ticks | 2 | Sync: ticks per syllable. |
| Glide | 0–127 | 0 | How slowly the mouth moves to a new shape (off, or 2 ms to 1 s). |
| Scan | 0–127 | 64 | Scrub: automatic travel, -2x to +2x speaking pace. 64 is still. |
| Mouth | Per Track / Shared | Per Track | |
| Tune | 0–127 | 0 | Resonance tuning amount. |
| Tune To | Harmonics / Scale | Harmonics | |
| Root | C–B | C | For Scale tuning and Inflect Snap. |
| Scale | 15 choices | Major | Chromatic, Major, Minor, Dorian, Mixolydian, Harmonic Minor, Major and Minor Pentatonic, Whole Tone, Major and Minor Triad, Major 7, Minor 7, Fifths, Octaves. |
| Inflect Snap | Off / On | Off | Quantises the pitch movement to the scale, so spoken intonation becomes a melody (try Phrase mode with Inflection high). |
| Cons Tune | 0–127 | 0 | Pitched-consonant amount. |
| Cons Pitch | Unison / Fifth / Octave / Octave+Fifth / 2 Octaves | Octave | |
| Vib Depth | 0–127 | 0 | Up to ±1 semitone, fading in after the attack. |
| Vib Rate | 0–127 | 64 | 2 to 10 Hz. |
| Portamento | 0–127 | 0 | Pitch glide time (off, or 5 ms to 1 s). |

The first 13 are the v1.0 set; the rest were appended in v2.0.

## Writing phrases

- **One phrase per line**, up to 100.
- **Punctuation** shapes Phrase-mode delivery: `,` pause, `.` falling end, `?`
  rising end, `!` bigger accents.
- **Numbers** are read as words; in Spell mode, digit by digit.
- **Phonemes in brackets** replace a word: `[R IY B AH1 Z]`. `1` marks the
  stressed vowel, `_` is a pause. This is also how to write pure sound: `[AA]`,
  `[S]`, `[M AA M AA]`.
  Vowels: IY IH EY EH AE AA AO OW UH UW AH AX ER AY AW OY.
  Consonants: P B T D K G CH JH F V TH DH S Z SH ZH HH M N NG L R W Y WH.
- The editor shows the line under the caret as phonemes, with `·` between the
  syllables Syllable mode will step through, and counts of words, syllables and
  phonemes. **Speak line** plays the whole line on track 1.

## Presets

`Pedal RoboSing.prs.xml` loads automatically: Syllable Singer, Overtone Singer,
Scale Bells, Chant in Time, Choir One Mouth, Tuned Percussion, Scrub Pad,
Backwards Loop, Speech Melody, Whisper Hiss, Chip Talker, Chip Syllables,
Spelling Bee, Broken Batteries, Giant Drone and Tiny Robot. They set the globals
only. `gen_presets.py --check` verifies the bundle against the source.

## How it works

1. **Text to mouth movement.** Dictionary and letter-to-sound rules give
   phonemes; each becomes formant, voicing, noise and pitch keyframes. Words are
   split into syllables (maximal onset), and every syllable and phoneme gets a
   start, a hold point inside its vowel, and an end. All of this is compiled on
   the UI thread when the phrase list changes, so playback allocates nothing.
2. **Playhead.** A unit is played in to its hold point, frozen there for as long
   as needed (until note-off, or to fill a tick slot), then played out. Timing
   overshoot is carried from one syllable to the next, so Sync doesn't drift.
3. **Tuning.** Each frame the resonances are moved in pitch toward their target
   and narrowed, before being turned into an all-pole filter.
4. **Filter.** Formants become reflection coefficients (step-down recursion),
   optionally quantised, interpolated in 8 steps per frame, and drive a 10-pole
   lattice. The source is a pitched pulse plus noise; the noise can go through a
   feedback comb at the note's pitch.
5. **Output.** DAC quantiser, hold to the host rate, Speaker filter.

## Example song

`examples/robosing_showcase.bmxml` is a 24-bar piece (about 46 s at 126 BPM) made
of five RoboSing instances, one per idea, entering in turn:

| Machine | Bars | Shows |
|---|---|---|
| Perc | 1–20 | Phoneme mode, Hold off, Cons Tune: a line of consonants as tuned percussion. |
| Drone | 5–24 | Syllable mode on a low note, Tune to A minor pentatonic with Glide: the words move overtones inside the note. |
| Lead | 9–20 | Syllable mode, one syllable per note with note-offs, Tune to harmonics, vibrato. |
| Choir | 13–20 | Sync mode with a shared mouth: three tracks speak a chord together, a syllable per tick. |
| Scrub | 21–24 | Scrub mode: the Position column drags two mouths through a phrase, one forwards, one backwards. |

Mute machines to hear each alone. `examples/build_example.py` rebuilds it from
`robosing_ref.bmxml`, a one-machine song saved by ReBuzz 1849; the machine and
editor blocks are cloned from that save, never written by hand.
`tests/compile` can play the same events through the machine code:
`dotnet run -c Release -- song ../../examples/robosing_showcase.events.txt out.wav`.

## Limitations

- **Lightly tested inside ReBuzz.** It builds, loads and plays in ReBuzz 1849.
  Most testing was on Linux against stand-ins for the ReBuzz interfaces
  (`tests/compile`); the modes, the phrase editor and save/load have not been
  checked one by one in ReBuzz.
- **Tuned by measurement, not by ear.** Levels and spectra were checked; how it
  actually sounds needs listening.
- English only. Use brackets for names and odd spellings.
- Editing phrases may not mark the song as modified. Save with Ctrl+S.
- Needs ReBuzz 1848 or later for notes on several tracks in one row.
- Held notes with no note-off sustain for ever, like any synth. Shared-mouth
  followers stay ready (silent) until their note-off.

## Build

Open `Pedal RoboSing.NET.csproj` in Visual Studio 2026 or run
`dotnet build -c Release`. The post-build step copies `Pedal RoboSing.NET.dll`
and `Pedal RoboSing.prs.xml` to `C:\Program Files\ReBuzz\Gear\Generators`
(override with `-p:ReBuzzPath=...`). Close ReBuzz first. Only the DLL is
produced (no `.pdb` or `.deps.json`).

`tests/compile` type-checks the machine against stand-in interfaces and runs a
smoke test; `dotnet run -c Release -- demo out.wav` renders the demo reel. It is
not part of the machine build.

## Credits and licence

Copyright (C) 2026 thepedal. GPL-3.0-or-later; see `LICENSE`.

The letter-to-sound rules follow the method published in *Automatic Translation
of English Text to Phonetics by Means of Letter-to-Sound Rules* by H. S.
Elovitz, R. Johnson, A. McHugh and J. E. Shore (NRL Report 7948, 1976), a U.S.
Government work. They were written out for this machine; no code from any
implementation of that report is used. Phoneme formant values are rounded
textbook figures.
