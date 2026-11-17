# Pedal RoboSing

A vocal-tract instrument for ReBuzz. Text is the score for timbre: typed phrases
are compiled into mouth movement, and notes play that movement a syllable or a
phoneme at a time, in step with the tempo, or scrubbed like a wavetable. The
mouth's resonances can be tuned to the harmonics of the note or to a scale, and
the consonant noise can be pitched, so the words become part of the harmony.
It isn't trying to be a realistic singer.

Version 1.4.0 · managed generator · 8 tracks · net10.0-windows · GPL-3.0-or-later

The voice is original: a formant model converted to filter coefficients as it
plays, with no speech ROM data and no code from any other synthesiser. Word
pronunciations come from the CMU Pronouncing Dictionary (about 125,000 words,
built into the DLL), with the machine's own spelling rules for anything the
dictionary doesn't have.

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
| Line | One note sings the whole line from the melody drawn for it in the phrase editor. See below. |
| Scrub | The note just turns the voice on. The **Position** column says where in the window the mouth is; **Glide** sets how fast it travels there; **Scan** makes it travel on its own, forwards or backwards, looping. |

### Line mode: a melody kept with the words (trial, v1.4)

Writing a sung line into pattern rows means counting syllables and placing
note-offs by hand. In Line mode the melody lives with the line instead. Open
**Edit Phrases**, put the caret on a line, and the panel under the text shows
one block per syllable on a grid: time runs left to right in ticks (lighter
lines every beat, brighter every four), pitch up and down in semitones around
the middle row, which is the note you play.

- Drag a block **up or down** to change its pitch (12 semitones either way).
- Drag a block **sideways** to change the silence before it.
- Drag a block's **right edge** to change its length.
- **Sing line** plays the line under the caret with its melody.

In the pattern, set Play Mode to Line (or load the Line Singer preset) and put
one note on the row where the line starts, with its Phrase number. The note
sets the key; the line sings itself and ends with its last consonant, so no
note-off is needed. A new note restarts it. A line with no melody drawn sings
two ticks a syllable on the played note.

Because the machine knows the whole line in advance, each syllable's opening
consonants are sung just before its beat, so the vowel lands on it (measured
within about 10 ms on an ordinary line). The first syllable cannot start before
its note, so its vowel is late by its consonants unless you give it a gap.

Melodies are saved with the song. If you edit a line's words its blocks are
kept in order; if the syllable count changes, blocks are added or dropped at
the end. A melody belongs to the Speak text mode; in Spell modes Line uses the
plain two-ticks melody. Word and Words are ignored in Line mode.

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

**The sound of the words moves the voice** (the React controls):
the machine rates every sound it is about to make as open or closed, bright or
dark, hard or soft, sung or unsung, and each React control lets one of those
move one thing. 64 is off; above 64 works as described and below 64 does the
opposite. A new machine starts with them on.

| Control | Listens to | Moves |
|---|---|---|
| React Level | open vowels (ah) against closed ones (ee, oo) | loudness, up to 6 dB either way |
| React Tune | the same | the Tune amount, so open vowels ring and closed ones stay plain |
| React Size | bright sounds (ee, s) against dark ones (oo, m) | the mouth size, exaggerating each vowel's colour |
| React Ring | hard consonants (t, k, p) against soft ones (f, th) | the pitched-consonant amount |
| React Vib | stressed vowels against consonants | vibrato depth (needs Vib Depth above 0) |
| React Rhyme | syllables that rhyme with another word anywhere in the phrase list | more Tune, level and vibrato on the rhyme, so it rings out |
| React Allit | nearby words that open on the same consonant | those consonants hit harder and ring more |

Rhymes are found across the whole phrase list, so the ends of two different
lines can rhyme. A word's rhyme runs from its last stressed vowel to its end;
filler words (the, of, to) only count at the end of a line or clause. The
phrase editor lists the rhymes and alliteration it found on the line under the
caret. Detection works on the machine's own pronunciation, so a word it
mispronounces can miss a rhyme; fix the word with brackets and the bracketed
version stops taking part.

Glide smooths these along with the mouth, and a shared mouth passes them to the
tracks that follow it.

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
| Excitation | Chirp / Glottal / Saw / Square / Whisper | Glottal | The voice source. |
| Chip Rate | 4–22 kHz | 16 kHz | Internal sample rate. |
| Frame | 5–60 ms | 15 | How often the filter is updated. |
| Coef Bits | 2–11 | 10 | Filter coefficient resolution. 11 is off. |
| DAC Bits | 3–16 | 14 | Output resolution. 16 is off. |
| Speaker | 0–127 | 30 | Small-speaker voicing and image filtering. 0 is raw. |
| Note Off | Ignore / Cut | Ignore | Cut makes note-off a fast fade in every mode. With Ignore, note-off still releases a held syllable (it finishes its consonants) and stops Scrub and shared-mouth voices. |
| Breath | 0–127 | 0 | Adds noise to voiced sounds. |
| Play Mode | see above | Syllable | |
| Hold | Off / On | On | Syllable/Phoneme: sustain while the note is held. Off plays each step once at its natural length. |
| Sync Step | 1–32 ticks | 2 | Sync: ticks per syllable. |
| Glide | 0–127 | 15 | How slowly the mouth moves to a new shape (off, or 2 ms to 1 s). |
| Scan | 0–127 | 64 | Scrub: automatic travel, -2x to +2x speaking pace. 64 is still. |
| Mouth | Per Track / Shared | Per Track | |
| Tune | 0–127 | 50 | Resonance tuning amount. |
| Tune To | Harmonics / Scale | Harmonics | |
| Root | C–B | C | For Scale tuning and Inflect Snap. |
| Scale | 15 choices | Major | Chromatic, Major, Minor, Dorian, Mixolydian, Harmonic Minor, Major and Minor Pentatonic, Whole Tone, Major and Minor Triad, Major 7, Minor 7, Fifths, Octaves. |
| Inflect Snap | Off / On | Off | Quantises the pitch movement to the scale, so spoken intonation becomes a melody (try Phrase mode with Inflection high). |
| Cons Tune | 0–127 | 40 | Pitched-consonant amount. |
| Cons Pitch | Unison / Fifth / Octave / Octave+Fifth / 2 Octaves | Octave | |
| Vib Depth | 0–127 | 30 | Up to ±1 semitone, fading in after the attack. |
| Vib Rate | 0–127 | 64 | 2 to 10 Hz. |
| Portamento | 0–127 | 20 | Pitch glide time (off, or 5 ms to 1 s). |
| React Level, Tune, Size, Ring, Vib, Rhyme, Allit | 0–127 | on | See above. Defaults 95, 120, 90, 110, 120, 100, 100. |

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
Spelling Bee, Broken Batteries, Giant Drone, Tiny Robot, Reactive Singer, Open
Vowels Ring, Rubber Mouth, Hard Edges, Rhyme Spotlight, Tongue Twister and Line Singer. They set the globals
only. `gen_presets.py --check` verifies the bundle against the source.

## How it works

1. **Text to mouth movement.** The built-in word list, the CMU dictionary and
   then the letter-to-sound rules give phonemes; each becomes formant, voicing, noise and pitch keyframes. Words are
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

## Singing a melody from a MIDI file

`examples/midi_to_robosing.py` turns one channel of a MIDI file into a song with
one RoboSing in Syllable mode, a note per syllable:

```
python midi_to_robosing.py song.mid --list              # show the channels
python midi_to_robosing.py song.mid --channel 1 -o song.bmxml
```

The melody is cut into phrases at rests (`--gap`, in beats). Every phrase becomes
a line of the phrase list filled with one `la` per note, and a template
(`song.lyrics.txt`) lists how many syllables each line needs. Type the words in
ReBuzz with Edit Phrases, where each line's syllable count is shown, or edit the
template and run again with `--lyrics song.lyrics.txt`. Needs Python 3 and
`robosing_ref.bmxml` beside the script.

### Drums and melody together

`examples/midi_to_song.py` does the same and also puts the MIDI drum channel on
one Pedal DrumGrid, a lane per drum sound in kit order, with velocities:

```
python midi_to_song.py song.mid --melody-channel 1 -o song.bmxml
python midi_to_song.py song.mid -o song.bmxml            # drums only
```

It prints the lane list (also written to `song.lanes.txt`) so you know which
sample to load on each lane; DrumGrid is left empty. Use `--tpb 8` if it reports
hits falling between rows. It needs `drums_singer_ref.bmxml` (a ReBuzz save with
one DrumGrid and one RoboSing), `robosing_ref.bmxml`, `build_example.py` and
`midi_to_robosing.py` beside it.

## Changes

**1.4.0** (trial of line melodies)
- Play Mode gains **Line**: one pattern note sings a whole line from a melody
  stored with it (a gap, a length and a pitch per syllable).
- The phrase editor gains a melody panel for the line under the caret, and a
  Sing line button.
- Vowels land on their beats in Line mode.
- Melodies are saved with the song; songs saved by earlier versions load as
  before and simply have none.
- The melody panel has not been seen running: it was checked for compile
  errors against stand-ins only. Expect rough edges.

**1.3.0**
- The CMU Pronouncing Dictionary is built in: 124,911 words, packed to 750 KB
  inside the DLL and loaded once in about 30 ms. Words are looked up in the
  machine's own short list first, then the dictionary, then the spelling rules.
- Some words change their syllable count because the dictionary is right where
  the rules were wrong (everything is now 3 syllables, evening 2) or takes a
  different view (fire is 2: fi-er). A line you matched to notes syllable by
  syllable may now be one out; the phrase editor shows each line's count.
- Rhyme and alliteration detection is more accurate, since it works on the
  pronunciation.

**1.2.1**
- Pronunciation: about 100 common lyric words added to the dictionary (tonight,
  today, together, heart, believe, forever, enough, touch, friend, eyes, ...),
  mostly fixing a wrong stress or vowel. Regular endings of dictionary words
  are now handled (hearts, dreaming, touched), and doubled letters are one
  sound (running, little).

**1.2.0**
- React Rhyme and React Allit: rhyming syllables and alliterating consonants
  are detected and brought out. Two presets use them alone.
- New defaults, close to the Reactive Singer preset: clean chip settings
  (Glottal, 16 kHz, 15 ms frame, 10 coefficient bits, 14 DAC bits, Speaker 30),
  Tune 50, Cons Tune 40, Vib Depth 30, Portamento 20, Glide 15 and the React
  controls on. For the old starting sound, load the Chip Syllables preset.
- A song saved with 1.0 has no React values, so it picks up the new React
  defaults when opened and will sound different. Set the seven React controls
  to 64 to get the old behaviour. Songs saved with 1.1 keep their values for
  the first five and pick up Rhyme and Allit. Everything else is stored in the
  song and does not change.

**1.1.0**
- Five React controls: the sound of each word moves level, tuning, mouth size,
  consonant ring and vibrato. Four presets use them.
- Vowel loudness evened out. Held "ee" and "oo" were about 8 dB louder than
  "ah"; they are now within about 2 dB, averaged over pitch. Songs made with
  1.0 will sound slightly different in balance.
- The React controls are five new global parameters, so the track columns move
  five places to the right. ReBuzz matches saved parameters by name, so songs
  saved with 1.0 should load unchanged; check one before relying on it. Files
  written by the example scripts use the layout of their reference save.

## Limitations

- **Lightly tested inside ReBuzz.** It builds, loads and plays in ReBuzz 1849.
  Most testing was on Linux against stand-ins for the ReBuzz interfaces
  (`tests/compile`); the modes, the phrase editor and save/load have not been
  checked one by one in ReBuzz.
- **Tuned by measurement, not by ear.** Levels and spectra were checked; how it
  actually sounds needs listening.
- English only, in the dictionary's American pronunciation. Names and made-up
  words fall back to the spelling rules; use brackets when those get it wrong.
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

Pronunciations come from the **CMU Pronouncing Dictionary**
(github.com/cmusphinx/cmudict, commit 74790861), Copyright (C) 1993-2015
Carnegie Mellon University, used and redistributed under its own licence, which
is reproduced in `Speech/cmudict.LICENSE.txt`. `tools/build_lexicon.py` packs it
into `Speech/cmudict.bin.gz`: first pronunciation of each word only, words with
characters other than letters and apostrophes left out, and unstressed AH
written as the schwa AX. The packed file is embedded in the DLL.

The letter-to-sound rules follow the method published in *Automatic Translation
of English Text to Phonetics by Means of Letter-to-Sound Rules* by H. S.
Elovitz, R. Johnson, A. McHugh and J. E. Shore (NRL Report 7948, 1976), a U.S.
Government work. They were written out for this machine; no code from any
implementation of that report is used. Phoneme formant values are rounded
textbook figures.
