// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 thepedal
//
// Pedal RoboSing — one voice, run at the "chip" sample rate.
//
// A voice has a mouth (where it is in a compiled phrase), a pitch and a source.
// The mouth is a playhead with five ways of moving:
//   Run     plays a window of the phrase through at speaking pace
//   Seg     plays one unit (syllable or phoneme): in to the hold point, freeze
//           there while the note is held, then out through the rest
//   Sync    plays syllable after syllable, each fitted to a slot of N ticks
//   Scrub   sits wherever the Position column puts it, optionally scanning
//   Follow  copies another voice's mouth, so a chord speaks as one
//   Line    sings a whole line from its own melody: a length, pitch and gap per
//           syllable, with each vowel landing on its beat
//
// Every frame the mouth's formants are (optionally) pulled toward the harmonics
// of the note or the tones of a scale, turned into an all-pole polynomial, then
// into reflection coefficients (step-down), optionally quantised like an LPC
// chip, and interpolated in 8 steps into a 10-pole lattice filter. The lattice
// is driven by a pitched source plus noise; the noise can be passed through a
// comb tuned to the note so consonants are pitched too.
//
// Nothing here allocates after construction.

using System;

namespace PedalRoboSing.Speech
{
    public sealed class LpcVoice
    {
        public const int MaxOrder = 10;
        const int Steps = 8;
        const float OutLevel = 0.2f;
        // Level trims so the excitations sit at similar loudness through the
        // formant filter (low-heavy sources excite F1 harder than the chirp).
        static readonly float[] ExcTrim = { 1.0f, 0.5f, 0.5f, 0.45f, 1.0f };

        enum Kind { Run, Seg, Sync, Scrub, Follow, Line }

        // ── Per-buffer settings, written by the machine before rendering ─────
        public int   ChipRate = 8000;
        public float FrameMs = 25f;
        public int   CoefBits = 6;          // 11 = unquantised
        public int   DacBits = 8;           // 16 = unquantised
        public int   Excitation;            // 0 chirp, 1 glottal, 2 saw, 3 square, 4 whisper
        public float FormantScale = 1f;
        public float Inflection = 1f;
        public float Speed = 1f;
        public float Breath;

        public float GlideMs;               // mouth smoothing (0 = none)
        public float TuneAmt;               // 0..1 formant tuning
        public int   TuneMode;              // 0 harmonics of the note, 1 scale
        public int   Root;                  // 0..11, C = 0
        public int   ScaleMask = 0xFFF;     // bit n = semitone n above the root
        public bool  InflSnap;              // quantise the pitch contour to the scale
        public float ConsTune;              // 0..1 pitched-noise amount
        public float ConsRatio = 2f;        // comb pitch relative to the note
        public float VibDepth, VibRateHz = 6f;   // semitones, Hz
        public float PortaMs;
        public float TickMs = 125f;
        public int   SyncTicks = 2;
        public float ScanRate;              // Scrub: phrase ms per real ms, signed
        public float ScrubTarget01;         // Scrub: position in the window, 0..1
        public int   KeyRoot, KeyMask = 0xFFF;   // the song's key, for Key Lock (apart from Root/ScaleMask, which tune the sound)
        public bool  KeyLock;               // the melody moves by scale steps from the note that starts it
        public bool  CatchBursts = true;    // never step over a stop's burst between frames
        public bool  LineHold = true;       // Line: vowels fill their blocks (off = short, with silence after)

        // React: how far the feel of each sound moves the voice, each -1..+1 (0 = off)
        public float ReactLevel;            // open vowels louder, closed quieter
        public float ReactTune;             // open vowels more tuned
        public float ReactSize;             // bright sounds a smaller mouth, dark a bigger one
        public float ReactRing;             // hard consonants ring more, soft ones less
        public float ReactVib;              // vibrato on sung sounds, none on consonants
        public float ReactRhyme;            // rhyming syllables ring out: more tuning, level and vibrato
        public float ReactAllit;            // alliterating consonants hit harder and ring more

        // ── Mouth state other voices may follow ──────────────────────────────
        public readonly float[] MouthF = new float[4], MouthB = new float[4];
        public float MouthAV, MouthAN, MouthSemis;
        public FeelKey MouthFeel = FeelKey.Neutral;
        public bool MouthLive;

        public bool IsActive { get; private set; }
        /// <summary>The note the voice is aiming at now, as a semitone number (before vibrato and portamento).</summary>
        public float NoteNow => _noteMidi;

        // ── Playhead ─────────────────────────────────────────────────────────
        struct Req
        {
            public Kind Kind; public Utterance U; public LpcVoice Leader; public LineScore Score;
            public float A, B, C, Slot; public int I0, I1; public bool Rev;
            public float Midi, Amp;
        }

        Kind _kind;
        Utterance _u = Utterance.Empty;
        LpcVoice _leader;
        float _t, _tStart, _tEnd;
        float _segS, _segH, _segE, _rate, _holdLeft;
        int _phase, _sylIdx, _sylLast;
        float _scanOff, _refSemis;
        LineScore _score;                       // the line's melody: timing in Line mode, pitch in every mode
        int _lineStep, _lineFirst, _lineLast, _lineCount, _sylCur;
        bool _lineRev;
        float _lineOffTicks, _lineLead;
        float _lineClock, _lineNom, _lineNextNom, _lineWait, _lineBase;
        bool _resting, _vowelSeen, _lineSettled;
        /// <summary>Test hook: called with (syllable, ms since the line started) when a syllable's vowel begins.</summary>
        public Action<int, float> LineTrace;
        /// <summary>Test hook: playhead (ms into the phrase), voiced level and noise level of each frame.</summary>
        public Action<float, float, float> FrameTrace;
        float _tSeen;                           // how far the playhead had got at the last frame
        int _fi, _si, _pi, _qi;
        FeelKey _q = FeelKey.Neutral, _qRaw = FeelKey.Neutral;
        float _consEff, _vibMul = 1f;
        bool _speaking, _snap;
        int _tailSamples;
        float _noteMidi = 48f, _pitchSm = 48f, _amp = 1f;
        float _vibPhase, _vibMs;

        bool _pending; Req _req;
        float _gain, _gainTarget;

        // ── Frame / step interpolation state ─────────────────────────────────
        readonly float[] _kPrev = new float[MaxOrder], _kTgt = new float[MaxOrder], _kCur = new float[MaxOrder];
        float _avPrev, _avTgt, _anPrev, _anTgt, _f0Prev, _f0Tgt;
        float _av, _an, _f0, _g;
        int _step;
        float _stepLeft;

        // ── Filter / excitation state ────────────────────────────────────────
        readonly double[] _bd = new double[MaxOrder + 1];
        float _phaseOsc;
        uint _rng;
        readonly float[] _comb = new float[2048];
        int _combW;

        readonly float[] _F = new float[5], _B = new float[5];
        readonly double[] _poly = new double[MaxOrder + 1], _tmp = new double[MaxOrder + 1];
        readonly float[] _fTrack = new float[4], _bTrack = new float[4];
        readonly float[] _fSm = new float[4], _bSm = new float[4];
        float _avSm, _anSm;

        float[] _chirp = new float[64];
        int _chirpLen, _chirpRate;

        public LpcVoice(uint seed) { _rng = seed | 1u; }

        // ── Starting notes ───────────────────────────────────────────────────
        /// <summary>Speak the window [tStart, tEnd] of a phrase at speaking pace.</summary>
        public void StartPhrase(Utterance u, float tStart, float tEnd, float midi, float amp, LineScore score = null) =>
            Start(new Req { Kind = Kind.Run, U = u, A = tStart, B = tEnd, Midi = midi, Amp = amp, Score = score }, false);

        /// <summary>Play one unit. slot &lt; 0: natural length; +infinity: hold until NoteOff; else fit to slot ms.</summary>
        public void StartSegment(Utterance u, float s, float h, float e, float slot, float midi, float amp) =>
            Start(new Req { Kind = Kind.Seg, U = u, A = s, B = h, C = e, Slot = slot, Midi = midi, Amp = amp }, true);

        /// <summary>Play syllables first..last, one per SyncTicks ticks.</summary>
        public void StartSync(Utterance u, int first, int last, float midi, float amp, LineScore score = null) =>
            Start(new Req { Kind = Kind.Sync, U = u, I0 = first, I1 = last, Midi = midi, Amp = amp, Score = score }, false);

        public void StartScrub(Utterance u, float tStart, float tEnd, float midi, float amp, LineScore score = null) =>
            Start(new Req { Kind = Kind.Scrub, U = u, A = tStart, B = tEnd, Midi = midi, Amp = amp, Score = score }, true);

        /// <summary>Sing the whole line from its melody (null = two ticks a syllable, all on the note).</summary>
        public void StartLine(Utterance u, LineScore score, float midi, float amp) =>
            StartLine(u, score, 0, (u?.SylS.Length ?? 0) - 1, false, midi, amp);

        /// <summary>Sing syllables first..last of the line; backwards = last syllable first, each still sung forwards.</summary>
        /// <param name="leadMs">Started this long before the line is due, so the first syllable's consonants can come ahead of its beat.</param>
        public void StartLine(Utterance u, LineScore score, int first, int last, bool backwards, float midi, float amp, float leadMs = 0f) =>
            Start(new Req { Kind = Kind.Line, U = u, Score = score, I0 = first, I1 = last, Rev = backwards, Midi = midi, Amp = amp, A = Math.Max(0f, leadMs) }, false);

        /// <summary>How long before its beat the first syllable of a line wants to start, at the current speed.</summary>
        public static float LeadFor(Utterance u, float speed) =>
            u != null && u.SylS.Length > 0 ? (u.SylV[0] - u.SylS[0]) / Math.Max(0.05f, speed) : 0f;

        public void StartFollow(LpcVoice leader, float midi, float amp) =>
            Start(new Req { Kind = Kind.Follow, U = Utterance.Empty, Leader = leader, Midi = midi, Amp = amp }, true);

        void Start(in Req r, bool legato)
        {
            if (r.Kind != Kind.Follow && (r.U == null || r.U.Length <= 0)) return;
            if (IsActive && _gain > 0.001f)
            {
                if (legato)
                {
                    // Same breath: move the mouth and the pitch, keep the filter running
                    _pending = false;
                    _noteMidi = r.Midi; _amp = r.Amp;
                    SetPlayer(r, false);
                    _gainTarget = 1f;
                    return;
                }
                _pending = true; _req = r; _gainTarget = 0f;      // 3 ms fade, then restart
                return;
            }
            Begin(r);
        }

        public void NoteOff(bool cut)
        {
            if (!IsActive) return;
            if (_kind == Kind.Seg && !cut)
            {
                if (float.IsPositiveInfinity(_holdLeft)) _holdLeft = 0f;   // finish the unit
                return;
            }
            if (cut || _kind == Kind.Scrub || _kind == Kind.Follow) Kill();
        }

        /// <summary>Quick fade to silence (cut, transport stop).</summary>
        public void Kill()
        {
            _pending = false;
            _gainTarget = 0f;
            if (_gain <= 0.001f) IsActive = false;
        }

        void Begin(in Req r)
        {
            _pending = false;
            _noteMidi = r.Midi; _amp = r.Amp;
            SetPlayer(r, true);
            Array.Clear(_bd, 0, _bd.Length);
            Array.Clear(_comb, 0, _comb.Length);
            _phaseOsc = 0f; _vibPhase = 0f; _vibMs = 0f;
            EnsureChirp();

            _snap = true;
            ComputeFrame();
            _snap = false;
            Array.Copy(_kTgt, _kPrev, MaxOrder);
            _avPrev = _avTgt; _anPrev = _anTgt; _f0Prev = _f0Tgt;
            AdvanceFrame();
            _step = 0; _stepLeft = 0;
            IsActive = true;
            _gain = 0f; _gainTarget = 1f;
        }

        void SetPlayer(in Req r, bool fresh)
        {
            _kind = r.Kind;
            _u = r.U ?? Utterance.Empty;
            _leader = r.Leader;
            _speaking = true;
            _tailSamples = (int)((0.04f + 0.35f * Math.Max(ConsTune, _consEff)) * ChipRate);
            _fi = _si = _pi = _qi = 0; _tSeen = float.MaxValue;
            _refSemis = 0f;
            _score = r.Score; _lineBase = r.Midi; _sylCur = 0;
            switch (r.Kind)
            {
                case Kind.Run:
                    _tStart = r.A; _tEnd = r.B; _t = r.A;
                    MelodyPitch();
                    break;
                case Kind.Seg:
                    _tStart = 0; _tEnd = _u.Length;
                    SetSeg(r.A, r.B, r.C, r.Slot, 0f);
                    break;
                case Kind.Sync:
                    _tStart = 0; _tEnd = _u.Length;
                    _sylIdx = r.I0; _sylLast = r.I1;
                    SetSeg(_u.SylS[_sylIdx], _u.SylH[_sylIdx], _u.SylE[_sylIdx], TickMs * SyncTicks, 0f);
                    _noteMidi = Mel(_sylIdx);
                    break;
                case Kind.Scrub:
                    _tStart = r.A; _tEnd = r.B; _scanOff = 0f;
                    if (fresh) _t = ScrubTarget();
                    MelodyPitch();
                    break;
                case Kind.Line:
                {
                    _tStart = 0; _tEnd = _u.Length;
                    int n = _u.SylS.Length;
                    _lineFirst = Math.Clamp(r.I0, 0, Math.Max(0, n - 1));
                    _lineLast = Math.Clamp(r.I1, _lineFirst, Math.Max(0, n - 1));
                    _lineCount = n > 0 ? _lineLast - _lineFirst + 1 : 0;
                    _lineRev = r.Rev;
                    _lineClock = 0f; _lineStep = 0; _resting = false;
                    _lineLead = r.A;
                    _lineNom = StepGap(0) * TickMs + _lineLead;

                    // Where this run sits on the editor's grid, for the marker
                    int ticks = 0;
                    for (int i = 0; i < n && i <= _lineLast; i++)
                    {
                        if (!_lineRev && i == _lineFirst) { if (i > 0) ticks += Gap(i); break; }
                        ticks += Gap(i) + Len(i);
                    }
                    _lineOffTicks = ticks;

                    if (_lineCount > 0) { _noteMidi = Mel(K(0)); _t = _u.SylS[K(0)]; }
                    LineEnter();
                    break;
                }
            }
        }

        /// <summary>For the editor's marker: ticks into the line, if this voice is singing the line with this transcription.</summary>
        public bool TryLineProgress(string phonetic, out float ticks)
        {
            ticks = -1f;
            if (!IsActive || _kind != Kind.Line || !_speaking || _u.Phonetic != phonetic) return false;
            float run = Math.Max(0f, _lineClock - _lineLead) / Math.Max(1f, TickMs);
            ticks = _lineRev ? Math.Max(0f, _lineOffTicks - run) : _lineOffTicks + run;
            return true;
        }

        // ── Line mode: syllables on a tick grid ──────────────────────────────
        // _lineNom is where the current syllable's vowel belongs, in ms from the
        // start of the line; _lineClock is how far the line has actually got.
        // A syllable starts early by the length of its opening consonants, taken
        // from the syllable or rest before it, so the vowel lands on the beat.
        int Gap(int k) => _score?.GapAt(k) ?? 0;
        int Len(int k) => _score?.LenAt(k) ?? LineScore.DefaultLen;
        int Pitch(int k) => _score?.PitchAt(k) ?? 0;

        // The note for syllable k: the starting note moved by the melody, in semitones or, locked, in scale steps
        float Mel(int k)
        {
            if (_score == null) return _lineBase;
            return MelP(_score.PitchAt(k));
        }
        float MelP(int p) => KeyLock ? ScaleSteps.Transpose(_lineBase, p, KeyRoot, KeyMask) : _lineBase + p;

        // Step j of the run is syllable K(j): the Word/Words window, forwards or backwards.
        int K(int j) => _lineRev ? _lineLast - j : _lineFirst + j;

        // Silence before step j. Backwards, the silences keep their places between the same two syllables.
        int StepGap(int j)
        {
            if (j >= _lineCount) return 0;
            if (j == 0) return !_lineRev && _lineFirst == 0 ? Gap(0) : 0;
            return _lineRev ? Gap(K(j - 1)) : Gap(K(j));
        }

        // The melody's pitch for wherever the mouth is, for the modes that move through time freely.
        void MelodyPitch()
        {
            if (_score == null) return;
            var s = _u.SylS;
            if (s.Length == 0) return;
            if (_sylCur >= s.Length) _sylCur = s.Length - 1;
            while (_sylCur + 1 < s.Length && _t >= s[_sylCur + 1]) _sylCur++;
            while (_sylCur > 0 && _t < s[_sylCur]) _sylCur--;
            _noteMidi = Mel(_sylCur);
        }

        float Onset(int k) =>
            (uint)k < (uint)_u.SylS.Length ? (_u.SylV[k] - _u.SylS[k]) / Math.Max(0.05f, Speed) : 0f;

        // `late` is how far the clock has already run past the moment this step was due
        void LineEnter(float late = 0f)
        {
            if (_lineStep >= _lineCount) { _speaking = false; _resting = false; return; }
            float start = Math.Max(_lineClock - late, _lineNom - Onset(K(_lineStep)));
            if (start > _lineClock + 1f) { _resting = true; _lineWait = start; _lineSettled = false; }
            else LineBeginSyllable(_lineClock - start, true);
        }

        void LineBeginSyllable(float late, bool maySettle)
        {
            int j = _lineStep, k = K(j);
            float start = _lineClock - late;
            float nomEnd = _lineNom + Len(k) * TickMs;
            _lineNextNom = nomEnd + StepGap(j + 1) * TickMs;
            float end = j + 1 < _lineCount ? Math.Min(nomEnd, _lineNextNom - Onset(K(j + 1))) : nomEnd;
            float slot = Math.Max(30f, end - start);

            // A syllable squeezed into a short slot runs faster, so its consonants take
            // less time than allowed and the vowel would come early: wait out the difference.
            float natural = Math.Max(1f, _u.SylE[k] - _u.SylS[k]) / Math.Max(0.05f, Speed);
            if (maySettle && slot < natural && _lineNom - start >= Onset(k) - 1f)    // only if it did start early
            {
                float wait = Onset(k) * (1f - slot / natural) - late;
                if (wait > 4f) { _resting = true; _lineSettled = true; _lineWait = _lineClock + wait; return; }
            }

            _resting = false;
            _noteMidi = _score == null ? _lineBase : MelP(_score.PitchAtTime(k, 0f, _lineRev));
            _vowelSeen = false;
            if (!LineHold && slot > natural) slot = -1f;          // short: the syllable at its own length, then silence
            SetSeg(_u.SylS[k], _u.SylH[k], _u.SylE[k], slot, Math.Max(0f, late));
        }

        void LineNext(float late)
        {
            _lineStep++;
            _lineNom = _lineNextNom;
            LineEnter(late);
        }

        void SetSeg(float s, float h, float e, float slot, float carryMs)
        {
            _segS = s; _segH = Math.Clamp(h, s, e); _segE = e;
            float span = Math.Max(1f, e - s);
            float natural = span / Math.Max(0.05f, Speed);
            if (slot < 0f) { _rate = Speed; _holdLeft = 0f; }
            else if (float.IsPositiveInfinity(slot)) { _rate = Speed; _holdLeft = slot; }
            else if (slot < natural) { _rate = span / Math.Max(1f, slot); _holdLeft = 0f; }
            else { _rate = Speed; _holdLeft = slot - natural; }
            _phase = 0;
            _t = s + carryMs * _rate;
            int idx = 0;
            _refSemis = _u.SamplePitch(_segH, ref idx);   // the held vowel sits exactly on the note
        }

        float ScrubTarget()
        {
            float len = Math.Max(1f, _tEnd - _tStart);
            float x = ScrubTarget01 + _scanOff / len;
            if (ScanRate != 0f) x -= MathF.Floor(x);       // scanning loops round the window
            return _tStart + Math.Clamp(x, 0f, 1f) * len;
        }

        // Moves the mouth by one frame of real time. Overshoot is carried from one
        // phase or syllable into the next, so Sync doesn't drift against the tempo.
        void AdvancePlayer(float dt)
        {
            switch (_kind)
            {
                case Kind.Run:
                    _t += dt * Speed;
                    if (_t > _tEnd) _speaking = false;
                    MelodyPitch();
                    break;

                case Kind.Seg:
                case Kind.Sync:
                case Kind.Line:
                    if (!_speaking) break;
                    if (_kind == Kind.Line)
                    {
                        _lineClock += dt;
                        if (_resting)
                        {
                            if (_lineClock >= _lineWait) LineBeginSyllable(_lineClock - _lineWait, !_lineSettled);
                            break;
                        }
                        // A syllable sung over several notes: the pitch moves on at each note's beat, the vowel holds
                        if (_score != null && _lineStep < _lineCount && _score.HasMore(K(_lineStep)))
                            _noteMidi = MelP(_score.PitchAtTime(K(_lineStep), (_lineClock - _lineNom) / Math.Max(1f, TickMs), _lineRev));
                    }
                    if (_phase == 0)
                    {
                        _t += dt * _rate;
                        if (_t >= _segH)
                        {
                            if (_holdLeft > 0f)
                            {
                                _holdLeft -= (_t - _segH) / _rate;
                                _t = _segH; _phase = 1;
                            }
                            else _phase = 2;
                        }
                    }
                    else if (_phase == 1)
                    {
                        _holdLeft -= dt;
                        if (_holdLeft <= 0f) { _phase = 2; _t = _segH - _holdLeft * _rate; }
                    }
                    else _t += dt * _rate;

                    if (_kind == Kind.Line && !_vowelSeen && _lineStep < _lineCount && _t >= _u.SylV[K(_lineStep)])
                    {
                        _vowelSeen = true;
                        LineTrace?.Invoke(_lineStep, _lineClock);
                    }

                    if (_phase == 2 && _t >= _segE)
                    {
                        if (_kind == Kind.Sync && _sylIdx < _sylLast)
                        {
                            float carry = (_t - _segE) / _rate;
                            _sylIdx++;
                            SetSeg(_u.SylS[_sylIdx], _u.SylH[_sylIdx], _u.SylE[_sylIdx], TickMs * SyncTicks, carry);
                            _noteMidi = Mel(_sylIdx);
                        }
                        else if (_kind == Kind.Line) LineNext((_t - _segE) / _rate);
                        else _speaking = false;
                    }
                    break;

                case Kind.Scrub:
                {
                    _scanOff += dt * ScanRate;
                    float target = ScrubTarget();
                    if (ScanRate != 0f || GlideMs <= 0f) _t = target;
                    else _t += (target - _t) * (1f - MathF.Exp(-dt / GlideMs));
                    MelodyPitch();
                    break;
                }
            }
        }

        // ── Pitch helpers (m: Buzz semitone number, A-4 = 57 = 440 Hz) ───────
        static float Hz(float m) => 440f * MathF.Pow(2f, (m - 57f) / 12f);
        static float Semi(float hz) => 57f + 12f * MathF.Log2(hz / 440f);

        bool InScale(int m) => (ScaleMask >> (((m - Root) % 12 + 12) % 12) & 1) != 0;

        float SnapToScale(float m)
        {
            int r = (int)MathF.Round(m);
            float best = r, bestD = 99f;
            for (int d = -6; d <= 6; d++)
            {
                int c = r + d;
                if (!InScale(c)) continue;
                float dist = MathF.Abs(c - m);
                if (dist < bestD) { bestD = dist; best = c; }
            }
            return best;
        }

        float NextScaleAbove(float m)
        {
            int c = (int)MathF.Floor(m + 0.001f) + 1;
            for (int i = 0; i < 12; i++, c++) if (InScale(c)) return c;
            return m + 12f;
        }

        // ── Frame computation ────────────────────────────────────────────────
        void AdvanceFrame()
        {
            Array.Copy(_kTgt, _kPrev, MaxOrder);
            _avPrev = _avTgt; _anPrev = _anTgt; _f0Prev = _f0Tgt;
            AdvancePlayer(FrameMs);
            ComputeFrame();
        }

        void ComputeFrame()
        {
            float av, an, semis;

            if (_kind == Kind.Follow)
            {
                var L = _leader;
                bool live = L != null && L.IsActive && L.MouthLive;
                for (int i = 0; i < 4; i++) { _fSm[i] = L?.MouthF[i] ?? 500f * (i + 1); _bSm[i] = L?.MouthB[i] ?? 100f; }
                if (_fSm[0] <= 0f) { _fSm[0] = 500; _fSm[1] = 1500; _fSm[2] = 2500; _fSm[3] = 3300; }
                av = live ? L.MouthAV : 0f; an = live ? L.MouthAN : 0f;
                semis = L?.MouthSemis ?? 0f;
                if (L != null) _q = L.MouthFeel;
                if (!_speaking) { av = 0; an = 0; }
            }
            else
            {
                var u = _u;
                float t = Math.Clamp(_t, 0f, u.Length);

                // A stop's burst lasts 8 to 15 ms, and less when a syllable is squeezed, so a look at
                // one instant per frame can step straight over it. Look back over the stretch just
                // travelled; if a burst of noise sat inside it, give this frame to the burst.
                bool burst = false;
                if (CatchBursts && _kind != Kind.Scrub && t > _tSeen && t - _tSeen < 120f)
                {
                    u.SampleSource(t, ref _si, out _, out float anHere);
                    float best = anHere * 1.4f + 0.05f, at = t;
                    for (int j = 1; j <= 5; j++)
                    {
                        float tp = _tSeen + (t - _tSeen) * j / 6f;
                        u.SampleSource(tp, ref _si, out float avP, out float anP);
                        if (anP > best && avP < 0.3f) { best = anP; at = tp; }
                    }
                    if (at != t) { t = at; burst = true; }
                }
                _tSeen = Math.Clamp(_t, 0f, u.Length);

                u.SampleFormants(_kind == Kind.Run ? Math.Min(t, _tEnd) : t, ref _fi, _fTrack, _bTrack);
                u.SampleSource(t, ref _si, out av, out an);
                semis = u.SamplePitch(t, ref _pi);
                u.SampleFeel(t, ref _qi, ref _qRaw);

                if (_kind == Kind.Seg || _kind == Kind.Line) semis -= _refSemis;
                else if (_kind == Kind.Scrub && !InflSnap) semis = 0f;

                if (!_speaking || _resting) { av = 0; an = 0; }
                else if (_kind == Kind.Run)
                {
                    // 5 ms fades where a word window chops the phrase
                    float env = 1f;
                    if (_tStart > 0f) env = Math.Min(env, (t - _tStart) / 5f + 0.2f);
                    if (_tEnd < u.Length) env = Math.Min(env, (_tEnd - t) / 5f);
                    env = Math.Clamp(env, 0f, 1f);
                    av *= env; an *= env;
                }

                // Glide: the mouth eases toward where the playhead says it should be
                float c = (_snap || burst || GlideMs <= 0f || _kind == Kind.Scrub) ? 1f : 1f - MathF.Exp(-FrameMs / GlideMs);
                for (int i = 0; i < 4; i++)
                {
                    _fSm[i] += (_fTrack[i] - _fSm[i]) * c;
                    _bSm[i] += (_bTrack[i] - _bSm[i]) * c;
                }
                _avSm += (av - _avSm) * c; _anSm += (an - _anSm) * c;
                av = _avSm; an = _anSm;
                _q.Open += (_qRaw.Open - _q.Open) * c; _q.Bright += (_qRaw.Bright - _q.Bright) * c;
                _q.Hard += (_qRaw.Hard - _q.Hard) * c; _q.Sung += (_qRaw.Sung - _q.Sung) * c;
                _q.Rhyme += (_qRaw.Rhyme - _q.Rhyme) * c; _q.Allit += (_qRaw.Allit - _q.Allit) * c;
                MouthFeel = _q;

                for (int i = 0; i < 4; i++) { MouthF[i] = _fSm[i]; MouthB[i] = _bSm[i]; }
                MouthAV = av; MouthAN = an; MouthSemis = semis; MouthLive = _speaking;
            }

            // React: the feel of the sound moves level, ring and vibrato here, tuning and size below
            float lift = ReactLevel * (_q.Open - 0.5f) * 2f            // open vowels, up to ±6 dB
                       + ReactRhyme * _q.Rhyme * 0.6f                  // rhymes, up to ±3.6 dB
                       + ReactAllit * _q.Allit * 0.5f;                 // voiced alliterating consonants
            if (lift != 0f) av *= MathF.Pow(2f, lift);
            if (ReactAllit != 0f) an *= MathF.Pow(2f, ReactAllit * _q.Allit);       // their noise, up to ±6 dB
            _consEff = Math.Clamp(ConsTune + ReactRing * (_q.Hard - 0.5f) + ReactAllit * _q.Allit * 0.4f, 0f, 1f);
            _vibMul = Math.Clamp((1f + ReactVib * (_q.Sung * 2f - 1f)) * (1f + ReactRhyme * _q.Rhyme * 0.5f), 0f, 2.5f);

            if (Excitation == 4) { an += av * 0.8f; av = 0f; }
            else if (Breath > 0f) an += av * Breath * 0.4f;

            // Pitch: note + inflection, optionally snapped to the scale, then portamento
            float target = _noteMidi + Inflection * semis;
            if (InflSnap) target = SnapToScale(target);
            if (_snap || PortaMs <= 0f) _pitchSm = target;
            else _pitchSm += (target - _pitchSm) * (1f - MathF.Exp(-FrameMs / PortaMs));
            float f0 = Math.Clamp(Hz(_pitchSm), 25f, ChipRate / 3f);
            _f0Tgt = f0;

            // Formants → poles: voice size, then tuning, dropping any above ~0.92 Nyquist
            float nyq = ChipRate * 0.5f;
            float fs = FormantScale * MathF.Pow(2f, ReactSize * (_q.Bright - 0.5f) * 0.5f);       // up to ±19%
            float tune = Math.Clamp(TuneAmt + ReactTune * (_q.Open - 0.5f) + ReactRhyme * _q.Rhyme * 0.6f, 0f, 1f);
            float prevTarget = 0f;
            int np = 0;
            for (int i = 0; i < 4; i++)
            {
                float f = _fSm[i] * fs;
                float b = Math.Max(30f, _bSm[i] * fs);
                if (tune > 0f && f > 50f)
                {
                    float tf;
                    if (TuneMode == 0)
                    {
                        float n = MathF.Max(1f, MathF.Round(f / f0));
                        tf = n * f0;
                        if (tf <= prevTarget * 1.001f) tf = prevTarget + f0;        // one harmonic each
                    }
                    else
                    {
                        float m = SnapToScale(Semi(f));
                        tf = Hz(m);
                        if (tf <= prevTarget * 1.001f) tf = Hz(NextScaleAbove(Semi(prevTarget)));
                    }
                    prevTarget = tf;
                    f *= MathF.Pow(tf / f, tune);
                    b = Math.Max(35f, b * (1f - 0.75f * tune));     // tuned resonances ring
                }
                if (f > 0.92f * nyq || f < 50f) continue;
                _F[np] = f; _B[np] = b; np++;
            }
            float f5 = 4500f * fs;
            if (f5 < 0.92f * nyq && np < 5) { _F[np] = f5; _B[np] = 300f * fs; np++; }

            int order = PolyFromPoles(np);
            StepDown(order);
            for (int i = order; i < MaxOrder; i++) _kTgt[i] = 0f;

            if (CoefBits < 11)
            {
                for (int i = 0; i < order; i++)
                {
                    int bits = i < 2 ? CoefBits : (i < 4 ? CoefBits - 1 : CoefBits - 2);
                    _kTgt[i] = QuantK(_kTgt[i], Math.Max(2, bits));
                }
                av = QuantAmp(av); an = QuantAmp(an);
            }

            // A harmonic sitting on a narrow resonance is much louder than the
            // noise-power normalisation expects, so trim voiced level as Tune rises.
            _avTgt = av * (1f - 0.25f * tune); _anTgt = an;
            FrameTrace?.Invoke(_t, _avTgt, _anTgt);
        }

        int PolyFromPoles(int np)
        {
            var a = _poly;
            Array.Clear(a, 0, a.Length);
            a[0] = 1.0;
            int order = 0;
            for (int p = 0; p < np; p++)
            {
                double r = Math.Exp(-Math.PI * _B[p] / ChipRate);
                double th = 2.0 * Math.PI * _F[p] / ChipRate;
                double c1 = -2.0 * r * Math.Cos(th), c2 = r * r;
                for (int i = order + 2; i >= 1; i--)
                {
                    double v = a[i] + c1 * a[i - 1];
                    if (i >= 2) v += c2 * a[i - 2];
                    a[i] = v;
                }
                order += 2;
            }
            return order;
        }

        void StepDown(int order)
        {
            var a = _poly; var tmp = _tmp;
            for (int m = order; m >= 1; m--)
            {
                double k = a[m];
                if (k > 0.9995) k = 0.9995; else if (k < -0.9995) k = -0.9995;
                _kTgt[m - 1] = (float)k;
                double den = 1.0 - k * k;
                for (int i = 1; i < m; i++) tmp[i] = (a[i] - k * a[m - i]) / den;
                for (int i = 1; i < m; i++) a[i] = tmp[i];
            }
        }

        // Mid-rise quantiser in the arcsine domain: never reaches ±1, so the
        // lattice stays stable however coarse the bits.
        static float QuantK(float k, int bits)
        {
            int L = 1 << (bits - 1);
            float u = MathF.Asin(Math.Clamp(k, -0.9999f, 0.9999f)) * (2f / MathF.PI);
            int i = (int)MathF.Floor(u * L);
            if (i < -L) i = -L; else if (i > L - 1) i = L - 1;
            float q = (i + 0.5f) / L;
            return MathF.Sin(q * MathF.PI * 0.5f);
        }

        // 2 dB energy steps with a -40 dB floor, like a 4-bit energy table
        static float QuantAmp(float a)
        {
            if (a < 0.01f) return 0f;
            float db = 20f * MathF.Log10(a);
            db = MathF.Round(db / 2f) * 2f;
            return MathF.Pow(10f, db / 20f);
        }

        void EnsureChirp()
        {
            if (_chirpRate == ChipRate && _chirpLen > 0) return;
            _chirpRate = ChipRate;
            int len = Math.Clamp((int)(0.0055f * ChipRate), 8, 256);
            if (_chirp.Length < len) _chirp = new float[len];
            // A short downward-swept, decaying burst: bright and buzzy like an
            // LPC chip's chirp excitation (an original shape, not a ROM table).
            double e = 0, phase = 0;
            for (int j = 0; j < len; j++)
            {
                double x = (double)j / len;
                double f = 2600.0 - 2200.0 * x;
                double env = Math.Exp(-3.0 * x) * (j == 0 ? 1.6 : 1.0);
                double v = Math.Sin(phase) * env + (j == 0 ? 0.6 : 0.0);
                phase += 2.0 * Math.PI * f / ChipRate;
                _chirp[j] = (float)v;
                e += v * v;
            }
            float norm = (float)(1.0 / Math.Sqrt(e));
            double mean = 0;
            for (int j = 0; j < len; j++) { _chirp[j] *= norm; mean += _chirp[j]; }
            mean /= len;
            for (int j = 0; j < len; j++) _chirp[j] -= (float)mean;
            _chirpLen = len;
        }

        // ── One chip-rate sample ─────────────────────────────────────────────
        public float Next()
        {
            if (!IsActive) return 0f;

            // Declick ramp (~3 ms)
            float ramp = 1f / (0.003f * ChipRate);
            if (_gain < _gainTarget) _gain = Math.Min(_gainTarget, _gain + ramp);
            else if (_gain > _gainTarget) _gain = Math.Max(_gainTarget, _gain - ramp);
            if (_gain <= 0f && _gainTarget <= 0f)
            {
                if (_pending) Begin(_req);
                else { IsActive = false; return 0f; }
                if (!IsActive) return 0f;
            }

            // Interpolation step boundary
            if (_stepLeft <= 0f)
            {
                if (_step >= Steps) { AdvanceFrame(); _step = 0; }
                _step++;
                float x = (float)_step / Steps;
                double gp = 1.0;
                for (int i = 0; i < MaxOrder; i++)
                {
                    float k = _kPrev[i] + (_kTgt[i] - _kPrev[i]) * x;
                    _kCur[i] = k;
                    gp *= 1.0 - (double)k * k;
                }
                _g = (float)Math.Sqrt(Math.Max(gp, 1e-12));
                _av = _avPrev + (_avTgt - _avPrev) * x;
                _an = _anPrev + (_anTgt - _anPrev) * x;
                _f0 = _f0Prev + (_f0Tgt - _f0Prev) * x;

                float stepSamples = Math.Max(1f, FrameMs * 0.001f * ChipRate / Steps);
                if (VibDepth > 0f)
                {
                    float stepMs = stepSamples * 1000f / ChipRate;
                    _vibMs += stepMs;
                    _vibPhase += VibRateHz * stepMs * 0.001f;
                    if (_vibPhase >= 1f) _vibPhase -= 1f;
                    float env = Math.Clamp((_vibMs - 150f) / 250f, 0f, 1f);   // vibrato comes in after the attack
                    _f0 *= MathF.Pow(2f, VibDepth * _vibMul * env * MathF.Sin(2f * MathF.PI * _vibPhase) / 12f);
                }
                _stepLeft += stepSamples;
            }
            _stepLeft -= 1f;

            // Pitched source
            float v = 0f;
            if (_av > 0f)
            {
                float P = ChipRate / _f0;
                float ph = _phaseOsc;
                switch (Excitation)
                {
                    case 0:
                    {
                        int j = (int)(ph * P);
                        v = j < _chirpLen ? _chirp[j] * MathF.Sqrt(P) : 0f;
                        break;
                    }
                    case 1:
                    {
                        // Derivative of a Rosenberg glottal pulse, unit RMS
                        const float Tp = 0.4f, Tn = 0.16f, Rms = 3.2856f;
                        if (ph < Tp) v = (0.5f * MathF.PI / Tp) * MathF.Sin(MathF.PI * ph / Tp) / Rms;
                        else if (ph < Tp + Tn) v = -(MathF.PI / (2f * Tn)) * MathF.Sin(MathF.PI * (ph - Tp) / (2f * Tn)) / Rms;
                        break;
                    }
                    case 2: v = (2f * ph - 1f) * 1.7320508f; break;
                    case 3: v = ph < 0.5f ? 1f : -1f; break;
                }
                v *= ExcTrim[Excitation];
            }
            _phaseOsc += _f0 / ChipRate;
            if (_phaseOsc >= 1f) _phaseOsc -= 1f;

            // Noise, optionally through a comb tuned to the note (pitched consonants)
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            float noise = ((_rng & 0x10000u) != 0 ? 1f : -1f) * _an;
            if (_consEff > 0f)
            {
                float d = Math.Clamp(ChipRate / (_f0 * ConsRatio), 2f, _comb.Length - 2);
                if (!(d >= 2f)) d = 2f;                                // a pitch that is not a number must not reach the index
                float rp = _combW - d;
                if (rp < 0f) rp += _comb.Length;
                int i0 = (int)rp;
                // A read point a hair below zero rounds up to exactly the buffer length when the length is added
                if ((uint)i0 >= (uint)_comb.Length) { i0 = 0; rp = 0f; }
                int i1 = i0 + 1 == _comb.Length ? 0 : i0 + 1;
                float fr = rp - i0;
                float delayed = _comb[i0] + (_comb[i1] - _comb[i0]) * fr;
                float fb = 0.5f + 0.43f * _consEff;
                float y = noise + fb * delayed;
                if (y > 8f) y = 8f; else if (y < -8f) y = -8f;
                _comb[_combW] = y;
                if (++_combW == _comb.Length) _combW = 0;
                float mix = Math.Min(1f, 2f * _consEff);
                noise += (y * MathF.Sqrt(1f - fb * fb) * 1.6f - noise) * mix;
            }

            double x0 = (_av * v + noise) * _g;

            // 10-pole lattice (synthesis form)
            double f = x0;
            for (int m = MaxOrder; m >= 1; m--)
            {
                double k = _kCur[m - 1];
                f -= k * _bd[m - 1];
                _bd[m] = k * f + _bd[m - 1];
            }
            _bd[0] = f;
            if (f > 1e6 || f < -1e6 || double.IsNaN(f)) { Array.Clear(_bd, 0, _bd.Length); f = 0; }  // safety

            float outp = (float)f * OutLevel * _amp * _gain;
            if (outp > 1f) outp = 1f; else if (outp < -1f) outp = -1f;
            if (DacBits < 16)
            {
                float L = 1 << (DacBits - 1);
                outp = MathF.Round(outp * L) / L;
            }

            // After the mouth stops: ring out briefly, then go idle
            if (!_speaking && _kind != Kind.Follow)
            {
                if (--_tailSamples <= 0 && !_pending) IsActive = false;
            }
            return outp;
        }
    }
}
